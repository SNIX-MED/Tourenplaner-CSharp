using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public static class OrderTourAssignmentIntegrityService
{
    public static IReadOnlyList<Order> ClearAssignmentsToMissingTours(
        IEnumerable<Order> orders,
        IEnumerable<TourRecord> tours)
    {
        var existingTourIds = (tours ?? [])
            .Where(x => x is not null && x.Id > 0)
            .Select(x => x.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changedOrders = new List<Order>();

        foreach (var order in orders ?? [])
        {
            if (order is null)
            {
                continue;
            }

            var assignedTourId = (order.AssignedTourId ?? string.Empty).Trim();
            if (assignedTourId.Length == 0 || existingTourIds.Contains(assignedTourId))
            {
                continue;
            }

            order.AssignedTourId = string.Empty;
            changedOrders.Add(order);
        }

        return changedOrders;
    }
}
