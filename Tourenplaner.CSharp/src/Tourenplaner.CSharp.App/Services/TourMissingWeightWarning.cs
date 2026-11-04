using System.Windows;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class TourMissingWeightWarning
{
    public static string BuildMessage(TourRecord tour, IEnumerable<Order> orders)
    {
        var byId = orders.GroupBy(o => o.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var affected = new Dictionary<string, (Order Order, HashSet<string> Products)>(StringComparer.OrdinalIgnoreCase);
        foreach (var stop in tour.Stops.Where(s => !TourStopIdentity.IsCompanyStop(s) && !TourStopIdentity.IsPauseStop(s)
                     && !string.Equals(s.StopKind, "manual", StringComparison.OrdinalIgnoreCase)))
        {
            if (!byId.TryGetValue((stop.Auftragsnummer ?? string.Empty).Trim(), out var order)) continue;
            // Match the weight calculation: a whole-order override does not cover partial deliveries.
            var usesWholeOrderWeight = order.Deliveries.Count <= 1 &&
                (string.IsNullOrWhiteSpace(stop.DeliveryId) || order.Deliveries.Any(d =>
                    string.Equals(d.Id, stop.DeliveryId, StringComparison.OrdinalIgnoreCase) &&
                    order.Products.All(p => (d.Lines ?? []).Where(l => string.Equals(l.OrderProductPositionId,
                        p.PositionId, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Quantity) ==
                        OrderProductInfo.NormalizeQuantity(p.Quantity))));
            if (usesWholeOrderWeight && order.ManualTotalWeightKg.HasValue)
            {
                if (double.IsFinite(order.ManualTotalWeightKg.Value) && order.ManualTotalWeightKg.Value > 0)
                    continue;
                if (!affected.TryGetValue(order.Id, out var manualEntry))
                    manualEntry = (order, new HashSet<string>(StringComparer.Ordinal));
                manualEntry.Products.Add("  • Manuelles Gesamtgewicht ist nicht grösser als 0 kg oder ungültig.");
                affected[order.Id] = manualEntry;
            }
            var products = OrderDeliveryWeightService.ResolveStopProducts(order, stop.DeliveryId, tour.Id.ToString());
            foreach (var product in products.Where(p => p.UnitWeightKg == 0 && p.WeightKg == 0))
            {
                if (!affected.TryGetValue(order.Id, out var entry))
                    entry = (order, new HashSet<string>(StringComparer.Ordinal));
                var position = order.Products.IndexOf(product);
                if (position < 0)
                    position = order.Products.FindIndex(p => string.Equals(p.PositionId, product.PositionId, StringComparison.OrdinalIgnoreCase));
                entry.Products.Add($"  • {product.Name} (Position {position + 1})");
                affected[order.Id] = entry;
            }
        }
        if (affected.Count == 0) return string.Empty;
        return "Für folgende Aufträge oder Lieferungen fehlen verwertbare Gewichtsangaben (0 kg):\n\n"
            + string.Join("\n\n", affected.Values.Select(e => $"{e.Order.Id} – {e.Order.CustomerName}\n" + string.Join("\n", e.Products)))
            + "\n\nDas berechnete Tourgewicht kann dadurch zu niedrig sein.\nMöchtest du die Tour trotzdem speichern?";
    }

    public static bool Confirm(TourRecord tour, IEnumerable<Order> orders)
    {
        var message = BuildMessage(tour, orders);
        return message.Length == 0 || AppMessageBox.Show(message, "Fehlende Produktgewichte",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
