using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderMaintenancePersistenceServiceTests
{
    [Fact]
    public async Task SaveChangesAsync_UpsertsOnlyChangedOrders()
    {
        var unchanged = new Order { Id = "1", CustomerName = "Gleich", ConcurrencyToken = "a" };
        var before = new Order { Id = "2", CustomerName = "Vorher", ConcurrencyToken = "b" };
        var after = new Order { Id = "2", CustomerName = "Nachher", ConcurrencyToken = "b" };
        var repository = new RecordingOrderRepository();

        await OrderMaintenancePersistenceService.SaveChangesAsync(
            repository,
            [unchanged, before],
            [unchanged, after]);

        Assert.Equal(["2"], repository.UpsertedOrderIds);
        Assert.Equal(0, repository.SaveAllCallCount);
    }

    [Fact]
    public async Task SaveChangesAsync_AcceptsIdenticalConcurrentMaintenanceResult()
    {
        var before = new Order { Id = "2", CustomerName = "Vorher", ConcurrencyToken = "old" };
        var desired = new Order { Id = "2", CustomerName = "Nachher", ConcurrencyToken = "old" };
        var repository = new RecordingOrderRepository
        {
            ConflictOnUpsert = true,
            CurrentOrder = new Order { Id = "2", CustomerName = "Nachher", ConcurrencyToken = "new" }
        };

        await OrderMaintenancePersistenceService.SaveChangesAsync(repository, [before], [desired]);

        Assert.Equal("new", desired.ConcurrencyToken);
    }

    [Fact]
    public async Task SaveChangesAsync_RethrowsDivergentConcurrentChange()
    {
        var before = new Order { Id = "2", CustomerName = "Vorher", ConcurrencyToken = "old" };
        var desired = new Order { Id = "2", CustomerName = "Unsere Änderung", ConcurrencyToken = "old" };
        var repository = new RecordingOrderRepository
        {
            ConflictOnUpsert = true,
            CurrentOrder = new Order { Id = "2", CustomerName = "Fremde Änderung", ConcurrencyToken = "new" }
        };

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            OrderMaintenancePersistenceService.SaveChangesAsync(repository, [before], [desired]));
    }

    private sealed class RecordingOrderRepository : IOrderRepository, IOrderMutationRepository
    {
        public List<string> UpsertedOrderIds { get; } = [];
        public int SaveAllCallCount { get; private set; }
        public bool ConflictOnUpsert { get; init; }
        public Order? CurrentOrder { get; init; }

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([]);

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
        {
            SaveAllCallCount++;
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentOrder);

        public Task UpsertAsync(Order order, CancellationToken cancellationToken = default)
        {
            UpsertedOrderIds.Add(order.Id);
            return ConflictOnUpsert
                ? Task.FromException(new ConcurrencyConflictException("Auftrag", order.Id))
                : Task.CompletedTask;
        }

        public Task DeleteAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
