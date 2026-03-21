using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class OrderPartitionService
{
    public IReadOnlyList<Order> MergeMapOrders(IEnumerable<Order> existingAll, IEnumerable<Order> updatedMapOrders)
    {
        var nonMap = (existingAll ?? Array.Empty<Order>())
            .Where(o => o.Type == OrderType.NonMap)
            .ToList();

        var map = (updatedMapOrders ?? Array.Empty<Order>())
            .Select(NormalizeMapOrder)
            .ToList();

        return nonMap.Concat(map).ToList();
    }

    public IReadOnlyList<Order> MergeNonMapOrders(IEnumerable<Order> existingAll, IEnumerable<Order> updatedNonMapOrders)
    {
        var map = (existingAll ?? Array.Empty<Order>())
            .Where(o => o.Type == OrderType.Map)
            .ToList();

        var nonMap = (updatedNonMapOrders ?? Array.Empty<Order>())
            .Select(NormalizeNonMapOrder)
            .ToList();

        return map.Concat(nonMap).ToList();
    }

    private static Order NormalizeMapOrder(Order order)
    {
        order.Type = OrderType.Map;
        return order;
    }

    private static Order NormalizeNonMapOrder(Order order)
    {
        order.Type = OrderType.NonMap;
        order.Location = null;
        return order;
    }
}
