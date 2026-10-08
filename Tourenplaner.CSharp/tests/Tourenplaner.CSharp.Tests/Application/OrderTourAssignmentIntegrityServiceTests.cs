using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderTourAssignmentIntegrityServiceTests
{
    [Fact]
    public void ClearAssignmentsToMissingTours_only_clears_dangling_references()
    {
        var existing = new Order { Id = "A", AssignedTourId = "12" };
        var missing = new Order { Id = "B", AssignedTourId = "14" };
        var open = new Order { Id = "C", AssignedTourId = string.Empty };

        var changed = OrderTourAssignmentIntegrityService.ClearAssignmentsToMissingTours(
            [existing, missing, open],
            [new TourRecord { Id = 12 }]);

        Assert.Single(changed);
        Assert.Same(missing, changed[0]);
        Assert.Equal("12", existing.AssignedTourId);
        Assert.Equal(string.Empty, missing.AssignedTourId);
        Assert.Equal(string.Empty, open.AssignedTourId);
    }

    [Fact]
    public void ClearAssignmentsToMissingTours_keeps_assignments_to_archived_existing_tours()
    {
        var order = new Order { Id = "A", AssignedTourId = "7" };

        var changed = OrderTourAssignmentIntegrityService.ClearAssignmentsToMissingTours(
            [order],
            [new TourRecord { Id = 7, IsArchived = true }]);

        Assert.Empty(changed);
        Assert.Equal("7", order.AssignedTourId);
    }
}
