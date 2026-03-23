using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonVehicleDataRepository
{
    private readonly JsonFileStore _store;
    private readonly string _path;

    public JsonVehicleDataRepository(string path, JsonFileStore? store = null)
    {
        _path = path;
        _store = store ?? new JsonFileStore();
    }

    public async Task<VehicleDataRecord> LoadAsync(CancellationToken cancellationToken = default)
    {
        var payload = await _store.LoadAsync(_path, () => new VehicleDataRecord(), createIfMissing: true, backupInvalid: true, cancellationToken: cancellationToken);
        var normalized = VehicleNormalizer.NormalizePayload(payload);
        return normalized;
    }

    public Task SaveAsync(VehicleDataRecord payload, CancellationToken cancellationToken = default)
    {
        var normalized = VehicleNormalizer.NormalizePayload(payload ?? new VehicleDataRecord());
        return _store.AtomicWriteAsync(_path, normalized, cancellationToken);
    }
}
