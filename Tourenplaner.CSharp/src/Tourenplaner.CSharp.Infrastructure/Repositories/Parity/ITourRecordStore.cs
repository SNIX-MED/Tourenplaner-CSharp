using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface ITourRecordStore
{
    Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default);
}
