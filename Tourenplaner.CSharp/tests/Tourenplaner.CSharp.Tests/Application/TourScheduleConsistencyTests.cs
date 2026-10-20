using System.Text.Json;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourScheduleConsistencyTests
{
    private static TourRecord CreateTour() => new()
    {
        Date = "28.10.2026", StartTime = "07:17",
        Stops = [new() { Id = "start", Auftragsnummer = "FIRMA-START", Order = 1 },
            new() { Id = "B", Order = 2, ServiceMinutes = 15 }, new() { Id = "C", Order = 3 }],
        TravelTimeProfileCache = new()
        {
            ["start|B"] = new() { OptimisticMinutes = 0, RealisticMinutes = 0, PessimisticMinutes = 59 },
            ["B|C"] = new() { OptimisticMinutes = 13, RealisticMinutes = 20, PessimisticMinutes = 20 }
        }
    };
    private static TourScheduleService Service() => new(0, 0, 0, 0);

    [Fact]
    public void ReadingLegacySnapshotPreservesPessimisticEndWithoutRecalculation()
    {
        var tour = new TourRecord { Date = "28.10.2026", StartTime = "08:00", Stops =
        [new() { Id = "B", Order = 1, PlannedArrival = "09:00", PlannedDeparture = "09:15",
            PlannedArrivalOptimistic = "08:45", PlannedArrivalPessimistic = "10:45" }] };
        var before = JsonSerializer.Serialize(tour);
        var result = TourScheduleService.ReadSchedule(tour);
        Assert.Equal(new DateTime(2026, 10, 28, 11, 0, 0), result.PessimisticEnd);
        Assert.Equal(before, JsonSerializer.Serialize(tour));
    }

    [Fact]
    public void MissingFixedWindowRemainsAConflictOnRepeatedCalculation()
    {
        var tour = CreateTour();
        tour.Stops[1].IsArrivalWindowFixed = true;
        for (var i = 0; i < 2; i++)
        {
            Service().ApplySchedule(tour);
            Assert.True(tour.Stops[1].ScheduleConflict);
            Assert.Contains("fehlt", tour.Stops[1].ScheduleConflictText);
            Assert.Null(tour.Stops[1].ArrivalWindowStart);
            Assert.Null(tour.Stops[1].ArrivalWindowEnd);
        }
    }

    [Fact]
    public void LegacyFixedWindowAcrossMidnightDoesNotMoveToFollowingEvening()
    {
        var tour = CreateTour(); tour.StartTime = "23:30";
        tour.TravelTimeProfileCache["start|B"] = new() { OptimisticMinutes = 30, RealisticMinutes = 40, PessimisticMinutes = 45 };
        var stop = tour.Stops[1]; stop.IsArrivalWindowFixed = true;
        stop.PlannedArrivalOptimistic = "23:45"; stop.PlannedArrivalPessimistic = "00:45";
        for (var i = 0; i < 2; i++)
        {
            Service().ApplySchedule(tour);
            Assert.Equal(new DateTime(2026, 10, 29, 0, 10, 0), stop.CalculatedArrival);
            Assert.Equal(new DateTime(2026, 10, 28, 23, 45, 0), stop.ArrivalWindowStart);
            Assert.Equal(new DateTime(2026, 10, 29, 0, 45, 0), stop.ArrivalWindowEnd);
            Assert.False(stop.ScheduleConflict);
        }
    }

    [Fact]
    public void LaterStopRetainsDelayAndCannotHaveEarlierWindowEnd()
    {
        var tour = Service().ApplySchedule(CreateTour());
        var b = tour.Stops[1]; var c = tour.Stops[2];
        Assert.Equal("07:15", b.PlannedArrivalOptimistic);
        Assert.Equal("09:15", b.PlannedArrivalPessimistic);
        Assert.Equal("07:45", c.PlannedArrivalOptimistic);
        Assert.Equal("09:45", c.PlannedArrivalPessimistic);
        Assert.Equal(b.CalculatedArrivalPessimistic!.Value.AddMinutes(35), c.CalculatedArrivalPessimistic);
        Assert.False(c.ScheduleConflict);
    }

    [Fact]
    public void MoreThanTwoHoursIsFlaggedWithoutDiscardingRawLatestArrival()
    {
        var tour = CreateTour();
        tour.TravelTimeProfileCache["start|B"].PessimisticMinutes = 180;
        Service().ApplySchedule(tour);
        Assert.True(tour.Stops[1].ScheduleConflict);
        Assert.True(tour.Stops[1].CalculatedArrivalPessimistic > tour.Stops[1].ArrivalWindowEnd);
    }

    [Fact]
    public void FixedPromiseIsPreservedAndConflictIsVisible()
    {
        var tour = CreateTour();
        var c = tour.Stops[2];
        c.IsArrivalWindowFixed = true;
        c.PlannedArrivalOptimistic = "07:45"; c.PlannedArrivalPessimistic = "08:45";
        Service().ApplySchedule(tour);
        Assert.Equal("07:45", c.PlannedArrivalOptimistic);
        Assert.Equal("08:45", c.PlannedArrivalPessimistic);
        Assert.True(c.ScheduleConflict);
        Assert.Contains("Stoppreihenfolge", c.ScheduleConflictText);
    }

    [Fact]
    public void SnapshotSurvivesSerializationAndDifferentWorkplaceSettings()
    {
        var tour = CreateTour();
        tour.TravelTimeProfileCache["start|B"] = new() { OptimisticMinutes = 80, RealisticMinutes = 90, PessimisticMinutes = 100 };
        Service().ApplySchedule(tour);
        var copy = JsonSerializer.Deserialize<TourRecord>(JsonSerializer.Serialize(tour))!;
        Assert.Equal(tour.Stops[2].CalculatedArrivalPessimistic, copy.Stops[2].CalculatedArrivalPessimistic);
        new TourScheduleService(100, 100, 100, 100).ApplySchedule(copy);
        Assert.Equal(tour.Stops[2].CalculatedArrival, copy.Stops[2].CalculatedArrival);
        Assert.Equal(tour.Stops[2].ArrivalWindowEnd, copy.Stops[2].ArrivalWindowEnd);
        Assert.Equal(TourScheduleService.CurrentVersion, copy.ScheduleVersion);
    }

    [Fact]
    public void WaitingForAppointmentCanAbsorbUncertainty()
    {
        var tour = CreateTour();
        tour.Stops[2].TimeWindowStart = "10:00";
        Service().ApplySchedule(tour);
        Assert.Equal(tour.Stops[2].CalculatedArrivalOptimistic, tour.Stops[2].CalculatedArrivalPessimistic);
        Assert.Equal("10:00", tour.Stops[2].PlannedArrival);
    }

    [Fact]
    public void MidnightKeepsDateAndWindowOrder()
    {
        var tour = CreateTour(); tour.StartTime = "23:40";
        Service().ApplySchedule(tour);
        Assert.Equal(new DateTime(2026, 10, 29, 0, 15, 0), tour.Stops[2].CalculatedArrival);
        Assert.True(tour.Stops[2].ArrivalWindowEnd > tour.Stops[1].ArrivalWindowEnd);
    }
}
