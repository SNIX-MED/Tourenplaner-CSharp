using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonToursRepository : ITourRecordStore, ITourRecordMutationStore
{
    private readonly JsonFileStore _store;
    private readonly string _path;

    public JsonToursRepository(string path, JsonFileStore? store = null)
    {
        _path = path;
        _store = store ?? new JsonFileStore();
    }

    public async Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var payload = await _store.LoadAsync(_path, () => new List<TourRecord>(), createIfMissing: true, backupInvalid: true, cancellationToken: cancellationToken);
        var normalized = payload
            .Where(x => x is not null)
            .Select(TourNormalizer.NormalizeTour)
            .ToList();
        return normalized;
    }

    public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default)
    {
        var normalized = (tours ?? Array.Empty<TourRecord>())
            .Where(x => x is not null)
            .Select(TourNormalizer.NormalizeTour)
            .ToList();

        return _store.AtomicWriteAsync(_path, normalized, cancellationToken);
    }

    public async Task<TourRecord?> GetByIdAsync(int tourId, CancellationToken cancellationToken = default)
    {
        var items = await LoadAsync(cancellationToken);
        return items.FirstOrDefault(x => x.Id == tourId);
    }

    public async Task UpsertAsync(TourRecord tour, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tour);
        tour.ConcurrencyToken = CreateConcurrencyToken();

        var items = (await LoadAsync(cancellationToken)).ToList();
        items.RemoveAll(x => x.Id == tour.Id);
        items.Add(TourNormalizer.NormalizeTour(tour));
        await SaveAsync(items, cancellationToken);
    }

    public async Task DeleteAsync(int tourId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
    {
        var items = (await LoadAsync(cancellationToken)).ToList();
        items.RemoveAll(x => x.Id == tourId);
        await SaveAsync(items, cancellationToken);
    }

    private static string CreateConcurrencyToken()
        => DateTimeOffset.UtcNow.ToString("O");
}
