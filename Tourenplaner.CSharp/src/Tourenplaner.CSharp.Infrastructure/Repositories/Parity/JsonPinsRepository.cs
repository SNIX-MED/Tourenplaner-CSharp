using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonPinsRepository
{
    private readonly JsonFileStore _store;
    private readonly string _path;

    public JsonPinsRepository(string path, JsonFileStore? store = null)
    {
        _path = path;
        _store = store ?? new JsonFileStore();
    }

    public async Task<IReadOnlyList<PinRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var payload = await _store.LoadAsync(_path, () => new List<PinRecord>(), backupInvalid: true, cancellationToken: cancellationToken);
        return payload
            .Where(x => x is not null)
            .Where(x => x.Data is not null)
            .ToList();
    }

    public Task SaveAsync(IEnumerable<PinRecord> pins, CancellationToken cancellationToken = default)
    {
        var payload = (pins ?? Array.Empty<PinRecord>())
            .Where(x => x is not null)
            .Where(x => x.Data is not null)
            .ToList();

        return _store.AtomicWriteAsync(_path, payload, cancellationToken);
    }
}
