using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface IVehicleDataStore
{
    Task<VehicleDataRecord> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(VehicleDataRecord payload, CancellationToken cancellationToken = default);
}
