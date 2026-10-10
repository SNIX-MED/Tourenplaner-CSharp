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

        var orderAddress = order.OrderAddress ?? new OrderAddressInfo();
        var deliveryAddress = order.DeliveryAddress ?? new DeliveryAddressInfo();
        var orderAddressLine = BuildAddressLine(
            orderAddress.Street, orderAddress.HouseNumber, orderAddress.PostalCode, orderAddress.City);
        var deliveryAddressLine = BuildAddressLine(
            deliveryAddress.Street, deliveryAddress.HouseNumber, deliveryAddress.PostalCode, deliveryAddress.City);

        var searchableColumnValues = new[]
        {
            order.Id,
            order.ScheduledDate.ToString("dd.MM.yyyy"),
            order.ScheduledDate.ToString("yyyy-MM-dd"),
            order.CustomerName,
            orderAddress.Name,
            orderAddressLine,
            order.Address,
            deliveryAddressLine,
            deliveryAddress.Name,
            deliveryAddress.ContactPerson,
            order.Phone,
            order.Notes,
            DeliveryMethodExtensions.GetPlanningDeliveryDisplayLabel(order),
            Order.NormalizeOrderStatus(order.OrderStatus),
            order.AssignedTourId ?? string.Empty
        };

        return orderNumberQueries.Any(orderNumber =>
                   order.Id.Contains(orderNumber, StringComparison.OrdinalIgnoreCase)) ||
               searchableColumnValues.Any(value =>
                   (value ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildAddressLine(string? street, string? houseNumber, string? postalCode, string? city)
    {
        var streetLine = string.Join(" ", new[] { street, houseNumber }
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var postalCityLine = string.Join(" ", new[] { postalCode, city }
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.Join(", ", new[] { streetLine, postalCityLine }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
