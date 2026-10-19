using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using System.Reflection;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebfleetTourDispatchServiceTests
{
    [Fact]
    public async Task PreviewCannotSendCustomerStopWithoutCurrentOrderData()
    {
        var tour = new TourRecord { Id = 20, Date = "19.10.2026", Stops =
            [new TourStopRecord { Auftragsnummer = "221878", Address = "Veraltete Adresse", Lat = 47, Lon = 8 }] };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WebfleetTourDispatchService().PreviewSynchronizationAsync(tour,
                new Vehicle { WebfleetObjectUid = "vehicle" }, new WebfleetConnectionSettings(), new WebfleetConnectService()));
        Assert.Contains("aktuelle Auftragsdaten", error.Message);
    }

    [Theory]
    [InlineData(double.PositiveInfinity, 8)]
    [InlineData(91, 8)]
    [InlineData(47, 181)]
    public void ManualStopsRejectInvalidCoordinates(double lat, double lon)
    {
        var tour = new TourRecord { Stops = [new TourStopRecord { StopKind = "manual", Lat = lat, Lon = lon }] };
        Assert.Contains(new WebfleetTourDispatchService([]).Validate(tour, new Vehicle { WebfleetObjectUid = "vehicle" }),
            error => error.Contains("Ungültige Koordinaten"));
    }

    [Fact]
    public void ValidationUsesCurrentOrderCoordinatesWhenStoredTourCoordinatesAreMissing()
    {
        var order = new Order { Id = "221878", Location = new GeoPoint(47.49, 7.58),
            DeliveryAddress = new DeliveryAddressInfo { Street = "Hauptstrasse 41", PostalCode = "4153", City = "Reinach" } };
        var tour = new TourRecord { Stops = [new TourStopRecord { Auftragsnummer = order.Id }] };
        Assert.Empty(new WebfleetTourDispatchService([order]).Validate(tour, new Vehicle { WebfleetObjectUid = "vehicle" }));
    }

    [Theory]
    [InlineData("Hauptstrasse 41", "8600", "Dübendorf", true)]
    [InlineData("", "4153", "Reinach BL", true)]
    [InlineData("Hauptstrasse 41", "4153", "Reinach BL", false)]
    public void SynchronizationDetectsWrongOrMissingAddressEvenWithCorrectCoordinates(string street, string zip, string city, bool changed)
    {
        var desired = new WebfleetDestinationOrderRequest("vehicle", "T20-4-221878", "Tour", 47.4921842, 7.5896901,
            "CH", "4153", "Reinach BL", "Hauptstrasse 41", new DateOnly(2026,10,19), null, null);
        var remote = new WebfleetOrderSnapshot(desired.OrderId, desired.OrderText, desired.Latitude, desired.Longitude,
            street, desired.ScheduledDate, null, null, zip, city);
        var method = typeof(WebfleetTourDispatchService).GetMethod("IsChanged", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(changed, (bool)method.Invoke(null,[remote,desired])!);
    }

    [Fact]
    public void DestinationUsesCurrentDeliveryAddressAndCoordinatesInsteadOfStaleTourStop()
    {
        var order = new Order
        {
            Id = "221878", Address = "Zürichstrasse 131, 8600 Dübendorf",
            DeliveryAddress = new DeliveryAddressInfo { Street = "Hauptstrasse", HouseNumber = "41", PostalCode = "4153", City = "Reinach BL" },
            Location = new GeoPoint(47.4921842, 7.5896901)
        };
        var stop = new TourStopRecord { Auftragsnummer = order.Id, Address = order.Address, Lat = 47.4, Lon = 8.6 };
        var destination = new WebfleetTourDispatchService([order]).ResolveDestination(stop);
        Assert.Equal("Hauptstrasse 41", destination.Street);
        Assert.Equal("4153", destination.PostalCode);
        Assert.Equal("Reinach BL", destination.City);
        Assert.Equal(47.4921842, destination.Latitude);
        Assert.Equal(7.5896901, destination.Longitude);
        Assert.Equal(order.Address, stop.Address);
        order.DeliveryAddress.Street = "";
        order.DeliveryAddress.HouseNumber = "";
        Assert.Throws<InvalidOperationException>(() => new WebfleetTourDispatchService([order]).ResolveDestination(stop));
    }

    [Fact]
    public void DispatchNeverFallsBackToOrderAddressWhenOrderMissingOrPinNeedsReview()
    {
        var stop = new TourStopRecord { Auftragsnummer = "221878", Address = "Falsche Adresse", Lat = 47, Lon = 8 };
        Assert.Throws<InvalidOperationException>(() => new WebfleetTourDispatchService([]).ResolveDestination(stop));
        var order = new Order { Id = "221878", ManualLocationRequiresReview = true, Location = new GeoPoint(47,8),
            DeliveryAddress = new DeliveryAddressInfo { Street = "Hauptstrasse 41", PostalCode = "4153", City = "Reinach" } };
        Assert.Throws<InvalidOperationException>(() => new WebfleetTourDispatchService([order]).ResolveDestination(stop));
        stop.StopKind = "manual";
        Assert.Equal("Falsche Adresse", new WebfleetTourDispatchService([]).ResolveDestination(stop).Street);
    }

    [Theory]
    [InlineData("manual", "stop-1")]
    [InlineData("", "manual:123")]
    [InlineData(" MANUAL ", "stop-1")]
    public void Validate_ManualStopNeedsCoordinatesButNoOrderNumber(string kind, string id)
    {
        var stop = new TourStopRecord { Id = id, StopKind = kind, Order = 2, Lat = 47.58, Lon = 9.06 };
        var tour = new TourRecord { Stops = [stop] };
        var vehicle = new Vehicle { WebfleetObjectUid = "object-uid" };
        var service = new WebfleetTourDispatchService();

        Assert.Empty(service.Validate(tour, vehicle));
        var method = typeof(WebfleetTourDispatchService).GetMethod("IsDispatchableStop", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [stop])));

        stop.Lat = null;
        Assert.Equal("Stopp 2: Koordinaten fehlen.", Assert.Single(service.Validate(tour, vehicle)));
    }

    [Fact]
    public void Validate_CustomerStopStillRequiresOrderNumber()
    {
        var tour = new TourRecord
        {
            Stops = [new TourStopRecord { Id = "order-1", Order = 1, Lat = 47.58, Lon = 9.06 }]
        };

        var errors = new WebfleetTourDispatchService().Validate(tour, new Vehicle { WebfleetObjectUid = "object-uid" });

        Assert.Equal("Stopp 1: Auftragsnummer fehlt.", Assert.Single(errors));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsChanged_CorrectsWrongTourDayEvenWithoutArrivalTime(bool hasArrival)
    {
        var desired = new WebfleetDestinationOrderRequest("vehicle", "order", "Tour", 47, 9,
            "CH", "", "", "Street", new DateOnly(2026, 9, 22),
            hasArrival ? new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.FromHours(2)) : null,
            hasArrival ? 60 : null);
        var remote = new WebfleetOrderSnapshot("order", "Tour", 47, 9, "Street",
            new DateOnly(2026, 9, 21), hasArrival ? new TimeOnly(9, 0) : null, hasArrival ? 60 : null);
        var method = typeof(WebfleetTourDispatchService).GetMethod("IsChanged", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method.Invoke(null, [remote, desired])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [remote with { ScheduledDate = desired.ScheduledDate }, desired])));
    }

    [Fact]
    public void Validate_IgnoresLegacyCompanyStartStop()
    {
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord { Id = "__company_start__", Order = 1 },
                new TourStopRecord { Id = "order-1", Order = 2, Auftragsnummer = "221949", Lat = 47.58, Lon = 9.06 }
            ]
        };
        var vehicle = new Vehicle { WebfleetObjectUid = "object-uid" };

        var errors = new WebfleetTourDispatchService().Validate(tour, vehicle);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_IgnoresCompanyEndStopWithoutOrderNumber()
    {
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord { Id = "__company_start__", StopKind = "company", Order = 1 },
                new TourStopRecord { Id = "order-1", Order = 2, Auftragsnummer = "221949", Lat = 47.58, Lon = 9.06 },
                new TourStopRecord { Id = "__company_end__", StopKind = "company", Order = 3, Lat = 47.58, Lon = 9.06 }
            ]
        };
        var vehicle = new Vehicle { WebfleetObjectUid = "object-uid" };

        var errors = new WebfleetTourDispatchService().Validate(tour, vehicle);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_IgnoresCanonicalCompanyStartAndEnd()
    {
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord { Id = TourStopIdentity.CompanyStartStopId, Auftragsnummer = TourStopIdentity.CompanyStartOrderNumber, Order = 1 },
                new TourStopRecord { Id = "order-1", Order = 2, Auftragsnummer = "221949", Lat = 47.58, Lon = 9.06 },
                new TourStopRecord { Id = TourStopIdentity.CompanyEndStopId, Auftragsnummer = TourStopIdentity.CompanyEndOrderNumber, Order = 3, Lat = 47.58, Lon = 9.06 }
            ]
        };
        var vehicle = new Vehicle { WebfleetObjectUid = "object-uid" };

        var errors = new WebfleetTourDispatchService().Validate(tour, vehicle);

        Assert.Empty(errors);
    }

    [Fact]
    public void IsDispatchableStop_ExcludesBothCompanyStops()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("IsDispatchableStop", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var start = Assert.IsType<bool>(method.Invoke(null, [new TourStopRecord { Id = TourStopIdentity.CompanyStartStopId }]));
        var end = Assert.IsType<bool>(method.Invoke(null, [new TourStopRecord { Id = TourStopIdentity.CompanyEndStopId }]));
        var customer = Assert.IsType<bool>(method.Invoke(null, [new TourStopRecord { Id = "order-1", Auftragsnummer = "221949" }]));

        Assert.False(start);
        Assert.False(end);
        Assert.True(customer);
    }

    [Fact]
    public void BuildWebfleetArrivalWindow_UsesRoundedCalculatedArrivalAndPreservesTolerance()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("BuildWebfleetArrivalWindow", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var tour = new TourRecord { Date = "22.09.2026" };
        var stop = new TourStopRecord
        {
            PlannedArrivalOptimistic = "07:42",
            PlannedArrival = "07:42",
            PlannedArrivalPessimistic = "07:42"
        };

        var result = ((DateTimeOffset? PlannedArrival, int? ArrivalToleranceMinutes))method.Invoke(null, [tour, stop])!;

        Assert.Equal(new DateTime(2026, 9, 22, 7, 45, 0), result.PlannedArrival!.Value.DateTime);
        Assert.Equal(60, result.ArrivalToleranceMinutes);
    }

    [Theory]
    [InlineData("08:19", "08:00", "09:00", 8, 15)]
    [InlineData("08:49", "08:30", "09:30", 8, 45)]
    [InlineData("09:22", "09:00", "10:00", 9, 30)]
    [InlineData("09:21", "09:00", "10:00", 9, 15)]
    [InlineData("09:52", "09:30", "10:30", 10, 0)]
    [InlineData("09:30", "09:00", "10:00", 9, 30)]
    public void BuildWebfleetArrivalWindow_RoundsCalculatedTimeIndependentlyOfWindow(
        string arrival, string optimistic, string pessimistic, int hour, int minute)
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("BuildWebfleetArrivalWindow", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tour = new TourRecord { Date = "22.09.2026" };
        var stop = new TourStopRecord
        {
            PlannedArrival = arrival,
            PlannedArrivalOptimistic = optimistic,
            PlannedArrivalPessimistic = pessimistic
        };

        var result = ((DateTimeOffset? PlannedArrival, int? ArrivalToleranceMinutes))method.Invoke(null, [tour, stop])!;

        Assert.Equal(new DateTime(2026, 9, 22, hour, minute, 0), result.PlannedArrival!.Value.DateTime);
        Assert.Equal(60, result.ArrivalToleranceMinutes);

        stop.PlannedArrivalOptimistic = string.Empty;
        stop.PlannedArrivalPessimistic = string.Empty;
        result = ((DateTimeOffset? PlannedArrival, int? ArrivalToleranceMinutes))method.Invoke(null, [tour, stop])!;
        Assert.Equal(new DateTime(2026, 9, 22, hour, minute, 0), result.PlannedArrival!.Value.DateTime);
        Assert.Equal(0, result.ArrivalToleranceMinutes);
    }

    [Fact]
    public void IsOrderAlreadyPresent_RecognizesWebfleetDuplicateOrderResponse()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("IsOrderAlreadyPresent", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = Assert.IsType<bool>(method.Invoke(null, [new InvalidOperationException("WEBFLEET-Verbindung fehlgeschlagen: 2515,Order already exists")]));

        Assert.True(result);
    }

    [Fact]
    public void BuildOrderText_UsesDispatchPositionInsteadOfInternalTourPosition()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("BuildOrderText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var tour = new TourRecord { Name = "Tour 22.09.2026" };
        var stop = new TourStopRecord { Order = 2, Name = "Tunap AG" };

        var text = Assert.IsType<string>(method.Invoke(null, [tour, stop, 1]));

        Assert.Equal("Tour 22.09.2026 · Stopp 1: Tunap AG", text);
    }

    [Fact]
    public void BuildOrderId_UsesUnpaddedTourAndEffectiveStopPosition()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("BuildOrderId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var orderId = Assert.IsType<string>(method.Invoke(null, [2, 1, "221770"]));

        Assert.Equal("T2-1-221770", orderId);
    }
}
