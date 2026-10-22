using System.Reflection;
using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Fact]
    public async Task BatchedRenameChainPreservesRouteStopAndSelectedDelivery()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        vm.SelectedOrder = vm.ResolveRoutePinInfoCard(vm.RouteStops[1]);
        vm.SelectedRouteStop = vm.RouteStops[1];
        var stop = vm.RouteStops[1];
        orders.Items[0].Id = "FINAL";
        // A delayed tour notification can arrive after the order notifications. The order
        // snapshot already contains the final ID while the displayed tour still has A.
        var source = Guid.NewGuid();
        var first = vm.RefreshAfterRemoteChangeAsync(new(source, AppDataKind.Orders, "A", "MIDDLE"));
        await vm.RefreshAfterRemoteChangeAsync(new(source, AppDataKind.Orders, "MIDDLE", "FINAL"));
        await first;
        Assert.Contains(stop, vm.RouteStops);
        Assert.Equal("FINAL", stop.OrderId);
        Assert.Equal("FINAL", vm.SelectedOrder!.OrderId);
        Assert.Equal("DA", vm.SelectedOrder.DeliveryId);
        Assert.Same(stop, vm.SelectedRouteStop);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingTourDuringRemoteReadCannotReopenPreviousTour(bool reconnect)
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        orders.DelayedRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        orders.ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var kinds = AppDataKind.Orders | AppDataKind.Tours;
        if (reconnect) kinds |= AppDataKind.Settings | AppDataKind.Employees | AppDataKind.Vehicles;
        var pending = vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), kinds));
        await orders.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // This is the same tour-loading path used by selecting another saved tour.
        var load = typeof(KarteSectionViewModel).GetMethod("LoadTourIntoRouteAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)load.Invoke(vm, [43, true])!;
        var stops = vm.RouteStops.ToArray();
        orders.DelayedRead.SetResult(CloneRemote(orders.Items));
        await pending;
        Assert.Equal(43, vm.LoadedTourId);
        Assert.Equal("Tour 43", vm.RouteName);
        Assert.Equal(stops, vm.RouteStops);
        Assert.True(GetPrivateField<bool>(vm, "_hasDeferredRemoteRefresh"));
    }

    [Fact]
    public async Task RemoteAvisoStatusUpdatesPinsAndDetailsWithoutRecalculatingOrSaving()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        vm.SelectedOrder = vm.ResolveRoutePinInfoCard(vm.RouteStops[1]);
        var revision = GetPrivateField<int>(vm, "_routeGeometryRevision");
        orders.Items[0].AvisoStatus = "Informiert";
        orders.Writes = tours.Writes = 0;
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Orders, "A", "A"));
        Assert.Equal("Informiert", vm.DetailSelectedAvisoStatus, ignoreCase: true);
        Assert.Equal("Informiert", vm.SelectedOrder!.AvisoStatusLabel, ignoreCase: true);
        Assert.Equal("Informiert", vm.RouteStops[1].AvisoStatus, ignoreCase: true);
        Assert.Equal(revision, GetPrivateField<int>(vm, "_routeGeometryRevision"));
        Assert.Equal(0, orders.Writes + tours.Writes);
    }

    [Fact]
    public async Task RemoteRenameKeepsSelectedDeliveryAndCanonicalTourReference()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        vm.SelectedOrder = vm.ResolveRoutePinInfoCard(vm.RouteStops[1]);
        vm.SelectedRouteStop = vm.RouteStops[1];
        orders.Items[0].Id = "NEW";
        tours.Items[0].Stops[1].Auftragsnummer = "NEW";
        var source = Guid.NewGuid();
        var rename = vm.RefreshAfterRemoteChangeAsync(new(source, AppDataKind.Orders, "A", "NEW"));
        await vm.RefreshAfterRemoteChangeAsync(new(source, AppDataKind.Tours, "42", "42"));
        await rename;
        Assert.Equal("NEW", vm.SelectedOrder!.OrderId);
        Assert.Equal("DA", vm.SelectedOrder.DeliveryId);
        Assert.Equal("NEW", vm.SelectedRouteStop!.OrderId);
        Assert.Equal("DA", vm.SelectedRouteStop.DeliveryId);
    }

    [Fact]
    public async Task ChangedDestinationStillRecalculatesTheAffectedRoute()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        var revision = GetPrivateField<int>(vm, "_routeGeometryRevision");
        orders.Items[0].Location = new(48, 9);
        tours.Items[0].Stops[1].Lat = 48;
        tours.Items[0].Stops[1].Lng = tours.Items[0].Stops[1].Lon = 9;
        tours.Items[0].RouteGeometryPoints.Clear();
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Orders | AppDataKind.Tours));
        Assert.Equal(48, vm.RouteStops[1].Latitude);
        Assert.Equal(9, vm.RouteStops[1].Longitude);
        Assert.True(GetPrivateField<int>(vm, "_routeGeometryRevision") > revision);
    }

    [Fact]
    public async Task ExplicitMaintenanceIsIdempotentAndFullReconnectDoesNotWriteBack()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        orders.Writes = tours.Writes = 0;
        await vm.RefreshAsync();
        Assert.Equal(0, orders.Writes + tours.Writes);
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.Empty, AppDataKind.Orders | AppDataKind.Tours |
            AppDataKind.Employees | AppDataKind.Vehicles | AppDataKind.Settings));
        Assert.Equal(0, orders.Writes + tours.Writes);
        Assert.Equal("07:30", vm.RouteStartHour + ":" + vm.RouteStartMinute);
        Assert.NotEmpty(vm.RouteStops[1].EtaText);
    }

    [Fact]
    public async Task RealSyncEventIsProcessedOnceAndUnrelatedTourKeepsAllDisplayedTimes()
    {
        var (orders, tours) = RemoteStores();
        var sync = new AppDataSyncService();
        var vm = CreateViewModel(orders, tours, syncService: sync);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        var stops = vm.RouteStops.ToArray();
        var times = stops.Select(s => (s.EtaText, s.EtaRangeText, s.NextLegDepartureText, s.NextLegDurationText)).ToArray();
        var revision = GetPrivateField<int>(vm, "_routeGeometryRevision");
        orders.Reads = tours.Reads = orders.Writes = tours.Writes = 0;
        var source = Guid.NewGuid();
        tours.Items[1].Name = "Andere Tour gespeichert";
        for (var i = 0; i < 30; i++)
        {
            sync.PublishOrders(source, "B", "B");
            sync.PublishTours(source, "43", "43");
        }
        await vm.RefreshAfterRemoteChangeAsync(new(source, AppDataKind.Tours, "43", "43"));
        Assert.Equal(1, orders.Reads);
        Assert.Equal(1, tours.Reads);
        Assert.Equal(0, orders.Writes + tours.Writes);
        Assert.Equal(stops, vm.RouteStops);
        Assert.Equal(times, vm.RouteStops.Select(s => (s.EtaText, s.EtaRangeText, s.NextLegDepartureText, s.NextLegDurationText)));
        Assert.Equal(revision, GetPrivateField<int>(vm, "_routeGeometryRevision"));
        Assert.Contains(vm.TourOverviewItems, t => t.TourId == 43 && t.TourName.Contains("Andere Tour"));
    }

    [Fact]
    public async Task TwoEditorsOfDifferentToursKeepDraftsTokensAndWindowsAcrossOtherPersonsSave()
    {
        var (orders, tours) = RemoteStores();
        var sync = new AppDataSyncService();
        var first = CreateViewModel(orders, tours, syncService: sync);
        var second = CreateViewModel(orders, tours, syncService: sync);
        await first.RefreshAsync(); await second.RefreshAsync();
        await first.FocusTourAsync(42); await second.FocusTourAsync(43);
        try
        {
            await InvokeRemoteTestAsync(first, "StartSelectedTourEditingAsync");
            await InvokeRemoteTestAsync(second, "StartSelectedTourEditingAsync");
            Assert.True(first.IsLoadedTourEditing); Assert.True(second.IsLoadedTourEditing);
            first.RouteStops[1].PlannedStayMinutes = 37;
            second.RouteStops[1].PlannedStayMinutes = 29;
            var firstStops = first.RouteStops.ToArray(); var secondStops = second.RouteStops.ToArray();
            var firstToken = GetPrivateField<string>(first, "_loadedTourEditConcurrencyToken");
            var secondToken = GetPrivateField<string>(second, "_loadedTourEditConcurrencyToken");
            var firstRevision = GetPrivateField<int>(first, "_routeGeometryRevision");
            var secondRevision = GetPrivateField<int>(second, "_routeGeometryRevision");
            orders.Reads = tours.Reads = orders.Writes = tours.Writes = 0;
            tours.Items[0].ConcurrencyToken = "new-server-version";
            var change = new AppDataChangedEventArgs(Guid.NewGuid(), AppDataKind.Orders | AppDataKind.Tours | AppDataKind.Settings);
            sync.Publish(change);
            await Task.WhenAll(first.RefreshAfterRemoteChangeAsync(change), second.RefreshAfterRemoteChangeAsync(change));
            Assert.Equal(0, orders.Reads + tours.Reads + orders.Writes + tours.Writes);
            Assert.Equal(firstStops, first.RouteStops); Assert.Equal(secondStops, second.RouteStops);
            Assert.Equal(37, first.RouteStops[1].PlannedStayMinutes);
            Assert.Equal(29, second.RouteStops[1].PlannedStayMinutes);
            Assert.Equal(firstToken, GetPrivateField<string>(first, "_loadedTourEditConcurrencyToken"));
            Assert.Equal(secondToken, GetPrivateField<string>(second, "_loadedTourEditConcurrencyToken"));
            Assert.Equal(firstRevision, GetPrivateField<int>(first, "_routeGeometryRevision"));
            Assert.Equal(secondRevision, GetPrivateField<int>(second, "_routeGeometryRevision"));
            Assert.True(GetPrivateField<bool>(first, "_hasDeferredRemoteRefresh"));
            Assert.True(GetPrivateField<bool>(second, "_hasDeferredRemoteRefresh"));
            Assert.Throws<ConcurrencyConflictException>(() => first.EnsureTourEditVersionIsCurrent(tours.Items[0]));
        }
        finally
        {
            ReleaseRemoteTestLeases(first); ReleaseRemoteTestLeases(second);
        }
    }

    [Fact]
    public async Task DeferredChangesAreLoadedAfterEditingAndDoNotWriteBack()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        try
        {
            await InvokeRemoteTestAsync(vm, "StartSelectedTourEditingAsync");
            tours.Items[0].Name = "Externe Änderung";
            await vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Tours));
            Assert.NotEqual("Externe Änderung", vm.RouteName);
        }
        finally { ReleaseRemoteTestLeases(vm); }
        SetPrivateField(vm, "_hasUnsavedRouteChanges", false);
        orders.Writes = tours.Writes = 0;
        await InvokeRemoteTestAsync(vm, "RefreshDeferredRemoteChangesAsync");
        Assert.Equal("Externe Änderung", vm.RouteName);
        Assert.False(GetPrivateField<bool>(vm, "_hasDeferredRemoteRefresh"));
        Assert.Equal(0, orders.Writes + tours.Writes);
    }

    [Fact]
    public async Task SavedTimeChangeRendersNewScheduleWithoutAnotherRoutingCalculation()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        var revision = GetPrivateField<int>(vm, "_routeGeometryRevision");
        tours.Items[0].Stops[1].PlannedArrival = "10:17";
        tours.Items[0].Stops[1].PlannedArrivalOptimistic = "10:00";
        tours.Items[0].Stops[1].PlannedArrivalPessimistic = "10:30";
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Tours, "42", "42"));
        Assert.Equal("10:17", vm.RouteStops[1].EtaText);
        Assert.Equal("10:00 - 10:30", vm.RouteStops[1].EtaRangeText);
        Assert.Equal(revision, GetPrivateField<int>(vm, "_routeGeometryRevision"));
    }

    [Fact]
    public async Task FailedReadKeepsSnapshotAndNextNotificationRecovers()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        var stops = vm.RouteStops.ToArray();
        orders.FailReads = true;
        await Assert.ThrowsAsync<IOException>(() => vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Orders)));
        Assert.Equal(stops, vm.RouteStops);
        Assert.True(GetPrivateField<bool>(vm, "_hasDeferredRemoteRefresh"));
        orders.FailReads = false;
        tours.Items[0].Name = "Nach Wiederverbindung";
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.Empty, AppDataKind.Orders | AppDataKind.Tours));
        Assert.Equal("Nach Wiederverbindung", vm.RouteName);
        Assert.False(GetPrivateField<bool>(vm, "_hasDeferredRemoteRefresh"));
    }

    [Fact]
    public async Task DraftStartedDuringDatabaseReadIsNotReplaced()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        orders.DelayedRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        orders.ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Orders));
        await orders.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.RouteStops.Add(new RouteStopItem { OrderId = "LOCAL", PlannedStayMinutes = 31 });
        SetPrivateField(vm, "_hasUnsavedRouteChanges", true);
        orders.DelayedRead.SetResult(CloneRemote(orders.Items));
        await pending;
        Assert.Contains(vm.RouteStops, s => s.OrderId == "LOCAL" && s.PlannedStayMinutes == 31);
        Assert.True(GetPrivateField<bool>(vm, "_hasDeferredRemoteRefresh"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedOrArchivedViewedTourIsRemovedFromPreview(bool archive)
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync(); await vm.FocusTourAsync(42);
        if (archive) tours.Items[0].IsArchived = true;
        else tours.Items.RemoveAt(0);
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Tours));
        Assert.Equal(0, vm.LoadedTourId);
        Assert.DoesNotContain(vm.RouteStops, s => s.OrderId == "A");
    }

    private static Task InvokeRemoteTestAsync(KarteSectionViewModel vm, string method) =>
        (Task)typeof(KarteSectionViewModel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null)!;
    private static void ReleaseRemoteTestLeases(KarteSectionViewModel vm) =>
        typeof(KarteSectionViewModel).GetMethod("ReleaseLoadedTourLeases", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
    private static T CloneRemote<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static (RemoteOrdersStore, RemoteToursStore) RemoteStores()
    {
        Order Order(string id, int tourId) => new()
        {
            Id = id, CustomerName = "Kunde " + id, Address = "Hauptstrasse 1, 8000 Zürich", AssignedTourId = tourId.ToString(),
            ConcurrencyToken = "order-" + id, Location = new(47.1, 8.1), AvisoStatus = "Bestätigt", OrderStatus = Tourenplaner.CSharp.Domain.Models.Order.ReadyToDeliverStatus,
            DeliveryAddress = new() { Name = "Kunde " + id, Street = "Hauptstrasse", HouseNumber = "1", PostalCode = "8000", City = "Zürich" },
            Products = [new() { PositionId = "P" + id, Name = "Produkt", Quantity = 1, DeliveryStatus = OrderProductInfo.InStockStatus }],
            Deliveries = [new() { Id = "D" + id, SequenceNumber = 1, AssignedTourId = tourId.ToString(), Status = OrderDeliveryStatus.Planned,
                Lines = [new() { OrderProductPositionId = "P" + id, Quantity = 1 }] }]
        };
        TourRecord Tour(int id, string orderId)
        {
            var tour = new TourRecord
            {
                Id = id, Name = "Tour " + id, ConcurrencyToken = "tour-" + id, Date = DateTime.Today.AddDays(1).ToString("dd.MM.yyyy"), StartTime = "07:30",
                Stops = [new() { Id = "company:start", Auftragsnummer = "FIRMA-START", Name = "Firma (Start)", Order = 1, Lat = 47, Lng = 8 },
                    new() { Id = "stop-" + orderId, Auftragsnummer = orderId, DeliveryId = "D" + orderId, DeliverySequenceNumber = 1,
                        Name = "Kunde " + orderId, Address = "Hauptstrasse 1, 8000 Zürich", Order = 2, Lat = 47.1, Lng = 8.1, Lon = 8.1, ServiceMinutes = 15 },
                    new() { Id = "company:end", Auftragsnummer = "FIRMA-ENDE", Name = "Firma (Ende)", Order = 3, Lat = 47, Lng = 8 }],
                RouteGeometryPoints = [new(47, 8), new(47.1, 8.1), new(47, 8)]
            };
            new TourScheduleService().ApplySchedule(tour);
            return tour;
        }
        return (new() { Items = [Order("A", 42), Order("B", 43)] }, new() { Items = [Tour(42, "A"), Tour(43, "B")] });
    }

    private sealed class RemoteOrdersStore : IOrderRepository
    {
        public List<Order> Items = [];
        public int Reads, Writes;
        public bool FailReads;
        public TaskCompletionSource<IReadOnlyList<Order>>? DelayedRead;
        public TaskCompletionSource? ReadStarted;
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            ReadStarted?.TrySetResult();
            if (FailReads) throw new IOException("offline");
            return DelayedRead?.Task ?? Task.FromResult<IReadOnlyList<Order>>(CloneRemote(Items));
        }
        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
        { Writes++; Items = CloneRemote(orders.ToList()); return Task.CompletedTask; }
    }
    private sealed class RemoteToursStore : ITourRecordStore
    {
        public List<TourRecord> Items = [];
        public int Reads, Writes;
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult<IReadOnlyList<TourRecord>>(CloneRemote(Items)); }
        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default)
        { Writes++; Items = CloneRemote(tours.ToList()); return Task.CompletedTask; }
    }
}
