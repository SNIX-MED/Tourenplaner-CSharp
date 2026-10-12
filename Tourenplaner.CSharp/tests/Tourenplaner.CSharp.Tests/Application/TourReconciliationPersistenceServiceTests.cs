using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourReconciliationPersistenceServiceTests
{
    [Fact]
    public async Task SaveAsync_ExplicitlyDeletesToursRemovedByReconciliation()
    {
        var removed = new TourRecord { Id = 7, ConcurrencyToken = "token-7" };
        var retained = new TourRecord { Id = 8, ConcurrencyToken = "token-8" };
        var repository = new RecordingTourStore();
        var cleanup = new TourOrderReferenceReconciliationResult(1, [], [7], []);

        await TourReconciliationPersistenceService.SaveAsync(
            repository,
            [removed, retained],
            [retained],
            cleanup);

        Assert.Equal([8], repository.SavedTourIds);
        Assert.Equal([(7, "token-7")], repository.DeletedTours);
    }

    private sealed class RecordingTourStore : ITourRecordStore, ITourRecordMutationStore
    {
        public List<int> SavedTourIds { get; } = [];
        public List<(int TourId, string? Token)> DeletedTours { get; } = [];

        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TourRecord>>([]);

        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default)
        {
            SavedTourIds.AddRange(tours.Select(x => x.Id));
            return Task.CompletedTask;
        }

        public Task<TourRecord?> GetByIdAsync(int tourId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TourRecord?>(null);

        public Task UpsertAsync(TourRecord tour, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(int tourId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
        {
            DeletedTours.Add((tourId, concurrencyToken));
            return Task.CompletedTask;
        }
    }
}
