using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonAppSettingsRepository : IAppSettingsStore
{
    private readonly JsonFileStore _store;
    private readonly string _path;

    public JsonAppSettingsRepository(string path, JsonFileStore? store = null)
    {
        _path = path;
        _store = store ?? new JsonFileStore();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _store.LoadAsync(_path, () => new AppSettings(), createIfMissing: true, backupInvalid: true, cancellationToken: cancellationToken);
        if (settings.ApplyStayMinutesDefaultsMigration())
        {
            await _store.AtomicWriteAsync(_path, settings, cancellationToken);
        }

        return settings;
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        return _store.AtomicWriteAsync(_path, settings ?? new AppSettings(), cancellationToken);
    }
}
