using System.Collections.ObjectModel;
using System.ComponentModel;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class KarteSectionViewModel
{
    private void RebuildOrderGrid(string? preferredSelectedId = null)
    {
        var previousSelectedId = string.IsNullOrWhiteSpace(preferredSelectedId)
            ? SelectedOrder?.OrderId
            : preferredSelectedId;
        RefreshOrderFilterOptions();
        var routeOrderIds = RouteStops.Select(s => s.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = (_searchText ?? string.Empty).Trim();
        IEnumerable<Order> filtered = _allOrders
            .Where(o => o.Type == OrderType.Map && o.Location is not null)
            .Where(o => !o.IsArchived)
            .Where(o => !routeOrderIds.Contains(o.Id));

        if (!IncludeOpenOrders)
        {
            filtered = filtered.Where(o => !string.IsNullOrWhiteSpace(o.AssignedTourId));
        }

        if (!IncludePlannedOrders)
        {
            filtered = filtered.Where(o => string.IsNullOrWhiteSpace(o.AssignedTourId));
        }

        var selectedOrderStatuses = OrderSectionSharedHelpers.GetSelectedFilterLabels(OrderStatusFilters);
        if (selectedOrderStatuses.Count != 0 && selectedOrderStatuses.Count != OrderStatusFilters.Count)
        {
            filtered = filtered.Where(o => selectedOrderStatuses.Contains(NormalizeOrderStatus(o.OrderStatus)));
        }

        var selectedDeliveryTypes = OrderSectionSharedHelpers.GetSelectedFilterLabels(DeliveryTypeFilters);
        if (selectedDeliveryTypes.Count != 0 && selectedDeliveryTypes.Count != DeliveryTypeFilters.Count)
        {
            filtered = filtered.Where(o => selectedDeliveryTypes.Contains(NormalizeDeliveryType(o.DeliveryType)));
        }

        var selectedAvisoStatuses = OrderSectionSharedHelpers.GetSelectedFilterLabels(AvisoStatusFilters);
        if (selectedAvisoStatuses.Count != 0 && selectedAvisoStatuses.Count != AvisoStatusFilters.Count)
        {
            filtered = filtered.Where(o => selectedAvisoStatuses.Contains(NormalizeAvisoStatus(o.AvisoStatus)));
        }

        var selectedSuppliers = OrderSectionSharedHelpers.GetSelectedFilterLabels(SupplierFilters)
            .Select(NormalizeSupplier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSuppliers.Count > 0 && selectedSuppliers.Count != SupplierFilters.Count)
        {
            filtered = filtered.Where(o => OrderContainsAnySupplier(o, selectedSuppliers));
        }

        var filteredList = filtered.ToList();
        var hasSearch = !string.IsNullOrWhiteSpace(query);
        var matching = hasSearch
            ? filteredList.Where(o => MatchesSearchQuery(o, query)).ToList()
            : filteredList;
        var nonMatching = hasSearch
            ? filteredList.Where(o => !MatchesSearchQuery(o, query)).ToList()
            : new List<Order>();

        MapOrders.Clear();
        foreach (var order in matching
                     .OrderBy(o => o.ScheduledDate)
                     .ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            MapOrders.Add(BuildMapOrderItem(order));
        }

        _dimmedMapOrders.Clear();
        if (_mapSearchDimNonMatchingPins && hasSearch)
        {
            foreach (var order in nonMatching
                         .OrderBy(o => o.ScheduledDate)
                         .ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
            {
                _dimmedMapOrders.Add(BuildMapOrderItem(order, isDimmed: true));
            }
        }

        var validOrderIds = _allOrders
            .Select(x => (x.Id ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_selectedBatchOrderIds.RemoveWhere(x => !validOrderIds.Contains(x)) > 0)
        {
            NotifyBatchOrderSelectionChanged(raiseCommandStates: false);
        }

        if (string.IsNullOrWhiteSpace(previousSelectedId))
        {
            SelectedOrder = null;
        }
        else
        {
            SelectOrderDetailsById(previousSelectedId);
        }
        UpdateStatus();
        RaiseCommandStates();
    }

    public IReadOnlyList<MapOrderItem> GetMapMarkerSnapshot()
    {
        if (!_mapSearchDimNonMatchingPins || string.IsNullOrWhiteSpace(_searchText))
        {
            return MapOrders.ToList();
        }

        return MapOrders.Concat(_dimmedMapOrders).ToList();
    }

    private void ResetOrderFilters()
    {
        _suppressFilterRefresh = true;
        try
        {
            IncludeOpenOrders = true;
            IncludePlannedOrders = true;
            OrderSectionSharedHelpers.SetAllFilterOptions(OrderStatusFilters, true);
            OrderSectionSharedHelpers.SetAllFilterOptions(DeliveryTypeFilters, true);
            OrderSectionSharedHelpers.SetAllFilterOptions(AvisoStatusFilters, true);
            OrderSectionSharedHelpers.SetAllFilterOptions(SupplierFilters, true);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        TriggerOrderFilterRefresh();
    }

    private void TriggerOrderFilterRefresh()
    {
        OnPropertyChanged(nameof(FilterSummaryText));
        OnPropertyChanged(nameof(ActiveFilterGroupCount));
        OnPropertyChanged(nameof(HasActiveFilterGroups));
        OnPropertyChanged(nameof(ToggleAllFiltersButtonText));
        if (_suppressFilterRefresh)
        {
            return;
        }

        RebuildOrderGrid();
    }

    private void RefreshOrderFilterOptions()
    {
        if (_isUpdatingFilterOptions)
        {
            return;
        }

        _isUpdatingFilterOptions = true;
        try
        {
            var mapOrders = _allOrders
                .Where(o => o.Type == OrderType.Map && o.Location is not null)
                .Where(o => !o.IsArchived)
                .ToList();

            var statuses = mapOrders
                .Select(o => NormalizeOrderStatus(o.OrderStatus))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetOrderStatusSortIndex)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var deliveryTypes = mapOrders
                .Select(o => NormalizeDeliveryType(o.DeliveryType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var avisoStatuses = mapOrders
                .Select(o => NormalizeAvisoStatus(o.AvisoStatus))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var suppliers = mapOrders
                .SelectMany(o => o.Products ?? [])
                .Select(p => NormalizeSupplier(p.Supplier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            UpdateFilterOptions(OrderStatusFilters, statuses);
            UpdateFilterOptions(DeliveryTypeFilters, deliveryTypes);
            UpdateFilterOptions(AvisoStatusFilters, avisoStatuses);
            UpdateFilterOptions(SupplierFilters, suppliers);
        }
        finally
        {
            _isUpdatingFilterOptions = false;
        }

        OnPropertyChanged(nameof(FilterSummaryText));
        OnPropertyChanged(nameof(ActiveFilterGroupCount));
        OnPropertyChanged(nameof(HasActiveFilterGroups));
        OnPropertyChanged(nameof(ToggleAllFiltersButtonText));
    }

    private void UpdateFilterOptions(ObservableCollection<MapOrderFilterOption> target, IReadOnlyList<string> values)
    {
        var previouslyAvailable = target
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previouslySelected = target
            .Where(x => x.IsSelected)
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepCurrentSelection = target.Count > 0;

        foreach (var option in target)
        {
            option.PropertyChanged -= OnOrderFilterOptionChanged;
        }

        target.Clear();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var isNewValue = !previouslyAvailable.Contains(value);
            var isSelected = !keepCurrentSelection || previouslySelected.Contains(value) || isNewValue;
            var option = new MapOrderFilterOption(value, isSelected);
            option.PropertyChanged += OnOrderFilterOptionChanged;
            target.Add(option);
        }
    }

    private static int GetOrderStatusSortIndex(string? status)
    {
        var normalized = NormalizeOrderStatus(status);
        for (var i = 0; i < _orderStatusOptions.Count; i++)
        {
            if (string.Equals(_orderStatusOptions[i], normalized, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private void OnOrderFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingFilterOptions || e.PropertyName != nameof(MapOrderFilterOption.IsSelected))
        {
            return;
        }

        TriggerOrderFilterRefresh();
    }

    private void ToggleAllOrderFilters()
    {
        var selectAll = !AreAllFiltersSelected();
        _suppressFilterRefresh = true;
        try
        {
            IncludeOpenOrders = selectAll;
            IncludePlannedOrders = selectAll;
            OrderSectionSharedHelpers.SetAllFilterOptions(OrderStatusFilters, selectAll);
            OrderSectionSharedHelpers.SetAllFilterOptions(DeliveryTypeFilters, selectAll);
            OrderSectionSharedHelpers.SetAllFilterOptions(AvisoStatusFilters, selectAll);
            OrderSectionSharedHelpers.SetAllFilterOptions(SupplierFilters, selectAll);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        TriggerOrderFilterRefresh();
    }

    private bool AreAllFiltersSelected()
    {
        return IncludeOpenOrders &&
               IncludePlannedOrders &&
               OrderStatusFilters.All(x => x.IsSelected) &&
               DeliveryTypeFilters.All(x => x.IsSelected) &&
               AvisoStatusFilters.All(x => x.IsSelected) &&
               SupplierFilters.All(x => x.IsSelected);
    }

    private string BuildFilterSummaryText()
    {
        var parts = new List<string>();
        if (IncludeOpenOrders && !IncludePlannedOrders)
        {
            parts.Add("nur offen");
        }
        else if (!IncludeOpenOrders && IncludePlannedOrders)
        {
            parts.Add("nur eingeplant");
        }
        else if (!IncludeOpenOrders && !IncludePlannedOrders)
        {
            parts.Add("keine Tourzuordnung");
        }

        AddPartialSummary(parts, "Status", OrderStatusFilters);
        AddPartialSummary(parts, "Lieferart", DeliveryTypeFilters);
        AddPartialSummary(parts, "Aviso", AvisoStatusFilters);
        AddPartialSummary(parts, "Lieferant", SupplierFilters);

        return parts.Count == 0
            ? "Filter (alle Aufträge)"
            : $"Filter ({string.Join(" | ", parts)})";
    }

    private int CountActiveFilterGroups()
    {
        var count = 0;
        if (!(IncludeOpenOrders && IncludePlannedOrders))
        {
            count++;
        }

        if (IsFilterGroupActive(OrderStatusFilters))
        {
            count++;
        }

        if (IsFilterGroupActive(DeliveryTypeFilters))
        {
            count++;
        }

        if (IsFilterGroupActive(AvisoStatusFilters))
        {
            count++;
        }

        if (IsFilterGroupActive(SupplierFilters))
        {
            count++;
        }

        return count;
    }

    private static bool IsFilterGroupActive(ObservableCollection<MapOrderFilterOption> options)
    {
        return options.Count > 0 && options.Any(x => !x.IsSelected);
    }

    private static void AddPartialSummary(
        List<string> parts,
        string label,
        ObservableCollection<MapOrderFilterOption> options)
    {
        if (options.Count == 0)
        {
            return;
        }

        var selectedCount = options.Count(x => x.IsSelected);
        if (selectedCount == options.Count)
        {
            return;
        }

        parts.Add($"{label}: {selectedCount}/{options.Count}");
    }

    private static bool OrderContainsAnySupplier(Order order, IReadOnlySet<string> suppliers)
    {
        return OrderSectionSharedHelpers.OrderContainsAnySupplier(order, suppliers, NormalizeSupplier);
    }

    private static string NormalizeSupplier(string? supplier)
    {
        var normalized = (supplier ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? UnspecifiedSupplierFilterOption
            : normalized;
    }
}
