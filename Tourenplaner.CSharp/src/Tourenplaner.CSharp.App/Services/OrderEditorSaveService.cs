using System.Text.Json;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

/// <summary>Commit an order editor result together with its tour references before updating the UI.</summary>
internal static class OrderEditorSaveService
{
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

    public static async Task<OrderEditorSaveResult> SaveAsync(
        IOrderRepository orderRepository, ITourRecordStore tourRepository,
        Order original, Order edited, Action<TourRecord>? applySchedule = null,
        CancellationToken cancellationToken = default)
    {
        await SaveGate.WaitAsync(cancellationToken);
        try
        {
            // Never reconcile against an old UI list: it may omit another user's new orders.
            var orders = OrderMaintenancePersistenceService.CreateSnapshot(
                await orderRepository.GetAllAsync(cancellationToken)).ToList();
            var current = orders.FirstOrDefault(o => SameId(o.Id, original.Id));
            if (current is null || !Equivalent(current, original))
                throw new ConcurrencyConflictException("Auftrag", original.Id);

            var candidate = OrderEditPersistenceService.CreateCandidate(edited);
            var renamed = !SameId(original.Id, candidate.Id);
            if (renamed && orders.Any(o => SameId(o.Id, candidate.Id)))
                throw new InvalidOperationException($"Die Auftragsnummer {candidate.Id} ist bereits vorhanden. Bitte eine andere Nummer verwenden.");
            // Case-only changes must retain the actual repository key.
            if (!renamed) candidate.Id = current.Id;
            candidate.ConcurrencyToken = renamed ? null : current.ConcurrencyToken;
            if (!new OrderDeliveryService().TryValidateUpdate(current, candidate, out var validationError))
                throw new InvalidOperationException(validationError);

            var originalTours = TourReconciliationPersistenceService.CreateSnapshot(
                await tourRepository.LoadAsync(cancellationToken));
            var tours = TourReconciliationPersistenceService.CreateSnapshot(originalTours).ToList();
            if (renamed) TourOrderReferenceService.RenameOrderReferences(tours, original.Id, candidate.Id);

            var addressChanged = OrderPinSaveValidationService.HasDeliveryAddressChanged(current, candidate);
            var reviewTours = tours.Where(t => !t.IsArchived && t.Stops.Any(s =>
                SameId(s.Auftragsnummer, candidate.Id) && !TourStopIdentity.IsCompanyStop(s))).ToList();
            var requiresReview = addressChanged && DeliveryMethodExtensions.CanUseLiefertour(candidate) && reviewTours.Count > 0;
            if (requiresReview)
            {
                candidate.AvisoStatus = "nicht avisiert";
                foreach (var tour in reviewTours)
                {
                    TourAddressReviewService.Mark(tour, [candidate.Id]);
                    foreach (var stop in tour.Stops.Where(s => SameId(s.Auftragsnummer, candidate.Id)))
                    {
                        stop.IsArrivalWindowFixed = false;
                        stop.ArrivalWindowStart = stop.ArrivalWindowEnd = null;
                    }
                }
            }

            orders.RemoveAll(o => SameId(o.Id, original.Id));
            orders.Add(candidate);
            // Only reconcile tours actually affected by this edit. Unrelated tours must not be cleaned up here.
            var affectedTours = tours.Where(t => t.Stops.Any(s => SameId(s.Auftragsnummer, candidate.Id))).ToList();
            var cleanup = TourOrderReferenceService.ReconcileActiveToursWithOrders(affectedTours, orders);
            var deletedIds = cleanup.DeletedTourIds.ToHashSet();
            tours.RemoveAll(t => deletedIds.Contains(t.Id));
            foreach (var id in cleanup.RescheduledTourIds)
            {
                var tour = tours.FirstOrDefault(t => t.Id == id);
                // A marked tour must use freshly calculated routing, never the invalidated cache.
                if (tour is not null && tour.AddressReviewOrderIds.Count == 0)
                    (applySchedule ?? (t => new TourScheduleService().ApplySchedule(t)))(tour);
            }
            new OrderDeliveryService().SynchronizeTourAssignments([candidate], tours);
            var beforeById = originalTours.ToDictionary(t => t.Id);
            var changedTours = tours.Where(t => !Equivalent(t, beforeById[t.Id])).ToList();
            var deletedTours = originalTours.Where(t => deletedIds.Contains(t.Id)).ToList();

            if (tourRepository is ITourOrderTransactionStore transaction)
            {
                await transaction.SaveAsync(changedTours, [candidate], deletedTours,
                    renamed ? [current] : [], cancellationToken);
            }
            else
            {
                await SaveFallbackAsync(orderRepository, tourRepository, current, candidate, renamed,
                    orders, originalTours, tours, changedTours.Count > 0 || deletedTours.Count > 0, cancellationToken);
            }
            return new(candidate, changedTours.Count > 0 || deletedTours.Count > 0, requiresReview,
                orders, changedTours.Select(t => new OrderEditorTourCommit(t, beforeById[t.Id].ConcurrencyToken)).ToList());
        }
        finally { SaveGate.Release(); }
    }

    private static async Task SaveFallbackAsync(IOrderRepository orderRepository, ITourRecordStore tourRepository,
        Order original, Order candidate, bool renamed, IReadOnlyList<Order> orders,
        IReadOnlyList<TourRecord> originalTours, IReadOnlyList<TourRecord> tours, bool toursChanged,
        CancellationToken cancellationToken)
    {
        // File repositories have no cross-file transaction. Write the replacement first, keep the old
        // order until the tour write succeeds, and compensate completed writes on failure.
        var mutation = orderRepository as IOrderMutationRepository;
        var orderWritten = false;
        var toursWritten = false;
        try
        {
            if (mutation is not null) await mutation.UpsertAsync(candidate, cancellationToken);
            else await orderRepository.SaveAllAsync(renamed ? orders.Append(original) : orders, cancellationToken);
            orderWritten = true;
            if (toursChanged)
            {
                await tourRepository.SaveAsync(tours, cancellationToken);
                toursWritten = true;
            }
            if (renamed)
            {
                if (mutation is not null) await mutation.DeleteAsync(original.Id, original.ConcurrencyToken, cancellationToken);
                else await orderRepository.SaveAllAsync(orders, cancellationToken);
            }
        }
        catch (Exception saveError)
        {
            try
            {
                // Recovery must also run after cancellation of the original operation.
                if (toursWritten) await tourRepository.SaveAsync(originalTours, CancellationToken.None);
                if (orderWritten)
                {
                    if (mutation is not null)
                    {
                        await mutation.UpsertAsync(original, CancellationToken.None);
                        if (renamed) await mutation.DeleteAsync(candidate.Id, candidate.ConcurrencyToken, CancellationToken.None);
                    }
                    else await orderRepository.SaveAllAsync(orders.Where(o => !SameId(o.Id, candidate.Id)).Append(original), CancellationToken.None);
                }
            }
            catch (Exception recoveryError)
            {
                throw new AggregateException("Speichern und Wiederherstellen fehlgeschlagen. Bitte Auftrags- und Tourdaten neu laden und prüfen.", saveError, recoveryError);
            }
            throw;
        }
    }

    private static bool SameId(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Equivalent<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
}

internal sealed record OrderEditorSaveResult(Order Order, bool ToursChanged, bool RequiresAddressReview,
    IReadOnlyList<Order> CurrentOrders, IReadOnlyList<OrderEditorTourCommit> TourCommits);

internal sealed record OrderEditorTourCommit(TourRecord Tour, string? ExpectedToken);
