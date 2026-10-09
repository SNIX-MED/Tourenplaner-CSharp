using System.Collections.ObjectModel;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

internal static class OrderSectionSharedHelpers
{
    public static HashSet<string> GetSelectedFilterLabels(ObservableCollection<MapOrderFilterOption> options)
    {
        return options
            .Where(x => x.IsSelected)
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void SetAllFilterOptions(ObservableCollection<MapOrderFilterOption> options, bool isSelected)
    {
        foreach (var option in options)
        {
            option.IsSelected = isSelected;
        }
    }

    public static bool SyncDerivedOrderStatuses(IEnumerable<Order> orders)
    {
        var changed = false;
        foreach (var order in orders)
        {
            if (order is null)
            {
                continue;
            }

            var derivedStatus = Order.ResolveOrderStatusFromProducts(order.Products);
            if (string.Equals(Order.NormalizeOrderStatus(order.OrderStatus), derivedStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            order.OrderStatus = derivedStatus;
            changed = true;
        }

        return changed;
    }

    public static bool OrderContainsAnySupplier(Order order, IReadOnlySet<string> suppliers, Func<string?, string> normalizeSupplier)
    {
        if (suppliers.Count == 0)
        {
            return true;
        }

        return (order.Products ?? [])
            .Select(p => normalizeSupplier(p.Supplier))
            .Any(suppliers.Contains);
    }

    public static bool MatchesSearchQuery(Order order, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalizedQuery = query.Trim();
        var orderNumberQueries = normalizedQuery.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return orderNumberQueries.Any(orderNumber =>
                   order.Id.Contains(orderNumber, StringComparison.OrdinalIgnoreCase)) ||
               order.CustomerName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
               order.Address.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
               (order.AssignedTourId ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }
}
