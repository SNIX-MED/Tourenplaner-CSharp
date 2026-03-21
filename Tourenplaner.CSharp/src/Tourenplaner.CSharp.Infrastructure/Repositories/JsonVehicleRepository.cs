using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonVehicleRepository : JsonRepositoryBase<Vehicle>, IVehicleRepository
{
    public JsonVehicleRepository(string filePath) : base(filePath)
    {
    }

    public Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default)
        => ReadListAsync(cancellationToken);

    public Task SaveAllAsync(IEnumerable<Vehicle> vehicles, CancellationToken cancellationToken = default)
        => WriteListAsync(vehicles, cancellationToken);
}
