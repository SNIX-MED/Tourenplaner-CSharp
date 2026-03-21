using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonTourRepository : JsonRepositoryBase<Tour>, ITourRepository
{
    public JsonTourRepository(string filePath) : base(filePath)
    {
    }

    public Task<IReadOnlyList<Tour>> GetAllAsync(CancellationToken cancellationToken = default)
        => ReadListAsync(cancellationToken);

    public Task SaveAllAsync(IEnumerable<Tour> tours, CancellationToken cancellationToken = default)
        => WriteListAsync(tours, cancellationToken);
}
