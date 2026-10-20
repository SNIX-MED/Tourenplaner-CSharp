using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

internal static class XmlImportTourAddressUpdateService
{
    public static async Task UpdateAsync(IReadOnlyList<ImportTourAddressChange> changes, bool recalculate,
        ITourRecordStore tourStore, IOrderRepository orderStore, IAppSettingsStore settingsStore,
        IVehicleDataStore? vehicleStore, string userName,
        Func<IReadOnlyList<GeoPoint>, Task<OsrmRouteResult>>? routeBuilder = null)
    {
        if (changes.Count == 0) return;
        var changedIds = changes.Select(c => c.OrderId.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var tours = (await tourStore.LoadAsync()).ToList();
                var original = TourReconciliationPersistenceService.CreateSnapshot(tours);
                var orders = await orderStore.GetAllAsync();
                // Work only on tours containing the imported orders, including assignments moved during geocoding.
                var affected = tours.Where(t => !t.IsArchived && t.Stops.Any(s =>
                    !TourStopIdentity.IsCompanyStop(s) && changedIds.Contains(s.Auftragsnummer?.Trim() ?? string.Empty))).ToList();
                var affectedIds = affected.Select(t => t.Id).ToHashSet();
                var snapshotTours = original.Where(t => affectedIds.Contains(t.Id)).ToList();
                var destinationSnapshot = TourAddressReviewService.DestinationSnapshot(snapshotTours, orders);
                var reconciliation = TourOrderReferenceService.ReconcileActiveToursWithOrders(affected, orders);
                var settings = recalculate ? await settingsStore.LoadAsync() : new AppSettings();
                var vehicles = recalculate && vehicleStore is not null ? await vehicleStore.LoadAsync() : new VehicleDataRecord();
                foreach (var tour in affected.Where(t => !t.IsArchived))
                {
                    var ids = tour.Stops.Select(s => s.Auftragsnummer?.Trim() ?? string.Empty).Where(changedIds.Contains).ToList();
                    if (!recalculate || ids.Except(tour.AddressReviewOrderIds, StringComparer.OrdinalIgnoreCase).Any())
                        TourAddressReviewService.Mark(tour, ids);
                    if (recalculate)
                        await TourAddressReviewService.RecalculateAsync(tour, orders, settings, vehicles, userName, routeBuilder);
                }
                if (recalculate && destinationSnapshot != TourAddressReviewService.DestinationSnapshot(
                        snapshotTours, await orderStore.GetAllAsync()))
                    throw new ConcurrencyConflictException("Fahrtziel", "(XML-Import)");
                tours.RemoveAll(t => reconciliation.DeletedTourIds.Contains(t.Id));
                await TourReconciliationPersistenceService.SaveAsync(tourStore, original, tours, reconciliation);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 2)
            {
                // Reload and merge with the latest data instead of overwriting another client's changes.
            }
        }
    }
}
