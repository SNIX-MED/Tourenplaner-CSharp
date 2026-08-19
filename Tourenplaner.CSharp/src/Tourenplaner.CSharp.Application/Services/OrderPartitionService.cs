using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class OrderPartitionService
{
    public IReadOnlyList<Order> MergeMapOrders(IEnumerable<Order> existingAll, IEnumerable<Order> updatedMapOrders)
    {
        var updated = (updatedMapOrders ?? Array.Empty<Order>())
            .Select(NormalizeByDeliveryMethod)
            .ToList();
        var updatedIds = BuildOrderIdSet(updated);

        var nonMap = (existingAll ?? Array.Empty<Order>())
            .Where(o => o.Type == OrderType.NonMap)
            .Where(o => !updatedIds.Contains(o.Id ?? string.Empty))
            .ToList();

        return nonMap.Concat(updated).ToList();
    }

    public IReadOnlyList<Order> MergeNonMapOrders(IEnumerable<Order> existingAll, IEnumerable<Order> updatedNonMapOrders)
    {
        var updated = (updatedNonMapOrders ?? Array.Empty<Order>())
            .Select(NormalizeByDeliveryMethod)
            .ToList();
        var updatedIds = BuildOrderIdSet(updated);

        var map = (existingAll ?? Array.Empty<Order>())
            .Where(o => o.Type == OrderType.Map)
            .Where(o => !updatedIds.Contains(o.Id ?? string.Empty))
            .ToList();

        return map.Concat(updated).ToList();
    }

    private static HashSet<string> BuildOrderIdSet(IEnumerable<Order> orders)
    {
        return orders
            .Select(o => o.Id ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Order NormalizeByDeliveryMethod(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.DeliveryType))
        {
            order.Type = DeliveryMethodExtensions.ResolveOrderType(order.DeliveryType);
        }

        if (order.Type == OrderType.NonMap)
        {
            order.Location = null;
            order.AssignedTourId = string.Empty;
        }

        return order;
    }
}
