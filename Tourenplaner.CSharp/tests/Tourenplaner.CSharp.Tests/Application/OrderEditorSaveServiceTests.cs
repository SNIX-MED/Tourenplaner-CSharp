using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderEditorSaveServiceTests
{
    [Fact]
    public async Task ChangedDestinationMarksEveryActivePartialDeliveryTour()
    {
        var original = Order();
        original.Products[0].Quantity = 2;
        original.AssignedTourId = string.Empty;
        var secondDelivery = Copy(original.Deliveries[0]);
        secondDelivery.Id = "D2"; secondDelivery.SequenceNumber = 2; secondDelivery.AssignedTourId = "43";
        original.Deliveries.Add(secondDelivery);
        var secondTour = Tour(); secondTour.Id = 43;
        secondTour.Stops[0].DeliveryId = "D2"; secondTour.Stops[0].DeliverySequenceNumber = 2;
        var orders = new Orders(original);
        var tours = new TransactionTours(orders, Tour(), secondTour);
        var candidate = Copy(original); candidate.DeliveryAddress.HouseNumber = "99";

        var result = await OrderEditorSaveService.SaveAsync(orders, tours, original, candidate);

        Assert.True(result.RequiresAddressReview);
        Assert.All(tours.Items, tour =>
        {
            Assert.Equal(["OLD"], tour.AddressReviewOrderIds);
            Assert.False(tour.Stops[0].IsArrivalWindowFixed);
            Assert.Empty(tour.TravelTimeCache);
        });
        Assert.Equal(new[] { "42", "43" }, result.Order.Deliveries.Select(d => d.AssignedTourId));
        Assert.Equal(new[] { "D1", "D2" }, result.Order.Deliveries.Select(d => d.Id));
        Assert.Equal("nicht avisiert", result.Order.AvisoStatus);
    }

    [Fact]
    public async Task FormattingOnlyAddressChangeKeepsConfirmationAndManualPin()
    {
        var original = Order(); original.IsLocationManuallySet = true;
        var orders = new Orders(original);
        var tours = new TransactionTours(orders, Tour());
        var candidate = Copy(original); candidate.DeliveryAddress.Street = "HAUPTSTRASSE";
        var result = await OrderEditorSaveService.SaveAsync(orders, tours, original, candidate);
        Assert.False(result.RequiresAddressReview);
        Assert.Equal("Bestätigt", result.Order.AvisoStatus);
        Assert.True(result.Order.IsLocationManuallySet);
        Assert.Equal(original.Location, result.Order.Location);
        Assert.Empty(tours.Items[0].AddressReviewOrderIds);
        Assert.True(tours.Items[0].Stops[0].IsArrivalWindowFixed);
    }

    [Fact]
    public async Task DeliveredProductQuantityCannotBeChangedThroughAnyEditorEntryPoint()
    {
        var original = Order(); original.Deliveries[0].Status = OrderDeliveryStatus.Delivered;
        var orders = new Orders(original);
        var tours = new TransactionTours(orders, Tour());
        var candidate = Copy(original); candidate.Products[0].Quantity = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrderEditorSaveService.SaveAsync(orders, tours, original, candidate));
        Assert.Equal(0, tours.Commits);
        Assert.Equal(1, orders.Items[0].Products[0].Quantity);
    }

    [Fact]
    public async Task NotesEditWritesOnlyEditedOrderAndKeepsPromiseAndUnrelatedTours()
    {
        var original = Order();
        var orders = new Orders(original, new Order { Id = "OTHER", Notes = "latest remote data" });
        var tour = Tour();
        var unrelated = new TourRecord { Id = 99 }; // Not the editor's job to remove this tour.
        var tours = new TransactionTours(orders, tour, unrelated);
        var candidate = Copy(original);
        candidate.Notes = "Neue Notiz";

        var result = await OrderEditorSaveService.SaveAsync(orders, tours, original, candidate);

        Assert.Equal(["OLD"], tours.WrittenOrderIds);
        Assert.Empty(tours.WrittenTourIds);
        Assert.False(result.RequiresAddressReview);
        Assert.Equal("Bestätigt", result.Order.AvisoStatus);
        Assert.Equal("latest remote data", orders.Items.Single(o => o.Id == "OTHER").Notes);
        Assert.True(tours.Items.Single(t => t.Id == 42).Stops[0].IsArrivalWindowFixed);
        Assert.Contains(tours.Items, t => t.Id == 99);
        Assert.Empty(original.Notes);
        Assert.Equal("order-token", original.ConcurrencyToken);
    }

    [Fact]
    public async Task RenameCommitsReplacementDeletionAndAllReferencesTogether()
    {
        var original = Order();
        var orders = new Orders(original);
        var archived = Tour(); archived.Id = 43; archived.IsArchived = true;
        var tours = new TransactionTours(orders, Tour(), archived);
        var candidate = Copy(original); candidate.Id = "NEW";

        var result = await OrderEditorSaveService.SaveAsync(orders, tours, original, candidate);

        Assert.Equal("NEW", Assert.Single(orders.Items).Id);
        Assert.Equal(["OLD"], tours.DeletedOrderIds);
        Assert.Equal(2, tours.Items.Count);
        Assert.All(tours.Items, t =>
        {
            Assert.Equal("NEW", Assert.Single(t.Stops).Auftragsnummer);
            Assert.Equal("stable-stop", t.Stops[0].Id);
            Assert.Equal("D1", t.Stops[0].DeliveryId);
            Assert.True(t.Stops[0].IsArrivalWindowFixed);
        });
        Assert.Equal("Bestätigt", result.Order.AvisoStatus);
        Assert.Equal("42", result.Order.AssignedTourId);
        Assert.Equal("42", result.Order.Deliveries[0].AssignedTourId);
        Assert.Equal("OLD", original.Id);
    }

    [Fact]
    public async Task ExistingTargetNumberIsRejectedBeforeAnyWrite()
    {
        var original = Order();
        var orders = new Orders(original, new Order { Id = "NEW", Notes = "Keep me" });
        var tours = new TransactionTours(orders, Tour());
        var candidate = Copy(original); candidate.Id = "new";
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrderEditorSaveService.SaveAsync(orders, tours, original, candidate));
        Assert.Equal(0, tours.Commits);
        Assert.Equal(0, orders.Writes);
        Assert.Equal("Keep me", orders.Items[1].Notes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleEditorCannotOverwriteLatestStatusEvenWithoutVersionChange(bool changedToken)
    {
        var original = Order();
        var remote = Copy(original); remote.AvisoStatus = "Informiert";
        if (changedToken) remote.ConcurrencyToken = "new-token";
        var orders = new Orders(remote);
        var tours = new TransactionTours(orders, Tour());
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            OrderEditorSaveService.SaveAsync(orders, tours, original, Copy(original)));
        Assert.Equal(0, tours.Commits);
        Assert.Equal("Informiert", orders.Items[0].AvisoStatus);
    }

    [Fact]
    public async Task DeletedOrderIsNotRecreatedByEditor()
    {
        var orders = new Orders();
        var tours = new TransactionTours(orders, Tour());
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            OrderEditorSaveService.SaveAsync(orders, tours, Order(), Order()));
        Assert.Empty(orders.Items);
        Assert.Equal(0, tours.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedTransactionLeavesSourceAndCommittedStateUntouched(bool rename)
    {
        var original = Order();
        var orders = new Orders(original);
        var tour = Tour();
        var tours = new TransactionTours(orders, tour) { Failure = new IOException("offline") };
        var candidate = Copy(original); candidate.Notes = "Unsaved";
        if (rename) candidate.Id = "NEW";
        await Assert.ThrowsAsync<IOException>(() => OrderEditorSaveService.SaveAsync(orders, tours, original, candidate));
        Assert.Equal("OLD", Assert.Single(orders.Items).Id);
        Assert.Empty(orders.Items[0].Notes);
        Assert.Equal("OLD", tours.Items[0].Stops[0].Auftragsnummer);
        Assert.Equal("OLD", tour.Stops[0].Auftragsnummer);
        Assert.Empty(original.Notes);
    }

    [Fact]
    public async Task ChangedDestinationInvalidatesRoutingAndPromiseOnlyOnActiveToursAndBlocksDispatch()
    {
        var original = Order();
        var orders = new Orders(original);
        var archived = Tour(); archived.Id = 43; archived.IsArchived = true;
        var tours = new TransactionTours(orders, Tour(), archived);
        var candidate = Copy(original);
        candidate.DeliveryAddress.Street = "Neue Strasse";
        candidate.Location = new GeoPoint(48, 9);

        var result = await OrderEditorSaveService.SaveAsync(orders, tours, original, candidate,
            _ => throw new Exception("Must not schedule with invalidated routing"));

        Assert.True(result.RequiresAddressReview);
        Assert.Equal("nicht avisiert", result.Order.AvisoStatus);
        var active = tours.Items.Single(t => !t.IsArchived);
        Assert.Equal(["OLD"], active.AddressReviewOrderIds);
        Assert.NotEmpty(active.AddressReviewRoutingError);
        Assert.Empty(active.TravelTimeCache);
        Assert.Empty(active.TravelTimeProfileCache);
        Assert.Empty(active.RouteGeometryPoints);
        Assert.Empty(active.RouteGeometrySignature);
        Assert.False(active.Stops[0].IsArrivalWindowFixed);
        Assert.Null(active.Stops[0].ArrivalWindowStart);
        Assert.Null(active.Stops[0].ArrivalWindowEnd);
        Assert.Equal(48, active.Stops[0].Lat);
        Assert.Contains(new WebfleetTourDispatchService([result.Order]).Validate(active,
            new() { WebfleetObjectUid = "vehicle" }), error => error.Contains("Planung prüfen"));
        Assert.True(tours.Items.Single(t => t.IsArchived).Stops[0].IsArrivalWindowFixed);
        Assert.Equal("Bestätigt", original.AvisoStatus);
    }

    [Fact]
    public async Task RemovingDeliveryChannelRemovesOnlyAffectedTour()
    {
        var original = Order();
        var orders = new Orders(original);
        var tours = new TransactionTours(orders, Tour(), new TourRecord { Id = 99 });
        var candidate = Copy(original); candidate.DeliveryType = DeliveryMethodExtensions.Post;
        candidate.Type = OrderType.NonMap;
        candidate.AssignedTourId = string.Empty;
        var result = await OrderEditorSaveService.SaveAsync(orders, tours, original, candidate);
        Assert.Equal(99, Assert.Single(tours.Items).Id);
        Assert.Equal([42], tours.DeletedTourIds);
        Assert.False(result.RequiresAddressReview);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileFallbackRollsBackAfterTourWriteOrOldOrderDeleteFails(bool failDelete)
    {
        var original = Order();
        var orders = new Orders(original) { FailDeleteOnce = failDelete };
        var tours = new Tours(Tour()) { FailSaveOnce = !failDelete };
        var candidate = Copy(original); candidate.Id = "NEW";
        await Assert.ThrowsAsync<IOException>(() => OrderEditorSaveService.SaveAsync(orders, tours, original, candidate));
        Assert.Equal("OLD", Assert.Single(orders.Items).Id);
        Assert.Equal("OLD", tours.Items[0].Stops[0].Auftragsnummer);
        Assert.Empty(orders.Items[0].Notes);
        Assert.True(tours.Items[0].Stops[0].IsArrivalWindowFixed);
    }

    [Fact]
    public async Task JsonRepositoriesRenameAndReloadWithoutLosingDeliveryOrAddressData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "order-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var orders = new JsonOrderRepository(Path.Combine(directory, "orders.json"));
            var tours = new JsonToursRepository(Path.Combine(directory, "tours.json"));
            await orders.SaveAllAsync([Order()]);
            await tours.SaveAsync([Tour()]);
            var original = Assert.Single(await orders.GetAllAsync());
            var vm = new Tourenplaner.CSharp.App.Views.Dialogs.ManualOrderDialogViewModel(original);
            vm.OrderNumber = "NEW";
            Assert.True(vm.TryBuildOrder(out var candidate, out var error), error);
            await OrderEditorSaveService.SaveAsync(orders, tours, original, candidate!);
            var saved = Assert.Single(await orders.GetAllAsync());
            Assert.Equal("NEW", saved.Id);
            Assert.Equal("Bestätigt", saved.AvisoStatus);
            Assert.Equal("Rampe hinten", saved.DeliveryAddress.Additional);
            Assert.Equal("D1", Assert.Single(saved.Deliveries).Id);
            var savedTour = Assert.Single(await tours.LoadAsync());
            Assert.Equal("NEW", Assert.Single(savedTour.Stops).Auftragsnummer);
            Assert.True(savedTour.Stops[0].IsArrivalWindowFixed);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Order Order() => new()
    {
        Id = "OLD", ConcurrencyToken = "order-token", CustomerName = "Kunde", AssignedTourId = "42",
        AvisoStatus = "Bestätigt", Address = "Hauptstrasse 1, 8000 Zürich", Location = new GeoPoint(47, 8),
        DeliveryAddress = new() { Name = "Kunde", Street = "Hauptstrasse", HouseNumber = "1", PostalCode = "8000", City = "Zürich", Additional = "Rampe hinten" },
        Products = [new() { PositionId = "P1", Name = "Produkt", Quantity = 1, DeliveryStatus = "An Lager" }],
        Deliveries = [new() { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Planned, AssignedTourId = "42",
            Lines = [new() { OrderProductPositionId = "P1", Quantity = 1 }] }]
    };

    private static TourRecord Tour() => new()
    {
        Id = 42, Date = "23.10.2026", ConcurrencyToken = "tour-token",
        Stops = [new() { Id = "stable-stop", Auftragsnummer = "OLD", DeliveryId = "D1", DeliverySequenceNumber = 1,
            Name = "Kunde", Address = "Hauptstrasse 1, 8000 Zürich", Lat = 47, Lon = 8, Lng = 8,
            IsArrivalWindowFixed = true, ArrivalWindowStart = new DateTime(2026, 10, 23, 8, 0, 0),
            ArrivalWindowEnd = new DateTime(2026, 10, 23, 9, 0, 0) }],
        TravelTimeCache = new() { ["cached"] = 10 },
        TravelTimeProfileCache = new() { ["cached"] = new() { RealisticMinutes = 10 } },
        RouteGeometryPoints = [new(47, 8), new(47.1, 8.1)], RouteGeometrySignature = "old-route"
    };

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private sealed class Orders(params Order[] items) : IOrderRepository, IOrderMutationRepository
    {
        public List<Order> Items { get; set; } = Copy(items.ToList());
        public int Writes { get; private set; }
        public bool FailDeleteOnce { get; set; }
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Copy(Items));
        public Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default) => Task.FromResult(Copy(Items.FirstOrDefault(o => o.Id == orderId)));
        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
        { Writes++; Items = Copy(orders.ToList()); return Task.CompletedTask; }
        public Task UpsertAsync(Order order, CancellationToken cancellationToken = default)
        { Writes++; Items.RemoveAll(o => o.Id == order.Id); Items.Add(Copy(order)); return Task.CompletedTask; }
        public Task DeleteAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
        {
            if (FailDeleteOnce) { FailDeleteOnce = false; throw new IOException("delete failed"); }
            Writes++; Items.RemoveAll(o => o.Id == orderId); return Task.CompletedTask;
        }
    }

    private class Tours(params TourRecord[] items) : ITourRecordStore
    {
        public List<TourRecord> Items { get; set; } = Copy(items.ToList());
        public bool FailSaveOnce { get; set; }
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TourRecord>>(Copy(Items));
        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default)
        {
            if (FailSaveOnce) { FailSaveOnce = false; throw new IOException("tour write failed"); }
            Items = Copy(tours.ToList()); return Task.CompletedTask;
        }
    }

    private sealed class TransactionTours(Orders orderStore, params TourRecord[] items) : Tours(items), ITourOrderTransactionStore
    {
        public Exception? Failure { get; init; }
        public int Commits { get; private set; }
        public List<string> WrittenOrderIds { get; } = [];
        public List<int> WrittenTourIds { get; } = [];
        public List<string> DeletedOrderIds { get; } = [];
        public List<int> DeletedTourIds { get; } = [];
        public Task SaveAsync(IEnumerable<TourRecord> tours, IEnumerable<Order> orders,
            IEnumerable<TourRecord>? toursToDelete = null, IEnumerable<Order>? ordersToDelete = null, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            Commits++;
            foreach (var order in orders) { WrittenOrderIds.Add(order.Id); orderStore.Items.RemoveAll(o => o.Id == order.Id); orderStore.Items.Add(Copy(order)); }
            foreach (var tour in tours) { WrittenTourIds.Add(tour.Id); Items.RemoveAll(t => t.Id == tour.Id); Items.Add(Copy(tour)); }
            foreach (var order in ordersToDelete ?? []) { DeletedOrderIds.Add(order.Id); orderStore.Items.RemoveAll(o => o.Id == order.Id); }
            foreach (var tour in toursToDelete ?? []) { DeletedTourIds.Add(tour.Id); Items.RemoveAll(t => t.Id == tour.Id); }
            return Task.CompletedTask;
        }
    }
}
