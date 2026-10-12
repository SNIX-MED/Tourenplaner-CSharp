using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

/// <summary>
/// Resolves and updates the orders or individual partial deliveries represented by a tour's stops.
/// The stop references are authoritative; aggregate AssignedTourId values are only a cache and may
/// intentionally be empty when the two deliveries of an order are assigned independently.
/// </summary>
public sealed class TourArchiveOrderService
{
    private readonly OrderDeliveryService _deliveryService = new();

    public TourArchiveImpact GetImpact(TourRecord tour, IEnumerable<Order> orders)
    {
        var targets = ResolveTargets(tour, orders);
        return new TourArchiveImpact(targets.WholeOrders.Count, targets.PartialDeliveries.Count);
    }

    public TourArchiveResult Apply(
        TourRecord tour,
        IEnumerable<Order> orders,
        bool markStopsDelivered,
        DateTimeOffset completedAt)
    {
        var targets = ResolveTargets(tour, orders);
        if (markStopsDelivered)
        {
            foreach (var order in targets.WholeOrders)
            {
                order.IsArchived = true;
            }

            foreach (var target in targets.PartialDeliveries)
            {
                _deliveryService.MarkDelivered(target.Order, target.Delivery.Id, completedAt);
            }

            return new TourArchiveResult(
                targets.WholeOrders.Count,
                targets.PartialDeliveries.Count,
                0,
                0);
        }

        foreach (var order in targets.WholeOrders)
        {
            ReleaseWholeOrder(order, tour.Id);
        }

        foreach (var target in targets.PartialDeliveries)
        {
            target.Delivery.AssignedTourId = null;
            if (target.Delivery.Status is not OrderDeliveryStatus.Delivered and not OrderDeliveryStatus.Cancelled)
            {
                target.Delivery.Status = OrderDeliveryStatus.ReadyForPlanning;
            }
            RefreshAggregateAssignment(target.Order);
        }

        return new TourArchiveResult(
            0,
            0,
            targets.WholeOrders.Count,
            targets.PartialDeliveries.Count);
    }

    private ArchiveTargets ResolveTargets(TourRecord tour, IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(tour);
        var ordersById = (orders ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var wholeOrders = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var partialDeliveries = new Dictionary<string, PartialDeliveryTarget>(StringComparer.OrdinalIgnoreCase);

        foreach (var stop in tour.Stops ?? [])
        {
            var orderId = (stop.Auftragsnummer ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(orderId) || !ordersById.TryGetValue(orderId, out var order))
            {
                continue;
            }

            _deliveryService.Normalize(order);
            var deliveries = order.Deliveries ?? [];
            var isPartialOrder = deliveries.Count >= 2;
            var deliveryId = (stop.DeliveryId ?? string.Empty).Trim();
            var delivery = isPartialOrder && !string.IsNullOrWhiteSpace(deliveryId)
                ? deliveries.FirstOrDefault(x => string.Equals(x.Id, deliveryId, StringComparison.OrdinalIgnoreCase))
                : null;

            if (delivery is not null)
            {
                if (delivery.Status is not OrderDeliveryStatus.Delivered and not OrderDeliveryStatus.Cancelled)
                {
                    partialDeliveries.TryAdd($"{orderId}\u001f{delivery.Id}", new PartialDeliveryTarget(order, delivery));
                }
                continue;
            }

            if (!order.IsArchived && !isPartialOrder)
            {
                wholeOrders.TryAdd(orderId, order);
            }
        }

        return new ArchiveTargets(wholeOrders.Values.ToList(), partialDeliveries.Values.ToList());
    }

    private static void ReleaseWholeOrder(Order order, int tourId)
    {
        var tourKey = tourId.ToString(CultureInfo.InvariantCulture);
        if (string.Equals((order.AssignedTourId ?? string.Empty).Trim(), tourKey, StringComparison.OrdinalIgnoreCase))
        {
            order.AssignedTourId = string.Empty;
        }

        foreach (var delivery in order.Deliveries ?? [])
        {
            if (!string.Equals((delivery.AssignedTourId ?? string.Empty).Trim(), tourKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            delivery.AssignedTourId = null;
            if (delivery.Status is not OrderDeliveryStatus.Delivered and not OrderDeliveryStatus.Cancelled)
            {
                delivery.Status = OrderDeliveryStatus.ReadyForPlanning;
            }
        }
    }

    private static void RefreshAggregateAssignment(Order order)
    {
        var activeTourIds = (order.Deliveries ?? [])
            .Where(x => x.Status is not OrderDeliveryStatus.Delivered and not OrderDeliveryStatus.Cancelled)
            .Select(x => (x.AssignedTourId ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        order.AssignedTourId = activeTourIds.Count == 1 ? activeTourIds[0] : string.Empty;
    }

    private sealed record PartialDeliveryTarget(Order Order, OrderDelivery Delivery);
    private sealed record ArchiveTargets(List<Order> WholeOrders, List<PartialDeliveryTarget> PartialDeliveries);
}

public sealed record TourArchiveImpact(int WholeOrderCount, int PartialDeliveryCount)
{
    public bool HasTargets => WholeOrderCount > 0 || PartialDeliveryCount > 0;
}

public sealed record TourArchiveResult(
    int ArchivedOrderCount,
    int ArchivedPartialDeliveryCount,
    int ReleasedOrderCount,
    int ReleasedPartialDeliveryCount);
