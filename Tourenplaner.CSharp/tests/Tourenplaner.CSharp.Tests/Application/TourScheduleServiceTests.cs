using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourScheduleServiceTests
{
    [Fact]
    public void BuildSchedule_UsesTravelCacheAndFlagsLateWindowConflicts()
    {
        var service = new TourScheduleService(0, 0, 0, 0);
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
        var service = new TourScheduleService(0, 0, 0, 0);
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
    public void BuildSchedule_ProducesArrivalRangeFromTravelProfiles()
    {
        var service = new TourScheduleService(0, 0, 0, 0);
        var tour = new TourRecord
        {
            Id = 4,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 10 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 5 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 20,
                    RealisticMinutes = 30,
                    PessimisticMinutes = 45
                }
            }
        };

        var result = service.BuildSchedule(tour);

        Assert.Equal("08:40", result.Stops[1].Arrival.ToString("HH:mm"));
        Assert.Equal("08:30", result.Stops[1].OptimisticArrival?.ToString("HH:mm"));
        Assert.Equal("08:55", result.Stops[1].PessimisticArrival?.ToString("HH:mm"));
    }

    [Fact]
    public void ApplySchedule_WritesPlannedTimesBackToTour()
    {
        var service = new TourScheduleService(0, 0, 0, 0);
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

        Assert.Equal("08:00", updated.Stops[0].PlannedArrivalOptimistic);
        Assert.Equal("08:00", updated.Stops[0].PlannedArrival);
        Assert.Equal("08:00", updated.Stops[0].PlannedArrivalPessimistic);
        Assert.Equal("08:05", updated.Stops[0].PlannedDeparture);
    }

    [Fact]
    public void ApplySchedule_RoundsArrivalRangeToQuarterHours()
    {
        var service = new TourScheduleService(0, 0, 0, 0);
        var tour = new TourRecord
        {
            Id = 5,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 10 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 5 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 28,
                    RealisticMinutes = 37,
                    PessimisticMinutes = 53
                }
            }
        };

        var updated = service.ApplySchedule(tour);

        Assert.Equal("08:30", updated.Stops[1].PlannedArrivalOptimistic);
        Assert.Equal("08:47", updated.Stops[1].PlannedArrival);
        Assert.Equal("09:15", updated.Stops[1].PlannedArrivalPessimistic);
    }

    [Fact]
    public void ApplySchedule_RoundsDisplayedRangeEndUpToQuarterHour()
    {
        var service = new TourScheduleService(0, 0, 0, 0);
        var tour = new TourRecord
        {
            Id = 6,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 0 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 0 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 10,
                    RealisticMinutes = 10,
                    PessimisticMinutes = 14
                }
            }
        };

        var updated = service.ApplySchedule(tour);
        Assert.Equal("08:00", updated.Stops[1].PlannedArrivalOptimistic);
        Assert.Equal("08:15", updated.Stops[1].PlannedArrivalPessimistic);

        tour.TravelTimeProfileCache["A|B"] = new TourTravelTimeProfile
        {
            OptimisticMinutes = 30,
            RealisticMinutes = 30,
            PessimisticMinutes = 44
        };

        updated = service.ApplySchedule(tour);
        Assert.Equal("08:30", updated.Stops[1].PlannedArrivalOptimistic);
        Assert.Equal("08:45", updated.Stops[1].PlannedArrivalPessimistic);
    }

    [Fact]
    public void ApplySchedule_ClampsDisplayedArrivalRangeToTwoHours()
    {
        var service = new TourScheduleService(0, 0, 0, 0);
        var tour = new TourRecord
        {
            Id = 11,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 0 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 0 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 45,
                    RealisticMinutes = 60,
                    PessimisticMinutes = 180
                }
            }
        };

        var updated = service.ApplySchedule(tour);

        Assert.Equal("08:45", updated.Stops[1].PlannedArrivalOptimistic);
        Assert.Equal("09:00", updated.Stops[1].PlannedArrival);
        Assert.Equal("10:45", updated.Stops[1].PlannedArrivalPessimistic);
    }

    [Fact]
    public void BuildSchedule_AddsTrafficBufferForDrivesLongerThanThirtyMinutes()
    {
        var service = new TourScheduleService(20, 20, 10, 25);
        var tour = new TourRecord
        {
            Id = 7,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 0 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 0 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 35,
                    RealisticMinutes = 40,
                    PessimisticMinutes = 50
                }
            }
        };

        var result = service.BuildSchedule(tour);

        Assert.Equal("08:48", result.Stops[1].Arrival.ToString("HH:mm"));
        Assert.Equal("08:43", result.Stops[1].OptimisticArrival?.ToString("HH:mm"));
        Assert.Equal("08:58", result.Stops[1].PessimisticArrival?.ToString("HH:mm"));
    }

    [Fact]
    public void BuildSchedule_DoesNotAddTrafficBufferForDrivesUpToThirtyMinutes()
    {
        var service = new TourScheduleService(20, 20, 10, 25);
        var tour = new TourRecord
        {
            Id = 8,
            Date = "21.03.2026",
            StartTime = "08:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 0 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 0 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 25,
                    RealisticMinutes = 30,
                    PessimisticMinutes = 35
                }
            }
        };

        var result = service.BuildSchedule(tour);

        Assert.Equal("08:30", result.Stops[1].Arrival.ToString("HH:mm"));
        Assert.Equal("08:25", result.Stops[1].OptimisticArrival?.ToString("HH:mm"));
        Assert.Equal("08:35", result.Stops[1].PessimisticArrival?.ToString("HH:mm"));
    }

    [Fact]
    public void BuildSchedule_UsesMatchingDaytimeTrafficBufferWindow()
    {
        var service = new TourScheduleService(20, 20, 10, 25);
        var tour = new TourRecord
        {
            Id = 9,
            Date = "21.03.2026",
            StartTime = "10:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 0 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 0 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 80,
                    RealisticMinutes = 90,
                    PessimisticMinutes = 105
                }
            }
        };

        var result = service.BuildSchedule(tour);

        Assert.Equal("11:39", result.Stops[1].Arrival.ToString("HH:mm"));
        Assert.Equal("11:29", result.Stops[1].OptimisticArrival?.ToString("HH:mm"));
        Assert.Equal("11:54", result.Stops[1].PessimisticArrival?.ToString("HH:mm"));
    }

    [Fact]
    public void BuildSchedule_UsesMorningTrafficBufferWindowFromSixToNine()
    {
        var service = new TourScheduleService(10, 20, 0, 20);
        var tour = new TourRecord
        {
            Id = 12,
            Date = "21.03.2026",
            StartTime = "06:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 0 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 0 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 35,
                    RealisticMinutes = 40,
                    PessimisticMinutes = 50
                }
            }
        };

        var result = service.BuildSchedule(tour);

        Assert.Equal("06:48", result.Stops[1].Arrival.ToString("HH:mm"));
        Assert.Equal("06:43", result.Stops[1].OptimisticArrival?.ToString("HH:mm"));
        Assert.Equal("06:58", result.Stops[1].PessimisticArrival?.ToString("HH:mm"));
    }

    [Fact]
    public void BuildSchedule_DoesNotAddTrafficBufferOutsideConfiguredTimeWindows()
    {
        var service = new TourScheduleService(20, 20, 10, 25);
        var tour = new TourRecord
        {
            Id = 10,
            Date = "21.03.2026",
            StartTime = "19:00",
            Stops =
            [
                new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 0 },
                new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 0 }
            ],
            TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
            {
                ["A|B"] = new()
                {
                    OptimisticMinutes = 35,
                    RealisticMinutes = 40,
                    PessimisticMinutes = 50
                }
            }
        };

        var result = service.BuildSchedule(tour);

        Assert.Equal("19:40", result.Stops[1].Arrival.ToString("HH:mm"));
        Assert.Equal("19:35", result.Stops[1].OptimisticArrival?.ToString("HH:mm"));
        Assert.Equal("19:50", result.Stops[1].PessimisticArrival?.ToString("HH:mm"));
    }
}
