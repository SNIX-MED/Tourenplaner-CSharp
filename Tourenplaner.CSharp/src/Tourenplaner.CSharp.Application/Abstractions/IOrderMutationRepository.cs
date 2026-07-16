using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Abstractions;

public interface IOrderMutationRepository
{
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task UpsertAsync(Order order, CancellationToken cancellationToken = default);
    Task DeleteAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default);
}
