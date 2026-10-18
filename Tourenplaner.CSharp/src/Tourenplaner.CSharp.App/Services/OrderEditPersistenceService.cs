using System.Text.Json;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

/// <summary>Persist a detached edit before exposing it as the current order.</summary>
internal static class OrderEditPersistenceService
{
    public static Order CreateCandidate(Order order) =>
        JsonSerializer.Deserialize<Order>(
            JsonSerializer.SerializeToUtf8Bytes(order, JsonSerializerOptions.Default),
            JsonSerializerOptions.Default)
        ?? throw new InvalidOperationException("Auftrag konnte nicht zur Bearbeitung kopiert werden.");

    public static async Task SaveAsync(
        IOrderRepository repository,
        IReadOnlyList<Order> currentOrders,
        Order candidate,
        CancellationToken cancellationToken = default)
    {
        if (repository is IOrderMutationRepository mutationRepository)
        {
            await mutationRepository.UpsertAsync(candidate, cancellationToken);
            return;
        }

        var ordersToSave = currentOrders
            .Where(order => !string.Equals(
                order.Id,
                candidate.Id,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        ordersToSave.Add(candidate);
        await repository.SaveAllAsync(ordersToSave, cancellationToken);
    }
}
