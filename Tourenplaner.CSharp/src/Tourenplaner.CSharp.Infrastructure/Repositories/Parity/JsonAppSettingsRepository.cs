using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonAppSettingsRepository
{
    private readonly JsonFileStore _store;
    private readonly string _path;

    public JsonAppSettingsRepository(string path, JsonFileStore? store = null)
    {
        _path = path;
        _store = store ?? new JsonFileStore();
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadAsync(_path, () => new AppSettings(), createIfMissing: true, backupInvalid: true, cancellationToken: cancellationToken);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        return _store.AtomicWriteAsync(_path, settings ?? new AppSettings(), cancellationToken);
    }
}
