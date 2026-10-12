using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonVehicleDataRepository : IVehicleDataStore
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
        VehicleDataRecord payload;
        try
        {
            payload = await _store.LoadAsync(
                _path,
                () => new VehicleDataRecord(),
                createIfMissing: true,
                backupInvalid: false,
                cancellationToken: cancellationToken);
        }
        catch (InvalidJsonFileException currentFormatException)
        {
            try
            {
                var legacyVehicles = await _store.LoadAsync(
                    _path,
                    () => new List<Vehicle>(),
                    cancellationToken: cancellationToken);
                payload = new VehicleDataRecord { Vehicles = legacyVehicles };

                // Older releases stored vehicles.json as a plain vehicle array.
                // Rewrite it once in the current envelope format so subsequent
                // reads also preserve trailers and vehicle combinations.
                await SaveAsync(payload, cancellationToken);
            }
            catch (InvalidJsonFileException)
            {
                _store.BackupCorruptFile(_path);
                throw currentFormatException;
            }
        }

        var normalized = VehicleNormalizer.NormalizePayload(payload);
        return normalized;
    }

    public Task SaveAsync(VehicleDataRecord payload, CancellationToken cancellationToken = default)
    {
        var normalized = VehicleNormalizer.NormalizePayload(payload ?? new VehicleDataRecord());
        return _store.AtomicWriteAsync(_path, normalized, cancellationToken);
    }
}
