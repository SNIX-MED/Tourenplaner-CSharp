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

    public IReadOnlyList<TourArchiveTarget> GetTargets(TourRecord tour, IEnumerable<Order> orders)
    {
        var targets = ResolveTargets(tour, orders);
        return targets.WholeOrders
            .Select(order => new TourArchiveTarget(
                CreateSelectionKey(order.Id, null), order.Id, null, null))
            .Concat(targets.PartialDeliveries.Select(target => new TourArchiveTarget(
                CreateSelectionKey(target.Order.Id, target.Delivery.Id),
                target.Order.Id,
                target.Delivery.Id,
                target.Delivery.SequenceNumber)))
            .ToList();
    }

    public TourArchiveResult Apply(
        TourRecord tour,
        IEnumerable<Order> orders,
        bool markStopsDelivered,
        DateTimeOffset completedAt)
    {
        var targets = ResolveTargets(tour, orders);
        return Apply(targets, _ => markStopsDelivered, tour.Id, completedAt);
    }

    public TourArchiveResult Apply(
        TourRecord tour,
        IEnumerable<Order> orders,
        IReadOnlySet<string> deliveredSelectionKeys,
        DateTimeOffset completedAt)
    {
        var targets = ResolveTargets(tour, orders);
        return Apply(targets, deliveredSelectionKeys.Contains, tour.Id, completedAt);
    }

    private TourArchiveResult Apply(
        ArchiveTargets targets,
        Func<string, bool> shouldMarkDelivered,
        int tourId,
        DateTimeOffset completedAt)
    {
        var archivedOrders = 0;
        var archivedPartialDeliveries = 0;
        var releasedOrders = 0;
        var releasedPartialDeliveries = 0;

        foreach (var order in targets.WholeOrders)
        {
            if (shouldMarkDelivered(CreateSelectionKey(order.Id, null)))
            {
                order.IsArchived = true;
                archivedOrders++;
            }
            else
            {
                ReleaseWholeOrder(order, tourId);
                releasedOrders++;
            }
        }

        foreach (var target in targets.PartialDeliveries)
        {
            if (shouldMarkDelivered(CreateSelectionKey(target.Order.Id, target.Delivery.Id)))
            {
                _deliveryService.MarkDelivered(target.Order, target.Delivery.Id, completedAt);
                archivedPartialDeliveries++;
            }
            else
            {
                target.Delivery.AssignedTourId = null;
                if (target.Delivery.Status is not OrderDeliveryStatus.Delivered and not OrderDeliveryStatus.Cancelled)
                {
                    target.Delivery.Status = OrderDeliveryStatus.ReadyForPlanning;
                }
                RefreshAggregateAssignment(target.Order);
                releasedPartialDeliveries++;
            }
        }

        foreach (var order in targets.PartialDeliveries.Select(x => x.Order).Distinct())
        {
            RefreshAggregateAssignment(order);
        }

        return new TourArchiveResult(
            archivedOrders,
            archivedPartialDeliveries,
            releasedOrders,
            releasedPartialDeliveries);
    }

    private static string CreateSelectionKey(string orderId, string? deliveryId) =>
        $"{(orderId ?? string.Empty).Trim().ToUpperInvariant()}\u001f{(deliveryId ?? string.Empty).Trim().ToUpperInvariant()}";

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

public sealed record TourArchiveTarget(
    string SelectionKey,
    string OrderId,
    string? DeliveryId,
    int? DeliverySequenceNumber);

public sealed record TourArchiveResult(
    int ArchivedOrderCount,
    int ArchivedPartialDeliveryCount,
    int ReleasedOrderCount,
    int ReleasedPartialDeliveryCount);
