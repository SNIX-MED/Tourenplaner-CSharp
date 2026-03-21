using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Abstractions;

public interface IVehicleRepository
{
    Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAllAsync(IEnumerable<Vehicle> vehicles, CancellationToken cancellationToken = default);
}
