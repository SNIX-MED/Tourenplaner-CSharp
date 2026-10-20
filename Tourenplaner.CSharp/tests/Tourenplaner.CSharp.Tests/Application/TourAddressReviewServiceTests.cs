using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourAddressReviewServiceTests
{
    private static TourRecord Tour() => new()
    {
        Id = 7, Date = "26.10.2026", StartTime = "08:00",
        Stops = [new() { Id = "manual:start", StopKind = "manual", Order = 1, Lat = 47, Lng = 8 },
            new() { Id = "pause:1", StopKind = "pause", Order = 2, ServiceMinutes = 30 },
            new() { Id = "auftrag:A-1", Auftragsnummer = "A-1", Order = 3, Lat = 48, Lng = 9 }],
        TravelTimeCache = new() { ["old"] = 123 }, RouteGeometryPoints = [new(47, 8), new(47.1, 8.1)]
    };

    private static Order Order(bool manualReview = false) => new()
    {
        Id = "A-1", Location = new(48, 9), ManualLocationRequiresReview = manualReview,
        DeliveryAddress = new() { Street = "Neu", HouseNumber = "1", PostalCode = "8000", City = "Zürich" }
    };

    [Fact]
    public async Task Recalculation_PreservesOrderAndPause_AndKeepsReviewUntilAcknowledged()
    {
        var tour = Tour();
        TourAddressReviewService.Mark(tour, ["A-1", "A-1"]);
        Assert.Empty(tour.TravelTimeCache);
        Assert.Empty(tour.RouteGeometryPoints);
        await TourAddressReviewService.RecalculateAsync(tour, [Order()], new(), new(), "test", points =>
        {
            Assert.Equal(new[] { new GeoPoint(47, 8), new GeoPoint(48, 9) }, points);
            return Task.FromResult(new OsrmRouteResult(points, [new OsrmRouteLeg(new RouteLegTravelTimeProfile(10, 20, 30), 45)]));
        });
        Assert.Equal(new[] { "manual:start", "pause:1", "auftrag:A-1" }, tour.Stops.Select(s => s.Id));
        Assert.Equal(0, tour.TravelTimeCache["manual:start|pause:1"]);
        Assert.Equal(20, tour.TravelTimeCache["pause:1|auftrag:A-1"]);
        Assert.Equal("08:50", tour.Stops[2].PlannedArrival);
        Assert.Empty(tour.AddressReviewRoutingError);
        Assert.Single(tour.AddressReviewOrderIds);
        var roundTrip = JsonSerializer.Deserialize<TourRecord>(JsonSerializer.Serialize(tour))!;
        Assert.Single(roundTrip.AddressReviewOrderIds);
        Assert.Contains(new WebfleetTourDispatchService([Order()]).Validate(roundTrip, new() { WebfleetObjectUid = "vehicle" }),
            e => e.Contains("Planung prüfen"));
        roundTrip.AddressReviewOrderIds.Clear();
        Assert.Empty(new WebfleetTourDispatchService([Order()]).Validate(roundTrip, new() { WebfleetObjectUid = "vehicle" }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnresolvedDestination_DoesNotBuildRoute(bool manualReview)
    {
        var tour = Tour();
        var order = Order(manualReview);
        if (!manualReview) order.Location = null;
        TourAddressReviewService.Mark(tour, [order.Id]);
        await TourAddressReviewService.RecalculateAsync(tour, [order], new(), new(), "test",
            _ => throw new Exception("Must not route an unresolved destination"));
        Assert.Contains("Fahrtziel", tour.AddressReviewRoutingError);
        Assert.Single(tour.AddressReviewOrderIds);
        Assert.Empty(tour.RouteGeometryPoints);
    }

    [Fact]
    public async Task RoutingFailure_RemainsVisibleAndBlocksDispatch()
    {
        var tour = Tour();
        TourAddressReviewService.Mark(tour, ["A-1"]);
        await TourAddressReviewService.RecalculateAsync(tour, [Order()], new(), new(), "test", _ => Task.FromResult(OsrmRouteResult.Empty));
        Assert.Contains("fehlgeschlagen", tour.AddressReviewRoutingError);
        Assert.Empty(tour.TravelTimeCache);
        Assert.Contains(new WebfleetTourDispatchService([Order()]).Validate(tour, new() { WebfleetObjectUid = "vehicle" }),
            e => e.Contains("fehlgeschlagen"));
    }
}
