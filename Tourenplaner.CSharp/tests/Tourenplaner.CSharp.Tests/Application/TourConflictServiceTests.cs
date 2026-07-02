using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourConflictServiceTests
{
    [Fact]
    public void FindAssignmentConflicts_DetectsEmployeeAndVehicleOverlap()
    {
        var service = new TourConflictService();
        var tours = new[]
        {
            new TourRecord
            {
                Id = 100,
                Date = "21.03.2026",
                StartTime = "08:00",
                VehicleId = "V-1",
                EmployeeIds = ["E-1"],
                Stops = [new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 60 }]
            },
            new TourRecord
            {
                Id = 101,
                Date = "21.03.2026",
                StartTime = "08:30",
                VehicleId = "V-1",
                EmployeeIds = ["E-1", "E-2"],
                Stops = [new TourStopRecord { Id = "B", Order = 1, ServiceMinutes = 30 }]
            }
        };

        var conflicts = service.FindAssignmentConflicts(tours);

        Assert.Contains(conflicts, c => c.ResourceType == "employee" && c.ResourceId == "E-1");
        Assert.Contains(conflicts, c => c.ResourceType == "vehicle" && c.ResourceId == "V-1");
    }

    [Fact]
    public void FindAssignmentConflicts_IgnoresSeparatedTours()
    {
        var service = new TourConflictService();
        var tours = new[]
        {
            new TourRecord
            {
                Id = 200,
                Date = "21.03.2026",
                StartTime = "08:00",
                EmployeeIds = ["E-1"],
                Stops = [new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 15 }]
            },
            new TourRecord
            {
                Id = 201,
                Date = "21.03.2026",
                StartTime = "10:00",
                EmployeeIds = ["E-1"],
                Stops = [new TourStopRecord { Id = "B", Order = 1, ServiceMinutes = 15 }]
            }
        };

        var conflicts = service.FindAssignmentConflicts(tours);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void FindAssignmentConflicts_DetectsOverlap_WithSecondaryAssignments()
    {
        var service = new TourConflictService();
        var tours = new[]
        {
            new TourRecord
            {
                Id = 300,
                Date = "21.03.2026",
                StartTime = "08:00",
                SecondaryVehicleId = "V-2",
                Stops = [new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 60 }]
            },
            new TourRecord
            {
                Id = 301,
                Date = "21.03.2026",
                StartTime = "08:30",
                VehicleId = "V-2",
                Stops = [new TourStopRecord { Id = "B", Order = 1, ServiceMinutes = 30 }]
            }
        };

        var conflicts = service.FindAssignmentConflicts(tours);

        Assert.Contains(conflicts, c => c.ResourceType == "vehicle" && c.ResourceId == "V-2");
    }

    [Fact]
    public void FindAssignmentConflicts_UsesPessimisticTourEndForOverlapDetection()
    {
        var service = new TourConflictService();
        var tours = new[]
        {
            new TourRecord
            {
                Id = 400,
                Date = "21.03.2026",
                StartTime = "08:00",
                VehicleId = "V-9",
                Stops =
                [
                    new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 10 },
                    new TourStopRecord { Id = "B", Order = 2, ServiceMinutes = 10 }
                ],
                TravelTimeProfileCache = new Dictionary<string, TourTravelTimeProfile>
                {
                    ["A|B"] = new()
                    {
                        OptimisticMinutes = 20,
                        RealisticMinutes = 30,
                        PessimisticMinutes = 80
                    }
                }
            },
            new TourRecord
            {
                Id = 401,
                Date = "21.03.2026",
                StartTime = "09:05",
                VehicleId = "V-9",
                Stops = [new TourStopRecord { Id = "C", Order = 1, ServiceMinutes = 15 }]
            }
        };

        var conflicts = service.FindAssignmentConflicts(tours);

        Assert.Contains(conflicts, c => c.ResourceType == "vehicle" && c.ResourceId == "V-9");
        Assert.Contains(conflicts, c => c.Message.Contains("pessimistisch bis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindSameDayAssignmentConflicts_DetectsEmployeeConflictEvenWithoutTimeOverlap()
    {
        var service = new TourConflictService();
        var tours = new[]
        {
            new TourRecord
            {
                Id = 500,
                Date = "21.03.2026",
                StartTime = "08:00",
                EmployeeIds = ["E-5"],
                Stops = [new TourStopRecord { Id = "A", Order = 1, ServiceMinutes = 15 }]
            },
            new TourRecord
            {
                Id = 501,
                Date = "21.03.2026",
                StartTime = "14:00",
                EmployeeIds = ["E-5"],
                Stops = [new TourStopRecord { Id = "B", Order = 1, ServiceMinutes = 15 }]
            }
        };

        var conflicts = service.FindSameDayAssignmentConflicts(tours);

        Assert.Contains(conflicts, c => c.ResourceType == "Mitarbeiter" && c.ResourceId == "E-5");
    }
}
