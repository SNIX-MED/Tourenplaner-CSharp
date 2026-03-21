using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Abstractions;

public interface ITourRepository
{
    Task<IReadOnlyList<Tour>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAllAsync(IEnumerable<Tour> tours, CancellationToken cancellationToken = default);
}
