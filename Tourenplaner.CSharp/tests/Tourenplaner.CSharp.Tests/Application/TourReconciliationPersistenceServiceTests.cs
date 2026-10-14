using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourReconciliationPersistenceServiceTests
{
    [Fact]
    public void CreateSnapshot_IsNotChangedWhenSourceTourIsMutated()
    {
        var source = new TourRecord
        {
            Id = 8,
            Name = "Vorher",
            Stops = [new TourStopRecord { Id = "stop-1", Name = "Vorher" }]
        };

        var snapshot = Assert.Single(TourReconciliationPersistenceService.CreateSnapshot([source]));
        source.Name = "Nachher";
        source.Stops[0].Name = "Nachher";

        Assert.Equal("Vorher", snapshot.Name);
        Assert.Equal("Vorher", Assert.Single(snapshot.Stops).Name);
    }

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

        Assert.Empty(repository.SavedTourIds);
        Assert.Empty(repository.UpsertedTourIds);
        Assert.Equal([(7, "token-7")], repository.DeletedTours);
    }

    [Fact]
    public async Task SaveAsync_UpsertsOnlyToursChangedByReconciliation()
    {
        var unchanged = new TourRecord { Id = 7, Name = "Unverändert", ConcurrencyToken = "old-7" };
        var changedBefore = new TourRecord { Id = 8, Name = "Vorher", ConcurrencyToken = "old-8" };
        var changedAfter = new TourRecord { Id = 8, Name = "Nachher", ConcurrencyToken = "old-8" };
        var repository = new RecordingTourStore();

        await TourReconciliationPersistenceService.SaveAsync(
            repository,
            [unchanged, changedBefore],
            [unchanged, changedAfter],
            new TourOrderReferenceReconciliationResult(0, [8], [], []));

        Assert.Empty(repository.SavedTourIds);
        Assert.Equal([8], repository.UpsertedTourIds);
    }

    [Fact]
    public async Task SaveAsync_AcceptsConcurrentWriteWhenDesiredTourIsAlreadyStored()
    {
        var before = new TourRecord { Id = 8, Name = "Vorher", ConcurrencyToken = "old" };
        var desired = new TourRecord { Id = 8, Name = "Nachher", ConcurrencyToken = "old" };
        var repository = new RecordingTourStore
        {
            ConflictOnUpsert = true,
            CurrentTour = new TourRecord { Id = 8, Name = "Nachher", ConcurrencyToken = "new" }
        };

        await TourReconciliationPersistenceService.SaveAsync(
            repository,
            [before],
            [desired],
            new TourOrderReferenceReconciliationResult(0, [8], [], []));

        Assert.Equal("new", desired.ConcurrencyToken);
    }

    [Fact]
    public async Task SaveAsync_RethrowsConcurrentWriteWhenStoredTourDiffers()
    {
        var before = new TourRecord { Id = 8, Name = "Vorher", ConcurrencyToken = "old" };
        var desired = new TourRecord { Id = 8, Name = "Unsere Änderung", ConcurrencyToken = "old" };
        var repository = new RecordingTourStore
        {
            ConflictOnUpsert = true,
            CurrentTour = new TourRecord { Id = 8, Name = "Fremde Änderung", ConcurrencyToken = "new" }
        };

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            TourReconciliationPersistenceService.SaveAsync(
                repository,
                [before],
                [desired],
                new TourOrderReferenceReconciliationResult(0, [8], [], [])));
    }

    private sealed class RecordingTourStore : ITourRecordStore, ITourRecordMutationStore
    {
        public List<int> SavedTourIds { get; } = [];
        public List<int> UpsertedTourIds { get; } = [];
        public List<(int TourId, string? Token)> DeletedTours { get; } = [];
        public bool ConflictOnUpsert { get; init; }
        public TourRecord? CurrentTour { get; init; }

        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TourRecord>>([]);

        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default)
        {
            SavedTourIds.AddRange(tours.Select(x => x.Id));
            return Task.CompletedTask;
        }

        public Task<TourRecord?> GetByIdAsync(int tourId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentTour);

        public Task UpsertAsync(TourRecord tour, CancellationToken cancellationToken = default)
        {
            UpsertedTourIds.Add(tour.Id);
            return ConflictOnUpsert
                ? Task.FromException(new ConcurrencyConflictException("Tour", tour.Id.ToString()))
                : Task.CompletedTask;
        }

        public Task DeleteAsync(int tourId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
        {
            DeletedTours.Add((tourId, concurrencyToken));
            return Task.CompletedTask;
        }
    }
}
