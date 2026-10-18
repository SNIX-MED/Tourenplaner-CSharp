using System.Collections.ObjectModel;
using System.ComponentModel;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class KarteSectionViewModel
{
    private void RebuildOrderGrid(string? preferredSelectedId = null, bool preserveSelectedOrderInstance = false)
    {
        var previousSelectedId = string.IsNullOrWhiteSpace(preferredSelectedId)
            ? SelectedOrder?.OrderId
            : preferredSelectedId;
        RefreshOrderFilterOptions();
        var routeDeliveryIds = RouteStops.Select(s => s.DeliveryId).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacyRouteOrderIds = RouteStops.Where(s => string.IsNullOrWhiteSpace(s.DeliveryId)).Select(s => s.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = (_searchText ?? string.Empty).Trim();
        IEnumerable<Order> filtered = _allOrders
            .Where(o => DeliveryMethodExtensions.CanUseLiefertour(o) && o.Location is not null)
            .Where(o => !o.IsArchived)
            .Where(o => !legacyRouteOrderIds.Contains(o.Id));

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

        var nextMapOrders = new List<MapOrderItem>();
        foreach (var order in matching
                     .OrderBy(o => o.ScheduledDate)
                     .ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            var deliveries = (order.Deliveries ?? [])
                .Where(x => x.Status is OrderDeliveryStatus.ReadyForPlanning or OrderDeliveryStatus.Planned or OrderDeliveryStatus.InTransit)
                .Where(x => !routeDeliveryIds.Contains(x.Id))
                .Where(x => (IncludeOpenOrders && string.IsNullOrWhiteSpace(x.AssignedTourId)) ||
                            (IncludePlannedOrders && !string.IsNullOrWhiteSpace(x.AssignedTourId)))
                .OrderBy(x => x.SequenceNumber)
                .ToList();
            if (deliveries.Count == 0)
            {
                if ((order.Deliveries ?? []).Count == 0 &&
                    ((IncludeOpenOrders && string.IsNullOrWhiteSpace(order.AssignedTourId)) ||
                     (IncludePlannedOrders && !string.IsNullOrWhiteSpace(order.AssignedTourId))))
                {
                    nextMapOrders.Add(BuildMapOrderItem(order));
                }
            }
            else
            {
                foreach (var delivery in deliveries)
                {
                    nextMapOrders.Add(BuildMapOrderItem(order, delivery));
                }
            }
        }

        MapOrders.ReplaceAll(nextMapOrders);

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

        if (preserveSelectedOrderInstance &&
            SelectedOrder is not null &&
            string.Equals(SelectedOrder.OrderId, previousSelectedId, StringComparison.OrdinalIgnoreCase))
        {
            // Editing the map search must not reassign SelectedOrder. Reassignment raises
            // PropertyChanged and would focus the previously clicked order pin again.
        }
        else if (string.IsNullOrWhiteSpace(previousSelectedId))
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
        return _mapSearchDimNonMatchingPins && !string.IsNullOrWhiteSpace(_searchText)
            ? MapOrders.Concat(_dimmedMapOrders).ToList()
            : MapOrders.ToList();
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
                .Where(o => DeliveryMethodExtensions.CanUseLiefertour(o) && o.Location is not null)
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
        OrderSectionSharedHelpers.UpdateFilterOptions(target, values, OnOrderFilterOptionChanged);
    }

    private static int GetOrderStatusSortIndex(string? status)
    {
        var normalized = NormalizeOrderStatus(status);
        return OrderSectionSharedHelpers.GetSortIndex(normalized, _orderStatusOptions, int.MaxValue);
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
               OrderSectionSharedHelpers.AreAllFilterOptionsSelected(
                   OrderStatusFilters,
                   DeliveryTypeFilters,
                   AvisoStatusFilters,
                   SupplierFilters);
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

        OrderSectionSharedHelpers.AddPartialFilterSummary(parts, "Status", OrderStatusFilters);
        OrderSectionSharedHelpers.AddPartialFilterSummary(parts, "Lieferart", DeliveryTypeFilters);
        OrderSectionSharedHelpers.AddPartialFilterSummary(parts, "Aviso", AvisoStatusFilters);
        OrderSectionSharedHelpers.AddPartialFilterSummary(parts, "Lieferant", SupplierFilters);

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

        if (OrderSectionSharedHelpers.IsFilterGroupActive(OrderStatusFilters))
        {
            count++;
        }

        if (OrderSectionSharedHelpers.IsFilterGroupActive(DeliveryTypeFilters))
        {
            count++;
        }

        if (OrderSectionSharedHelpers.IsFilterGroupActive(AvisoStatusFilters))
        {
            count++;
        }

        if (OrderSectionSharedHelpers.IsFilterGroupActive(SupplierFilters))
        {
            count++;
        }

        return count;
    }

    private static bool OrderContainsAnySupplier(Order order, IReadOnlySet<string> suppliers)
    {
        return OrderSectionSharedHelpers.OrderContainsAnySupplier(order, suppliers, NormalizeSupplier);
    }

    private static string NormalizeSupplier(string? supplier)
    {
        return OrderSectionSharedHelpers.NormalizeFilterLabel(supplier, UnspecifiedSupplierFilterOption);
    }
}
