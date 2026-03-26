using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class MapRouteServiceTests
{
    [Fact]
    public void SwapStops_ReordersStops_AndReindexesPositions()
    {
        var service = new MapRouteService();
        var stops = new[]
        {
            new MapRouteStop(1, "A", "A", "Addr A", 47.1, 8.1),
            new MapRouteStop(2, "B", "B", "Addr B", 47.2, 8.2),
            new MapRouteStop(3, "C", "C", "Addr C", 47.3, 8.3)
        };

        var swapped = service.SwapStops(stops, "A", "C");

        Assert.Equal(["B", "C", "A"], swapped.Select(x => x.OrderId));
        Assert.Equal([1, 2, 3], swapped.Select(x => x.Position));
    }

    [Fact]
    public void SwapStops_LeavesRouteUnchanged_ForInvalidOrEqualIds()
    {
        var service = new MapRouteService();
        var stops = new[]
        {
            new MapRouteStop(7, "A", "A", "Addr A", 47.1, 8.1),
            new MapRouteStop(8, "B", "B", "Addr B", 47.2, 8.2)
        };

        var unchangedUnknown = service.SwapStops(stops, "X", "B");
        var unchangedSame = service.SwapStops(stops, "A", "a");

        Assert.Equal(["A", "B"], unchangedUnknown.Select(x => x.OrderId));
        Assert.Equal(["A", "B"], unchangedSame.Select(x => x.OrderId));
        Assert.Equal([1, 2], unchangedUnknown.Select(x => x.Position));
        Assert.Equal([1, 2], unchangedSame.Select(x => x.Position));
    }

    [Fact]
    public void MoveStop_MovesWithinBounds_AndIgnoresOutOfBounds()
    {
        var service = new MapRouteService();
        var stops = new[]
        {
            new MapRouteStop(1, "A", "A", "Addr A", 47.1, 8.1),
            new MapRouteStop(2, "B", "B", "Addr B", 47.2, 8.2),
            new MapRouteStop(3, "C", "C", "Addr C", 47.3, 8.3)
        };

        var movedDown = service.MoveStop(stops, "A", 1);
        var outOfBounds = service.MoveStop(stops, "A", -1);

        Assert.Equal(["B", "A", "C"], movedDown.Select(x => x.OrderId));
        Assert.Equal(["A", "B", "C"], outOfBounds.Select(x => x.OrderId));
        Assert.Equal([1, 2, 3], movedDown.Select(x => x.Position));
    }

    [Fact]
    public void BuildTour_AppliesFallbacks_AndBuildsOrderedStops()
    {
        var service = new MapRouteService();
        var stops = new[]
        {
            new MapRouteStop(2, "B-200", "Kunde B", "B-Strasse", 47.2, 8.2),
            new MapRouteStop(1, "A-100", "Kunde A", "A-Strasse", 47.1, 8.1)
        };

        var tour = service.BuildTour(
            stops,
            nextTourId: 12,
            routeName: " ",
            routeDate: "invalid-date",
            routeStartTime: " ",
            companyName: "GAWELA",
            companyAddress: "Musterstrasse 1, 8000 Zuerich",
            companyLocation: new GeoPoint(47.35, 8.52),
            defaultServiceMinutes: -5);

        Assert.Equal(12, tour.Id);
        Assert.StartsWith("Karte Tour ", tour.Name);
        Assert.Equal($"Karte Tour {DateOnly.FromDateTime(DateTime.Today):dd.MM.yyyy}", tour.Name);
        Assert.Equal("08:00", tour.StartTime);
        Assert.Equal("car", tour.RouteMode);
        Assert.Equal("FIRMA-START", tour.Stops[0].Auftragsnummer);
        Assert.Equal("A-100", tour.Stops[1].Auftragsnummer);
        Assert.Equal("B-200", tour.Stops[2].Auftragsnummer);
        Assert.Equal("FIRMA-ENDE", tour.Stops[3].Auftragsnummer);
        Assert.Equal(1, tour.Stops[0].Order);
        Assert.Equal(2, tour.Stops[1].Order);
        Assert.Equal(3, tour.Stops[2].Order);
        Assert.Equal(4, tour.Stops[3].Order);
        Assert.Equal(0, tour.Stops[0].ServiceMinutes);
    }

    [Fact]
    public void BuildTour_ThrowsForEmptyRoute()
    {
        var service = new MapRouteService();

        Assert.Throws<ArgumentException>(() =>
            service.BuildTour(Array.Empty<MapRouteStop>(), 1, "Tour", "21.03.2026", "08:00"));
    }

    [Fact]
    public void DetermineNextTourId_UsesMaxPlusOne_AndHandlesEmptySet()
    {
        var service = new MapRouteService();
        var tours = new[]
        {
            new TourRecord { Id = 10 },
            new TourRecord { Id = 3 }
        };

        var nextForList = service.DetermineNextTourId(tours);
        var nextForEmpty = service.DetermineNextTourId(Array.Empty<TourRecord>());

        Assert.Equal(11, nextForList);
        Assert.Equal(1, nextForEmpty);
    }
}
