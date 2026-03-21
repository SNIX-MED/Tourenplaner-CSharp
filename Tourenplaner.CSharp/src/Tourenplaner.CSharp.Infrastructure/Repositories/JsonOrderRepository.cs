using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonOrderRepository : JsonRepositoryBase<Order>, IOrderRepository
{
    public JsonOrderRepository(string filePath) : base(filePath)
    {
    }

    public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
        => ReadListAsync(cancellationToken);

    public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
        => WriteListAsync(orders, cancellationToken);
}
