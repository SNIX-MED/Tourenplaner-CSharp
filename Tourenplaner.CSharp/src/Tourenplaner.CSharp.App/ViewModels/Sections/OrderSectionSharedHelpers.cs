using System.Collections.ObjectModel;
using System.ComponentModel;
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

    public static void UpdateFilterOptions(
        ObservableCollection<MapOrderFilterOption> target,
        IEnumerable<string> values,
        PropertyChangedEventHandler propertyChangedHandler)
    {
        var previouslyAvailable = target
            .Select(option => option.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previouslySelected = target
            .Where(option => option.IsSelected)
            .Select(option => option.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepCurrentSelection = target.Count > 0;

        foreach (var option in target)
        {
            option.PropertyChanged -= propertyChangedHandler;
        }

        target.Clear();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var isNewValue = !previouslyAvailable.Contains(value);
            var isSelected = !keepCurrentSelection || previouslySelected.Contains(value) || isNewValue;
            var option = new MapOrderFilterOption(value, isSelected);
            option.PropertyChanged += propertyChangedHandler;
            target.Add(option);
        }
    }

    public static int GetSortIndex(string? value, IReadOnlyList<string> orderedValues, int unknownValueIndex)
    {
        for (var index = 0; index < orderedValues.Count; index++)
        {
            if (string.Equals(orderedValues[index], value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return unknownValueIndex;
    }

    public static bool AreAllFilterOptionsSelected(params ObservableCollection<MapOrderFilterOption>[] filterGroups)
    {
        return filterGroups.All(group => group.All(option => option.IsSelected));
    }

    public static bool IsFilterGroupActive(ObservableCollection<MapOrderFilterOption> options)
    {
        return options.Count > 0 && options.Any(option => !option.IsSelected);
    }

    public static void AddPartialFilterSummary(
        ICollection<string> parts,
        string label,
        ObservableCollection<MapOrderFilterOption> options)
    {
        if (options.Count == 0)
        {
            return;
        }

        var selectedCount = options.Count(option => option.IsSelected);
        if (selectedCount != options.Count)
        {
            parts.Add($"{label}: {selectedCount}/{options.Count}");
        }
    }

    public static string NormalizeFilterLabel(string? value, string unspecifiedLabel)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? unspecifiedLabel : normalized;
    }

    public static int ResolveTrafficBufferPercent(int explicitValue, int legacyValue, int fallbackValue)
    {
        return explicitValue switch
        {
            >= 0 and <= 100 => explicitValue,
            _ when legacyValue is >= 0 and <= 100 => legacyValue,
            _ => fallbackValue
        };
    }

    public static string BuildStreetLine(string? street, string? houseNumber)
    {
        return JoinNonEmpty(" ", street, houseNumber);
    }

    public static string BuildCompanyAddress(string? street, string? postalCode, string? city)
    {
        return JoinNonEmpty(", ", street, JoinNonEmpty(" ", postalCode, city));
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

    public static string ResolveDeliveryName(Order order)
    {
        var deliveryName = (order.DeliveryAddress?.Name ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(deliveryName)
            ? (order.CustomerName ?? string.Empty).Trim()
            : deliveryName;
    }

    public static string BuildProductsSummary(Order order)
    {
        var summary = Tourenplaner.CSharp.App.Services.OrderProductFormatter.BuildSummary(order.Products);
        if (!order.ManualTotalWeightKg.HasValue)
        {
            return summary;
        }

        var totalWeight = order.ResolveTotalWeightKg();
        var weightText = $"Gesamtgewicht: {totalWeight:0.##} kg";
        return string.Equals(summary, "N/A", StringComparison.OrdinalIgnoreCase)
            ? weightText
            : $"{summary} · {weightText}";
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

    public static string BuildAddressLine(string? street, string? houseNumber, string? postalCode, string? city)
    {
        return JoinNonEmpty(", ", BuildStreetLine(street, houseNumber), JoinNonEmpty(" ", postalCode, city));
    }

    private static string JoinNonEmpty(string separator, params string?[] values)
    {
        return string.Join(
            separator,
            values.Select(value => (value ?? string.Empty).Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
