using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderEditPersistenceServiceTests
{
    [Fact]
    public async Task SingleEdit_DoesNotSaveUnrelatedStaleOrders()
    {
        var original = CreateOrder();
        var unrelated = new Order { Id = "OTHER", ConcurrencyToken = "stale" };
        var candidate = OrderEditPersistenceService.CreateCandidate(original);
        candidate.Products[0].DeliveryStatus = OrderProductInfo.InStockStatus;
        var repository = new RecordingRepository();

        await OrderEditPersistenceService.SaveAsync(repository, [original, unrelated], candidate);

        Assert.Equal(new[] { "221972" }, repository.SavedIds);
        Assert.Equal(OrderProductInfo.InTransitStatus, original.Products[0].DeliveryStatus);
        Assert.Equal(OrderProductInfo.InStockStatus, candidate.Products[0].DeliveryStatus);
        Assert.Equal(0, repository.FullSaves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSave_DoesNotChangeOriginalProductStatus(bool conflict)
    {
        var original = CreateOrder();
        var candidate = OrderEditPersistenceService.CreateCandidate(original);
        candidate.Products[0].DeliveryStatus = OrderProductInfo.InStockStatus;
        var repository = new RecordingRepository
        {
            Failure = conflict ? new ConcurrencyConflictException("Auftrag", original.Id) : new IOException("Verbindung unterbrochen")
        };

        await Assert.ThrowsAnyAsync<Exception>(() => OrderEditPersistenceService.SaveAsync(repository, [original], candidate));

        Assert.Equal(OrderProductInfo.InTransitStatus, original.Products[0].DeliveryStatus);
        Assert.Equal("original-token", original.ConcurrencyToken);
    }

    [Fact]
    public void CreateCandidate_DeepClonesNestedEditableState()
    {
        var original = CreateOrder();
        original.OrderAddress = new OrderAddressInfo { Street = "Originalstrasse", HouseNumber = "1" };
        original.DeliveryAddress = new DeliveryAddressInfo { Street = "Lieferstrasse", HouseNumber = "2" };
        var candidate = OrderEditPersistenceService.CreateCandidate(original);

        candidate.OrderAddress.Street = "Geänderte Auftragsadresse";
        candidate.DeliveryAddress.Street = "Geänderte Lieferadresse";
        candidate.Products[0].Name = "Geändertes Produkt";

        Assert.NotSame(original, candidate);
        Assert.NotSame(original.OrderAddress, candidate.OrderAddress);
        Assert.NotSame(original.DeliveryAddress, candidate.DeliveryAddress);
        Assert.NotSame(original.Products, candidate.Products);
        Assert.NotSame(original.Products[0], candidate.Products[0]);
        Assert.Equal("Originalstrasse", original.OrderAddress.Street);
        Assert.Equal("Lieferstrasse", original.DeliveryAddress.Street);
        Assert.Equal("Produkt", original.Products[0].Name);
        Assert.Equal(original.Id, candidate.Id);
        Assert.Equal(original.ConcurrencyToken, candidate.ConcurrencyToken);
    }

    [Fact]
    public async Task FullSaveFallback_UsesStableSnapshotAndReplacesCandidateCaseInsensitively()
    {
        var original = CreateOrder();
        var unrelated = new Order { Id = "OTHER" };
        var currentOrders = new List<Order> { original, unrelated };
        var candidate = OrderEditPersistenceService.CreateCandidate(original);
        candidate.Id = original.Id.ToLowerInvariant();
        var repository = new DeferredEnumerationRepository();

        await OrderEditPersistenceService.SaveAsync(repository, currentOrders, candidate);
        currentOrders.Clear();
        currentOrders.Add(new Order { Id = "LATE-MUTATION" });

        Assert.Equal(["OTHER", "221972"], repository.CapturedOrders.Select(order => order.Id).ToArray());
        Assert.Same(candidate, repository.CapturedOrders[1]);
    }

    private static Order CreateOrder() => new()
    {
        Id = "221972", ConcurrencyToken = "original-token",
        Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", DeliveryStatus = OrderProductInfo.InTransitStatus }]
    };

    private sealed class RecordingRepository : IOrderRepository, IOrderMutationRepository
    {
        public Exception? Failure { get; init; }
        public List<string> SavedIds { get; } = [];
        public int FullSaves { get; private set; }
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
        {
            FullSaves++;
            throw new InvalidOperationException("An unrelated stale row must not be saved.");
        }
        public Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task UpsertAsync(Order order, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) return Task.FromException(Failure);
            SavedIds.Add(order.Id);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DeferredEnumerationRepository : IOrderRepository
    {
        private IEnumerable<Order> _capturedOrders = [];

        public IReadOnlyList<Order> CapturedOrders => _capturedOrders.ToList();

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([]);

        public Task SaveAllAsync(
            IEnumerable<Order> orders,
            CancellationToken cancellationToken = default)
        {
            _capturedOrders = orders;
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(
            string orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Order?>(null);
    }
}
