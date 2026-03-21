using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonToursRepository
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

        await _store.AtomicWriteAsync(_path, normalized, cancellationToken);
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
}
