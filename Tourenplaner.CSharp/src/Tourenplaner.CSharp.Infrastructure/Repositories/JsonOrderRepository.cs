using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonOrderRepository : JsonRepositoryBase<Order>, IOrderRepository, IOrderMutationRepository
{
    public JsonOrderRepository(string filePath) : base(filePath)
    {
    }

    public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
        => ReadListAsync(cancellationToken);

    public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
        => WriteListAsync(orders, cancellationToken);

    public async Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var normalizedId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return null;
        }

        var items = await ReadListAsync(cancellationToken);
        return items.FirstOrDefault(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        order.ConcurrencyToken = CreateConcurrencyToken();

        var items = (await ReadListAsync(cancellationToken)).ToList();
        items.RemoveAll(x => string.Equals(x.Id, order.Id, StringComparison.OrdinalIgnoreCase));
        items.Add(order);
        await WriteListAsync(items, cancellationToken);
    }

    public async Task DeleteAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
    {
        var normalizedId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        var items = (await ReadListAsync(cancellationToken)).ToList();
        items.RemoveAll(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        await WriteListAsync(items, cancellationToken);
    }

    private static string CreateConcurrencyToken()
        => DateTimeOffset.UtcNow.ToString("O");
}
