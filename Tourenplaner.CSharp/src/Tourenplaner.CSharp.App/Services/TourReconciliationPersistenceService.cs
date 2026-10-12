using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

internal static class TourReconciliationPersistenceService
{
    public static async Task SaveAsync(
        ITourRecordStore repository,
        IReadOnlyList<TourRecord> originalTours,
        IReadOnlyList<TourRecord> reconciledTours,
        TourOrderReferenceReconciliationResult cleanup,
        CancellationToken cancellationToken = default)
    {
        await repository.SaveAsync(reconciledTours, cancellationToken);

        if (repository is not ITourRecordMutationStore mutationStore)
        {
            return;
        }

        foreach (var tourId in cleanup.DeletedTourIds.Distinct())
        {
            var concurrencyToken = originalTours.FirstOrDefault(x => x.Id == tourId)?.ConcurrencyToken;
            await mutationStore.DeleteAsync(tourId, concurrencyToken, cancellationToken);
        }
    }
}
