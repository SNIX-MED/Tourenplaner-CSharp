using System.Text.Json;
using System.Text.Json.Nodes;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

internal static class OrderMaintenancePersistenceService
{
    private static readonly SemaphoreSlim PersistenceGate = new(1, 1);

    public static IReadOnlyList<Order> CreateSnapshot(IEnumerable<Order> orders) =>
        (orders ?? [])
            .Where(x => x is not null)
            .Select(x => JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(x)) ?? new Order { Id = x.Id })
            .ToList();

    public static async Task SaveChangesAsync(
        IOrderRepository repository,
        IReadOnlyList<Order> originalOrders,
        IReadOnlyList<Order> currentOrders,
        CancellationToken cancellationToken = default)
    {
        if (repository is not IOrderMutationRepository mutationRepository)
        {
            await repository.SaveAllAsync(currentOrders, cancellationToken);
            return;
        }

        var originalById = originalOrders
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        await PersistenceGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var order in currentOrders.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Id)).ToList())
            {
                if (originalById.TryGetValue(order.Id.Trim(), out var original) && AreEquivalent(original, order))
                {
                    continue;
                }

                try
                {
                    await mutationRepository.UpsertAsync(order, cancellationToken);
                }
                catch (ConcurrencyConflictException)
                {
                    var current = await mutationRepository.GetByIdAsync(order.Id, cancellationToken);
                    if (current is null)
                    {
                        // A concurrently deleted order must not be recreated by maintenance.
                        if (currentOrders is ICollection<Order> mutableOrders && !mutableOrders.IsReadOnly)
                            mutableOrders.Remove(order);
                        else
                            order.IsArchived = true;
                        continue;
                    }
                    // Maintenance is best effort. Keep the latest server state instead
                    // of overwriting a concurrent edit or aborting the map refresh.
                    foreach (var property in typeof(Order).GetProperties()
                                 .Where(p => p.CanRead && p.SetMethod?.IsPublic == true))
                        property.SetValue(order, property.GetValue(current));
                }
            }
        }
        finally
        {
            PersistenceGate.Release();
        }
    }

    private static bool AreEquivalent(Order left, Order right) =>
        string.Equals(SerializeWithoutConcurrencyToken(left), SerializeWithoutConcurrencyToken(right), StringComparison.Ordinal);

    private static string SerializeWithoutConcurrencyToken(Order order)
    {
        var node = JsonSerializer.SerializeToNode(order) as JsonObject;
        node?.Remove(nameof(Order.ConcurrencyToken));
        return node?.ToJsonString() ?? string.Empty;
    }
}
