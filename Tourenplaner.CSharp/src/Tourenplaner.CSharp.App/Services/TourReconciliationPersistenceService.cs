using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tourenplaner.CSharp.App.Services;

internal static class TourReconciliationPersistenceService
{
    private static readonly SemaphoreSlim PersistenceGate = new(1, 1);

    public static IReadOnlyList<TourRecord> CreateSnapshot(IEnumerable<TourRecord> tours) =>
        (tours ?? [])
            .Where(x => x is not null)
            .Select(x => JsonSerializer.Deserialize<TourRecord>(JsonSerializer.Serialize(x)) ?? new TourRecord { Id = x.Id })
            .ToList();

    public static async Task SaveAsync(
        ITourRecordStore repository,
        IReadOnlyList<TourRecord> originalTours,
        IReadOnlyList<TourRecord> reconciledTours,
        TourOrderReferenceReconciliationResult cleanup,
        CancellationToken cancellationToken = default)
    {
        if (repository is not ITourRecordMutationStore mutationStore)
        {
            await repository.SaveAsync(reconciledTours, cancellationToken);
            return;
        }

        await PersistenceGate.WaitAsync(cancellationToken);
        try
        {
            var originalById = originalTours
                .Where(x => x is not null)
                .GroupBy(x => x.Id)
                .ToDictionary(x => x.Key, x => x.Last());
            var deletedTourIds = cleanup.DeletedTourIds.ToHashSet();

            foreach (var tour in reconciledTours.Where(x => x is not null && !deletedTourIds.Contains(x.Id)))
            {
                if (originalById.TryGetValue(tour.Id, out var original) && AreEquivalent(original, tour))
                {
                    continue;
                }

                await UpsertIdempotentlyAsync(mutationStore, tour, cancellationToken);
            }

            foreach (var tourId in deletedTourIds)
            {
                var concurrencyToken = originalTours.FirstOrDefault(x => x.Id == tourId)?.ConcurrencyToken;
                await mutationStore.DeleteAsync(tourId, concurrencyToken, cancellationToken);
            }
        }
        finally
        {
            PersistenceGate.Release();
        }
    }

    private static async Task UpsertIdempotentlyAsync(
        ITourRecordMutationStore mutationStore,
        TourRecord tour,
        CancellationToken cancellationToken)
    {
        try
        {
            await mutationStore.UpsertAsync(tour, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            var current = await mutationStore.GetByIdAsync(tour.Id, cancellationToken);
            if (current is null || !AreEquivalent(current, tour))
            {
                throw;
            }

            tour.ConcurrencyToken = current.ConcurrencyToken;
        }
    }

    private static bool AreEquivalent(TourRecord left, TourRecord right) =>
        string.Equals(SerializeWithoutConcurrencyToken(left), SerializeWithoutConcurrencyToken(right), StringComparison.Ordinal);

    private static string SerializeWithoutConcurrencyToken(TourRecord tour)
    {
        var node = JsonSerializer.SerializeToNode(tour) as JsonObject;
        node?.Remove(nameof(TourRecord.ConcurrencyToken));
        return node?.ToJsonString() ?? string.Empty;
    }
}
