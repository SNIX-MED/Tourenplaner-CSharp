using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class KarteSectionViewModel
{
    internal Task RefreshAfterRemoteChangeAsync(AppDataChangedEventArgs change) => _remoteRefreshQueue.EnqueueAsync(change);

    private async Task RefreshRemoteBatchAsync(IReadOnlyList<AppDataChangedEventArgs> changes)
    {
        await _tourAccessSyncGate.WaitAsync();
        try
        {
            if (IsLoadedTourEditing || _hasUnsavedRouteChanges)
            {
                DeferRemoteChanges(changes);
                StatusText = "Externe Änderungen liegen vor. Sie werden nach Abschluss der Bearbeitung geladen.";
                return;
            }
            var pending = _deferredRemoteChanges.Concat(changes).Distinct().ToArray();
            _deferredRemoteChanges.Clear();
            _hasDeferredRemoteRefresh = false;
            try { await ApplyRemoteSnapshotAsync(pending); }
            catch
            {
                DeferRemoteChanges(pending);
                StatusText = "Externe Änderungen konnten noch nicht geladen werden. Bitte die Ansicht erneut aktualisieren.";
                throw;
            }
        }
        finally { _tourAccessSyncGate.Release(); }
    }

    private void DeferRemoteChanges(IEnumerable<AppDataChangedEventArgs> changes)
    {
        _hasDeferredRemoteRefresh = true;
        foreach (var change in changes)
            if (!_deferredRemoteChanges.Contains(change)) _deferredRemoteChanges.Add(change);
    }

    private async Task RefreshDeferredRemoteChangesAsync()
    {
        if (!_hasDeferredRemoteRefresh || IsLoadedTourEditing || _hasUnsavedRouteChanges) return;
        var changes = _deferredRemoteChanges.ToArray();
        if (changes.Length == 0)
            changes = [new(Guid.Empty, AppDataKind.Orders | AppDataKind.Tours | AppDataKind.Settings |
                AppDataKind.Employees | AppDataKind.Vehicles)];
        _deferredRemoteChanges.Clear();
        _hasDeferredRemoteRefresh = false;
        try { await ApplyRemoteSnapshotAsync(changes); }
        catch { DeferRemoteChanges(changes); throw; }
    }

    private async Task ApplyRemoteSnapshotAsync(IReadOnlyList<AppDataChangedEventArgs> changes)
    {
        await _orderRefreshGate.WaitAsync();
        try
        {
            // Take the snapshot before replacing any lists or selection. A remote refresh is read-only;
            // normalization/geocoding belongs to startup, explicit refresh and actual edits.
            var viewedTourId = IsLoadedTourReadOnly ? LoadedTourId : 0;
            var loadedTourIdAtStart = LoadedTourId;
            var selectedOrderId = SelectedOrder?.OrderId;
            var selectedDeliveryId = SelectedOrder?.DeliveryId;
            var selectedStopOrderId = SelectedRouteStop?.OrderId;
            var selectedStopDeliveryId = SelectedRouteStop?.DeliveryId;
            var kinds = changes.Aggregate(AppDataKind.None, (all, change) => all | change.Kinds);
            foreach (var change in changes.Where(c => (c.Kinds & AppDataKind.Orders) != 0))
            {
                var orderChange = new OrderChangedEventArgs(change.SourceId, change.PreviousId, change.CurrentId);
                selectedOrderId = ResolvePreferredOrderId(orderChange, selectedOrderId);
                selectedStopOrderId = ResolvePreferredOrderId(orderChange, selectedStopOrderId);
            }

            var fullRefresh = (kinds & (AppDataKind.Settings | AppDataKind.Employees | AppDataKind.Vehicles)) != 0;
            if (fullRefresh)
            {
                if (!await RefreshCoreAsync(maintainData: false, expectedLoadedTourId: loadedTourIdAtStart))
                {
                    DeferRemoteChanges(changes);
                    return;
                }
            }
            else
            {
                var ordersTask = _orderRepository.GetAllAsync();
                var toursTask = _tourRepository.LoadAsync();
                await Task.WhenAll(ordersTask, toursTask);
                var orders = DeduplicateOrders(await ordersTask);
                var tours = await toursTask;
                if (IsLoadedTourEditing || _hasUnsavedRouteChanges || LoadedTourId != loadedTourIdAtStart)
                {
                    DeferRemoteChanges(changes);
                    return;
                }
                _allOrders.Clear();
                _allOrders.AddRange(orders);
                await LoadSavedToursAsync(viewedTourId > 0 ? viewedTourId : null, reconcile: false, loadedTours: tours);
                RefreshOrderFilterOptions();
            }

            var currentTour = viewedTourId > 0 ? _savedTours.FirstOrDefault(t => t.Id == viewedTourId) : null;
            if (viewedTourId > 0 && currentTour is null)
            {
                // Deleted/archived tours must not remain visible as apparently editable snapshots.
                ClearRoute();
            }
            else if (currentTour is not null &&
                     (!string.Equals(_displayedTourSnapshot, TourDisplaySnapshot(currentTour), StringComparison.Ordinal) ||
                      (kinds & (AppDataKind.Settings | AppDataKind.Vehicles)) != 0))
            {
                var recalculate = !string.Equals(_displayedTourRoutingSnapshot, TourRoutingSnapshot(currentTour), StringComparison.Ordinal) ||
                                  (kinds & (AppDataKind.Settings | AppDataKind.Vehicles)) != 0;
                await LoadTourIntoRouteAsync(viewedTourId, recalculate);
                StatusText = "Tour wurde aktualisiert (Nur Ansicht).";
            }

            // Also refresh labels/status for orders whose tour payload itself did not change.
            var orderChanges = changes.Where(c => (c.Kinds & AppDataKind.Orders) != 0).ToArray();
            // Resolve complete rename chains before looking up the final repository snapshot.
            // Looking up the intermediate ID (A -> B -> C) would wrongly remove the stop at B.
            foreach (var change in orderChanges)
            {
                if (string.IsNullOrWhiteSpace(change.PreviousId) || string.IsNullOrWhiteSpace(change.CurrentId)) continue;
                foreach (var stop in RouteStops.Where(s => !s.IsManualStop && !s.IsPauseStop && !s.IsCompanyAnchor &&
                    string.Equals(s.OrderId, change.PreviousId, StringComparison.OrdinalIgnoreCase)))
                    stop.OrderId = change.CurrentId;
            }
            if (orderChanges.Length > 0) RefreshRouteStopsFromOrders(new(Guid.Empty, null, null));
            RebuildOrderGrid(selectedOrderId);
            if (!string.IsNullOrWhiteSpace(selectedOrderId) && !string.IsNullOrWhiteSpace(selectedDeliveryId))
            {
                var selectedStop = RouteStops.FirstOrDefault(s => string.Equals(s.OrderId, selectedOrderId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.DeliveryId, selectedDeliveryId, StringComparison.OrdinalIgnoreCase));
                SelectedOrder = selectedStop is not null ? ResolveRoutePinInfoCard(selectedStop) :
                    MapOrders.FirstOrDefault(o => string.Equals(o.OrderId, selectedOrderId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(o.DeliveryId, selectedDeliveryId, StringComparison.OrdinalIgnoreCase)) ?? SelectedOrder;
            }
            SelectedRouteStop = RouteStops.FirstOrDefault(s => string.Equals(s.OrderId, selectedStopOrderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.DeliveryId, selectedStopDeliveryId, StringComparison.OrdinalIgnoreCase));
            RebuildTourOverviewItems();
            UpdateRouteSummary();
            RebuildPlannedTourRouteOverlays();
        }
        finally { _orderRefreshGate.Release(); }
    }

    private static string TourDisplaySnapshot(TourRecord tour)
    {
        var node = JsonSerializer.SerializeToNode(tour)!.AsObject();
        node.Remove(nameof(TourRecord.ConcurrencyToken));
        return node.ToJsonString();
    }

    private static string TourRoutingSnapshot(TourRecord tour) => JsonSerializer.Serialize(new
    {
        tour.Date, tour.StartTime, tour.RouteMode, tour.VehicleId, tour.TrailerId, tour.SecondaryVehicleId,
        tour.SecondaryTrailerId, tour.RouteGeometryPoints, tour.RouteGeometrySignature,
        Stops = tour.Stops.OrderBy(s => s.Order).Select(s => new { s.Id, s.StopKind, s.Lat, s.Lng, s.Lon })
    });
}
