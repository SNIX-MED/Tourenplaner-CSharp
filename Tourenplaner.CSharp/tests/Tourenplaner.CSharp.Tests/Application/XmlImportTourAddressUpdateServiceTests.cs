using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class XmlImportTourAddressUpdateServiceTests
{
    private static TourRecord Tour(int id, string orderId = "A-1") => new()
    {
        Id = id, Date = "26.10.2026", Name = "Tour " + id, Stops =
        [new() { Id = "manual:start", StopKind = "manual", Order = 1, Lat = 47, Lng = 8 },
            new() { Id = "auftrag:" + orderId, Auftragsnummer = orderId, Order = 2, Lat = 47.1, Lng = 8.1 }]
    };

    private static readonly ImportTourAddressChange[] Changes = [new("A-1", 7, "Tour 7", "26.10.2026", "Alt", "Neu")];

    [Fact]
    public async Task Update_LeavesUnrelatedTourUntouched_EvenIfItHasADanglingOrder()
    {
        var unrelated = Tour(8, "missing");
        unrelated.TravelTimeCache["manual:start|auftrag:missing"] = 44;
        var before = JsonSerializer.Serialize(unrelated);
        var tours = new TourStore([Tour(7), unrelated]);
        await XmlImportTourAddressUpdateService.UpdateAsync(Changes, false, tours, new OrderStore(), new SettingsStore(), null, "test");
        Assert.Equal(before, JsonSerializer.Serialize(tours.Items.Single(t => t.Id == 8)));
        Assert.Equal(new[] { 7 }, tours.UpsertIds);
        Assert.Single(tours.Items.Single(t => t.Id == 7).AddressReviewOrderIds);
    }

    [Fact]
    public async Task Update_MarksCurrentAssignment_IfOrderWasMovedDuringGeocoding()
    {
        var tours = new TourStore([Tour(8)]);
        await XmlImportTourAddressUpdateService.UpdateAsync(Changes, true, tours, new OrderStore(), new SettingsStore(), null, "test", Route);
        var stored = Assert.Single(tours.Items);
        Assert.Equal(8, stored.Id);
        Assert.Single(stored.AddressReviewOrderIds);
        Assert.Empty(stored.AddressReviewRoutingError);
        Assert.Equal(20, stored.TravelTimeCache["manual:start|auftrag:A-1"]);
    }

    [Fact]
    public async Task Update_ReloadsOrderChangedDuringRouting_AndSavesOnlyCurrentCoordinates()
    {
        var tours = new TourStore([Tour(7)]);
        var orders = new OrderStore();
        var routeCalls = 0;
        await XmlImportTourAddressUpdateService.UpdateAsync(Changes, true, tours, orders, new SettingsStore(), null, "test", points =>
        {
            if (++routeCalls == 1) orders.Current.Location = new(48, 9);
            return Route(points);
        });
        Assert.Equal(2, routeCalls);
        var stored = Assert.Single(tours.Items);
        Assert.Equal(48, stored.Stops[1].Lat);
        Assert.Equal(new GeoPoint(48, 9), stored.RouteGeometryPoints[^1]);
        Assert.Single(tours.UpsertIds);
    }

    [Fact]
    public async Task Update_RetriesTransientTourConflict_WithoutLosingReviewMarker()
    {
        var tours = new TourStore([Tour(7)]) { FailNextUpsert = true };
        await XmlImportTourAddressUpdateService.UpdateAsync(Changes, false, tours, new OrderStore(), new SettingsStore(), null, "test");
        Assert.Single(Assert.Single(tours.Items).AddressReviewOrderIds);
        Assert.Equal(2, tours.UpsertAttempts);
    }

    [Fact]
    public async Task Update_StopsAfterRepeatedOrderChanges_AndKeepsPersistedReviewMarker()
    {
        var tours = new TourStore([Tour(7)]);
        var orders = new OrderStore();
        await XmlImportTourAddressUpdateService.UpdateAsync(Changes, false, tours, orders, new SettingsStore(), null, "test");
        var routeCalls = 0;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => XmlImportTourAddressUpdateService.UpdateAsync(
            Changes, true, tours, orders, new SettingsStore(), null, "test", points =>
            {
                orders.Current.Location = new GeoPoint(48 + ++routeCalls * 0.01, 9);
                return Route(points);
            }));
        Assert.Equal(3, routeCalls);
        Assert.Single(Assert.Single(tours.Items).AddressReviewOrderIds);
        Assert.Empty(Assert.Single(tours.Items).RouteGeometryPoints);
    }

    private static Task<OsrmRouteResult> Route(IReadOnlyList<GeoPoint> points) =>
        Task.FromResult(new OsrmRouteResult(points, [new OsrmRouteLeg(20, 40)]));

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private sealed class TourStore(List<TourRecord> items) : ITourRecordStore, ITourRecordMutationStore
    {
        public List<TourRecord> Items = items;
        public List<int> UpsertIds = [];
        public bool FailNextUpsert;
        public int UpsertAttempts;
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TourRecord>>(Clone(Items));
        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) => throw new Exception("Must use targeted mutations");
        public Task<TourRecord?> GetByIdAsync(int tourId, CancellationToken cancellationToken = default) => Task.FromResult(Clone(Items.FirstOrDefault(t => t.Id == tourId)));
        public Task UpsertAsync(TourRecord tour, CancellationToken cancellationToken = default)
        {
            UpsertAttempts++;
            if (FailNextUpsert) { FailNextUpsert = false; throw new ConcurrencyConflictException("Tour", tour.Id.ToString()); }
            Items.RemoveAll(t => t.Id == tour.Id);
            Items.Add(Clone(tour));
            UpsertIds.Add(tour.Id);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(int tourId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(t => t.Id == tourId);
            return Task.CompletedTask;
        }
    }

    private sealed class OrderStore : IOrderRepository
    {
        public Order Current = new() { Id = "A-1", Location = new(47.5, 8.5),
            DeliveryAddress = new() { Street = "Neu", HouseNumber = "1", PostalCode = "8000", City = "Zürich" } };
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([Clone(Current)]);
        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SettingsStore : IAppSettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
