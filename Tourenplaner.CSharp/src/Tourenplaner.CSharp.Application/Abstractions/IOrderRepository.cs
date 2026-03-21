using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Abstractions;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default);
}
