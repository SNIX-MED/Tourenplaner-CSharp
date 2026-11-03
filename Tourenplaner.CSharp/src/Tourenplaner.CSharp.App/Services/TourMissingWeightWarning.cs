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
        return "Bei folgenden Aufträgen fehlen Produktgewichte (0 kg):\n\n"
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
