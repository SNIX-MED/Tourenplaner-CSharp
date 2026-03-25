using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonVehicleRepository : JsonRepositoryBase<Vehicle>, IVehicleRepository
{
    private readonly JsonVehicleDataRepository _vehicleDataRepository;

    public JsonVehicleRepository(string filePath) : base(filePath)
    {
        _vehicleDataRepository = new JsonVehicleDataRepository(filePath);
    }

    public async Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var payload = await _vehicleDataRepository.LoadAsync(cancellationToken);
        return payload.Vehicles;
    }

    public async Task SaveAllAsync(IEnumerable<Vehicle> vehicles, CancellationToken cancellationToken = default)
    {
        var payload = await _vehicleDataRepository.LoadAsync(cancellationToken);
        payload.Vehicles = (vehicles ?? []).ToList();
        await _vehicleDataRepository.SaveAsync(payload, cancellationToken);
    }
}
