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
}
