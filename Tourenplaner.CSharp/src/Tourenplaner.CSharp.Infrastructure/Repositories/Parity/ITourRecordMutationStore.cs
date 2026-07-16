using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface ITourRecordMutationStore
{
    Task<TourRecord?> GetByIdAsync(int tourId, CancellationToken cancellationToken = default);
    Task UpsertAsync(TourRecord tour, CancellationToken cancellationToken = default);
    Task DeleteAsync(int tourId, string? concurrencyToken = null, CancellationToken cancellationToken = default);
}
