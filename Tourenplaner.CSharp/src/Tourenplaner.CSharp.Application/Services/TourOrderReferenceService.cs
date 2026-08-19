using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed record TourOrderReferenceCleanupResult(
    int RemovedStopCount,
    IReadOnlyList<int> ChangedTourIds)
{
    public bool HasChanges => RemovedStopCount > 0;
}

public sealed record TourOrderReferenceReconciliationResult(
    int RemovedStopCount,
    IReadOnlyList<int> RescheduledTourIds,
    IReadOnlyList<int> DeletedTourIds,
    IReadOnlyList<int> ArchivedTourIds)
{
    public bool HasChanges => RemovedStopCount > 0 ||
                              DeletedTourIds.Count > 0 ||
                              ArchivedTourIds.Count > 0;
}

public static class TourOrderReferenceService
{
    public static TourOrderReferenceCleanupResult RemoveStopsWithoutOrders(
        IEnumerable<TourRecord> tours,
        IEnumerable<string> existingOrderIds,
        bool activeToursOnly = true)
    {
        var validOrderIds = (existingOrderIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        var changedTourIds = new List<int>();

        foreach (var tour in tours ?? [])
        {
            if (tour is null || (activeToursOnly && tour.IsArchived))
            {
                continue;
            }

            var stops = tour.Stops ?? [];
            var nextStops = stops
                .Where(stop => !ShouldRemoveStop(stop, validOrderIds))
                .ToList();

            var removedFromTour = stops.Count - nextStops.Count;
            if (removedFromTour == 0)
            {
                continue;
            }

            tour.Stops = nextStops;
            removed += removedFromTour;
            changedTourIds.Add(tour.Id);
        }

        return new TourOrderReferenceCleanupResult(removed, changedTourIds);
    }

    public static TourOrderReferenceReconciliationResult ReconcileActiveToursWithOrders(
        IList<TourRecord> tours,
        IEnumerable<Order> orders)
    {
        var orderById = BuildOrderLookup(orders);
        var validOrderIds = orderById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        var rescheduledTourIds = new List<int>();
        var deletedTourIds = new List<int>();
        var archivedTourIds = new List<int>();

        foreach (var tour in tours.Where(x => x is not null && !x.IsArchived).ToList())
        {
            var stops = tour.Stops ?? [];
            var nextStops = stops
                .Where(stop => !ShouldRemoveStop(stop, validOrderIds))
                .ToList();

            var removedFromTour = stops.Count - nextStops.Count;
            if (removedFromTour > 0)
            {
                tour.Stops = nextStops;
                removed += removedFromTour;
            }

            var orderStops = (tour.Stops ?? [])
                .Where(IsOrderStop)
                .ToList();
            if (orderStops.Count == 0)
            {
                deletedTourIds.Add(tour.Id);
                continue;
            }

            if (orderStops.All(stop =>
                orderById.TryGetValue((stop.Auftragsnummer ?? string.Empty).Trim(), out var order) &&
                order.IsArchived))
            {
                tour.IsArchived = true;
                archivedTourIds.Add(tour.Id);
            }

            if (removedFromTour > 0)
            {
                rescheduledTourIds.Add(tour.Id);
            }
        }

        if (deletedTourIds.Count > 0)
        {
            for (var index = tours.Count - 1; index >= 0; index--)
            {
                if (deletedTourIds.Contains(tours[index].Id))
                {
                    tours.RemoveAt(index);
                }
            }
        }

        return new TourOrderReferenceReconciliationResult(
            removed,
            rescheduledTourIds,
            deletedTourIds,
            archivedTourIds);
    }

    private static Dictionary<string, Order> BuildOrderLookup(IEnumerable<Order> orders)
    {
        var orderById = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders ?? [])
        {
            var orderId = (order.Id ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(orderId) && order.Type == OrderType.Map)
            {
                orderById[orderId] = order;
            }
        }

        return orderById;
    }

    private static bool ShouldRemoveStop(TourStopRecord? stop, IReadOnlySet<string> validOrderIds)
    {
        if (stop is null || TourStopIdentity.IsCompanyStop(stop))
        {
            return false;
        }

        if (string.Equals((stop.StopKind ?? string.Empty).Trim(), "pause", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var orderId = (stop.Auftragsnummer ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(orderId) && !validOrderIds.Contains(orderId);
    }

    private static bool IsOrderStop(TourStopRecord? stop)
    {
        if (stop is null || TourStopIdentity.IsCompanyStop(stop))
        {
            return false;
        }

        if (string.Equals((stop.StopKind ?? string.Empty).Trim(), "pause", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(stop.Auftragsnummer);
    }
}
