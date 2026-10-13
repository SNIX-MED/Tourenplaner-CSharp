using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface ITourOrderTransactionStore
{
    Task SaveAsync(
        IEnumerable<TourRecord> tours,
        IEnumerable<Order> orders,
        IEnumerable<TourRecord>? toursToDelete = null,
        IEnumerable<Order>? ordersToDelete = null,
        CancellationToken cancellationToken = default);
}
