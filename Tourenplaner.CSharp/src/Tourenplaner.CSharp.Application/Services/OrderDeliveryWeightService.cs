using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public static class OrderDeliveryWeightService
{
    public static IReadOnlyList<OrderProductInfo> ResolveStopProducts(Order order, string? deliveryId, string? tourId = null)
    {
        if (string.IsNullOrWhiteSpace(deliveryId) && order.Deliveries.Count <= 1) return order.Products;
        var quantities = order.Deliveries.Where(d => !string.IsNullOrWhiteSpace(deliveryId)
                ? string.Equals(d.Id, deliveryId, StringComparison.OrdinalIgnoreCase)
                : d.Status != OrderDeliveryStatus.Cancelled && !string.IsNullOrWhiteSpace(tourId) && d.AssignedTourId == tourId)
            .SelectMany(d => d.Lines).GroupBy(l => l.OrderProductPositionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(l => Math.Max(0, l.Quantity)), StringComparer.OrdinalIgnoreCase);
        return order.Products.Where(p => quantities.GetValueOrDefault(p.PositionId) > 0).Select(p => new OrderProductInfo
        {
            PositionId = p.PositionId, ExternalPositionId = p.ExternalPositionId, Name = p.Name, Supplier = p.Supplier,
            Quantity = quantities[p.PositionId], UnitWeightKg = Common.OrderProductWeightNormalization.ResolveUnitWeightKg(p),
            WeightKg = Math.Max(0, Common.OrderProductWeightNormalization.ResolveUnitWeightKg(p)) * quantities[p.PositionId],
            ExpectedDelivery = p.ExpectedDelivery, DeliveryStatus = p.DeliveryStatus, Dimensions = p.Dimensions
        }).ToList();
    }
    public static double ResolveStopWeightKg(Order order, string? deliveryId, string? tourId = null)
    {
        var deliveries = order.Deliveries ?? [];
        if (!string.IsNullOrWhiteSpace(deliveryId))
        {
            var delivery = deliveries.FirstOrDefault(d => string.Equals(d.Id, deliveryId, StringComparison.OrdinalIgnoreCase));
            return delivery is null ? 0 : new OrderDeliveryService().ResolveWeightKg(order, delivery);
        }
        if (deliveries.Count <= 1) return order.ResolveTotalWeightKg();
        // Older saved stops can lack DeliveryId. Resolve only parts assigned to this tour.
        return deliveries.Where(d => d.Status != OrderDeliveryStatus.Cancelled &&
                !string.IsNullOrWhiteSpace(tourId) && string.Equals(d.AssignedTourId, tourId, StringComparison.OrdinalIgnoreCase))
            .Sum(d => new OrderDeliveryService().ResolveWeightKg(order, d));
    }

    public static int ResolveTourOrderWeightKg(TourRecord tour, IEnumerable<Order> orders)
    {
        var byId = orders.GroupBy(o => o.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var stops = tour.Stops.Where(s => !string.IsNullOrWhiteSpace(s.Auftragsnummer) &&
            !Common.TourStopIdentity.IsCompanyStop(s) &&
            !string.Equals(s.StopKind, "pause", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(s.StopKind, "manual", StringComparison.OrdinalIgnoreCase)).ToList();
        if (stops.Count == 0)
            return byId.Values.Where(o => !o.IsArchived && (o.AssignedTourId == tour.Id.ToString() ||
                    o.Deliveries.Any(d => d.AssignedTourId == tour.Id.ToString())))
                .Sum(o => (int)Math.Ceiling(ResolveStopWeightKg(o, null, tour.Id.ToString())));
        return stops.GroupBy(s => s.Auftragsnummer.Trim(), StringComparer.OrdinalIgnoreCase).Sum(group =>
        {
            if (!byId.TryGetValue(group.Key, out var order)) return 0;
            var ids = group.Select(s => s.DeliveryId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (group.Any(s => string.IsNullOrWhiteSpace(s.DeliveryId)))
            {
                if (order.Deliveries.Count <= 1) return (int)Math.Ceiling(order.ResolveTotalWeightKg());
                ids.UnionWith(order.Deliveries.Where(d => d.Status != OrderDeliveryStatus.Cancelled &&
                    d.AssignedTourId == tour.Id.ToString()).Select(d => d.Id));
            }
            return (int)Math.Ceiling(ids.Sum(id => ResolveStopWeightKg(order, id, tour.Id.ToString())));
        });
    }
}
