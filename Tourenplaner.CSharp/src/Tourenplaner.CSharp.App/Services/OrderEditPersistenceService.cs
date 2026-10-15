using System.Text.Json;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

/// <summary>Persist a detached edit before exposing it as the current order.</summary>
internal static class OrderEditPersistenceService
{
    public static Order CreateCandidate(Order order) =>
        JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(order))
        ?? throw new InvalidOperationException("Auftrag konnte nicht zur Bearbeitung kopiert werden.");

    public static async Task SaveAsync(IOrderRepository repository, IReadOnlyList<Order> currentOrders,
        Order candidate, CancellationToken cancellationToken = default)
    {
        if (repository is IOrderMutationRepository mutationRepository)
            await mutationRepository.UpsertAsync(candidate, cancellationToken);
        else
            await repository.SaveAllAsync(currentOrders.Where(x => !string.Equals(x.Id, candidate.Id,
                StringComparison.OrdinalIgnoreCase)).Append(candidate), cancellationToken);
    }
}
