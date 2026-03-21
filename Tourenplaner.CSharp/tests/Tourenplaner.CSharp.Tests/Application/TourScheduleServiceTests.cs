using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourScheduleServiceTests
{
    [Fact]
    public void BuildSchedule_UsesTravelCacheAndFlagsLateWindowConflicts()
    {
        var service = new TourScheduleService();
        var tour = new TourRecord
        {
            Id = 1,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 10 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 10, TimeWindowEnd = "08:20" }
            ],
            TravelTimeCache = new Dictionary<string, int>
            {
                ["A|B"] = 15
            }
        };

        var result = service.BuildSchedule(tour);

        Assert.Equal("08:00", result.Stops[0].Arrival.ToString("HH:mm"));
        Assert.Equal("08:10", result.Stops[0].Departure.ToString("HH:mm"));
        Assert.Equal("08:25", result.Stops[1].Arrival.ToString("HH:mm"));
        Assert.True(result.Stops[1].HasConflict);
    }

    [Fact]
    public void BuildSchedule_ShiftsArrivalToWindowStart()
    {
        var service = new TourScheduleService();
        var tour = new TourRecord
        {
            Id = 2,
            Date = "2026-03-21",
            StartTime = "07:30",
            Stops =
            [
                new TourStopRecord
                {
                    Id = "S1",
                    Order = 1,
                    ServiceMinutes = 20,
                    TimeWindowStart = "08:00",
                    TimeWindowEnd = "09:00"
                }
            ]
        };

        var result = service.BuildSchedule(tour, fallbackTravelMinutes: 5);

        Assert.Equal("08:00", result.Stops[0].Arrival.ToString("HH:mm"));
        Assert.Equal("08:20", result.Stops[0].Departure.ToString("HH:mm"));
        Assert.False(result.Stops[0].HasConflict);
    }

    [Fact]
    public void ApplySchedule_WritesPlannedTimesBackToTour()
    {
        var service = new TourScheduleService();
        var tour = new TourRecord
        {
            Id = 3,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "S1", Order = 1, ServiceMinutes = 5 }
            ]
        };

        var updated = service.ApplySchedule(tour);

        Assert.Equal("08:00", updated.Stops[0].PlannedArrival);
        Assert.Equal("08:05", updated.Stops[0].PlannedDeparture);
    }
}
