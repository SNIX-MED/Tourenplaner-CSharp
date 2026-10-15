using System.Collections.Concurrent;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class KarteSearchDetailsTests
{
    [Theory]
    [InlineData(900, true, 0)]
    [InlineData(900, false, 900)]
    [InlineData(20, false, 100)]
    [InlineData(15000, false, 10000)]
    public void RouteRecalculationDebounce_ImmediateUserActionsBypassDelay(
        int configuredDebounceMs,
        bool calculateImmediately,
        int expectedDebounceMs)
    {
        var result = KarteSectionViewModel.ResolveRouteRecalculationDebounceMs(
            configuredDebounceMs,
            calculateImmediately);

        Assert.Equal(expectedDebounceMs, result);
    }

    [Fact]
    public void PartiallyReadyAndInTransit_UseDistinctPinAndLegendColors()
    {
        var viewModel = CreateViewModel();

        var inTransitColor = viewModel.ResolveOrderStatusColor(Order.InTransitStatus, false);
        var partiallyReadyColor = viewModel.ResolveOrderStatusColor(Order.PartiallyReadyStatus, false);

        Assert.Equal(AppSettings.DefaultStatusColorOnTheWay, inTransitColor);
        Assert.Equal(AppSettings.DefaultStatusColorPartiallyReady, partiallyReadyColor);
        Assert.NotEqual(inTransitColor, partiallyReadyColor);
        Assert.Equal(partiallyReadyColor, viewModel.LegendStatusColorPartiallyReady);
    }

    [Fact]
    public void OrderedStatus_UsesPinkForPinLegendAndProductBackground()
    {
        var viewModel = CreateViewModel();

        var orderedColor = viewModel.ResolveOrderStatusColor(Order.OrderedStatus, false);
        ProductStatusVisualService.Configure(
            AppSettings.DefaultStatusColorNotSpecified,
            orderedColor,
            AppSettings.DefaultStatusColorOnTheWay,
            AppSettings.DefaultStatusColorInStock);

        Assert.Equal("#EC4899", orderedColor);
        Assert.Equal(orderedColor, viewModel.LegendStatusColorOrdered);
        Assert.Equal("#EC4899", ProductStatusVisualService.ResolveBorderColor(OrderProductInfo.OrderedStatus));
        Assert.Equal("#33EC4899", ProductStatusVisualService.ResolveBackgroundColor(OrderProductInfo.OrderedStatus));
    }

    [Fact]
    public void HiddenReturnLeg_TruncatesGeometryAtLastDeliveryStop()
    {
        IReadOnlyList<GeoPoint> completeRoute =
        [
            new(47.10, 8.10),
            new(47.20, 8.20),
            new(47.30001, 8.30001),
            new(47.40, 8.40),
            new(47.50, 8.50)
        ];

        var visibleRoute = KarteSectionViewModel.BuildVisibleRouteGeometry(
            completeRoute,
            latitude: 47.30,
            longitude: 8.30);

        Assert.Equal(3, visibleRoute.Count);
        Assert.Equal(completeRoute[2], visibleRoute[^1]);
    }

    [Fact]
    public async Task ClosingDetails_ClearsSearchThatOpenedTheOrder()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync();

        await viewModel.ApplySearchSuggestionAsync(new MapSearchSuggestionItem(
            "Auftrag 222016 · Testkunde",
            "Teststrasse 1, 8000 Zürich",
            "Auftrag",
            47.3769,
            8.5417,
            "222016"));

        Assert.Equal("222016", viewModel.SelectedOrder?.OrderId);
        Assert.NotEmpty(viewModel.SearchText);

        viewModel.CloseDetailsCommand.Execute(null);

        Assert.Null(viewModel.SelectedOrder);
        Assert.Empty(viewModel.SearchText);
        Assert.All(viewModel.GetMapMarkerSnapshot(), marker => Assert.False(marker.IsDimmed));
    }

    [Fact]
    public async Task ClosingDetails_KeepsSearchWhenOrderWasOpenedDirectly()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync();
        viewModel.SearchText = "1";
        viewModel.SelectedOrder = Assert.Single(viewModel.MapOrders);

        viewModel.CloseDetailsCommand.Execute(null);

        Assert.Null(viewModel.SelectedOrder);
        Assert.Equal("1", viewModel.SearchText);
    }

    [Fact]
    public async Task ConcurrentRefreshes_AreSerializedAndDoNotDuplicateMapPins()
    {
        var repository = new ControlledOrderStore();
        var viewModel = CreateViewModel(repository);
        await viewModel.RefreshAsync();
        Assert.Single(viewModel.MapOrders);

        repository.DelayLoads = true;
        var completedCalls = repository.LoadCallCount;
        var firstRefresh = viewModel.RefreshAsync();
        await repository.WaitForLoadCallAsync(completedCalls + 1);

        var secondRefresh = viewModel.RefreshAsync();
        await Task.Delay(50);
        Assert.Equal(completedCalls + 1, repository.LoadCallCount);

        await repository.CompleteNextLoadAsync();
        await firstRefresh;
        await repository.WaitForLoadCallAsync(completedCalls + 2);
        await repository.CompleteNextLoadAsync();
        await secondRefresh;

        var marker = Assert.Single(viewModel.MapOrders);
        Assert.Equal("222016", marker.OrderId);
    }

    [Fact]
    public async Task SelectingAndRefreshingTourOverview_DoesNotRequestMapRecentering()
    {
        var viewModel = CreateViewModel(tourRepository: new SingleTourStore());
        await viewModel.RefreshAsync();

        var tour = Assert.Single(viewModel.TourOverviewItems);
        viewModel.SelectTourOverviewById(tour.TourId);
        var focusRevision = viewModel.TourOverviewFocusRevision;
        Assert.Equal(0, focusRevision);

        await viewModel.RefreshAsync();

        Assert.Equal(focusRevision, viewModel.TourOverviewFocusRevision);
        Assert.Equal(tour.TourId, viewModel.SelectedTourOverviewItem?.TourId);
    }

    [Fact]
    public async Task PartialDeliveries_UseOnlyTheirOwnProductStatusesForBadgeAndPinColor()
    {
        var order = CreateOrder();
        order.Products =
        [
            new OrderProductInfo { PositionId = "P1", Name = "Bereites Produkt", Quantity = 1, DeliveryStatus = OrderProductInfo.InStockStatus },
            new OrderProductInfo { PositionId = "P2", Name = "Bestelltes Produkt", Quantity = 1, DeliveryStatus = OrderProductInfo.OrderedStatus }
        ];
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Lines = [new OrderDeliveryLine { OrderProductPositionId = "P1", Quantity = 1 }] },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Lines = [new OrderDeliveryLine { OrderProductPositionId = "P2", Quantity = 1 }] }
        ];
        var viewModel = CreateViewModel(new FixedOrderStore(order));

        await viewModel.RefreshAsync();

        var delivery1 = viewModel.ResolveOrderVisualInfo(order.Id, "D1");
        var delivery2 = viewModel.ResolveOrderVisualInfo(order.Id, "D2");
        Assert.Equal(Order.ReadyToDeliverStatus, delivery1.StatusLabel);
        Assert.Equal(Order.OrderedStatus, delivery2.StatusLabel);
        Assert.Equal(viewModel.ResolveOrderStatusColor(Order.ReadyToDeliverStatus, false), delivery1.StatusColorHex);
        Assert.Equal(viewModel.ResolveOrderStatusColor(Order.OrderedStatus, false), delivery2.StatusColorHex);
        Assert.NotEqual(delivery1.StatusColorHex, delivery2.StatusColorHex);

        viewModel.SelectedOrder = viewModel.MapOrders.Single(x => x.DeliveryId == "D1");
        Assert.Equal(Order.ReadyToDeliverStatus, viewModel.DetailOrderStatus);
        viewModel.SelectedOrder = viewModel.MapOrders.Single(x => x.DeliveryId == "D2");
        Assert.Equal(Order.OrderedStatus, viewModel.DetailOrderStatus);
    }

    [Fact]
    public async Task DeliveryPeriod_IsAvailableOnMapCardAndSelectedOrderDetails()
    {
        var order = CreateOrder();
        order.DeliveryPeriod = "KW 43–44";
        var viewModel = CreateViewModel(new FixedOrderStore(order));
        await viewModel.RefreshAsync();

        var marker = Assert.Single(viewModel.MapOrders);
        Assert.Equal("KW 43–44", marker.DeliveryPeriod);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.SelectedOrder = marker;
        Assert.Equal("KW 43–44", viewModel.DetailDeliveryPeriod);
        Assert.Contains(nameof(viewModel.DetailDeliveryPeriod), notifications);

        viewModel.SelectedOrder = null;
        Assert.Equal(string.Empty, viewModel.DetailDeliveryPeriod);
    }

    private static KarteSectionViewModel CreateViewModel(
        IOrderRepository? orderRepository = null,
        ITourRecordStore? tourRepository = null) => new(
        orderRepository ?? new OrderStore(),
        tourRepository ?? new EmptyTourStore(),
        new EmptyEmployeeStore(),
        new EmptyVehicleStore(),
        new SettingsStore(),
        Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N")),
        new AppDataSyncService());

    private sealed class OrderStore : IOrderRepository
    {
        private readonly List<Order> _orders =
        [
            CreateOrder()
        ];

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(_orders);

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedOrderStore(Order order) : IOrderRepository
    {
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([order]);

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ControlledOrderStore : IOrderRepository
    {
        private readonly ConcurrentQueue<TaskCompletionSource<IReadOnlyList<Order>>> _pendingLoads = new();
        private int _loadCallCount;

        public bool DelayLoads { get; set; }
        public int LoadCallCount => Volatile.Read(ref _loadCallCount);

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _loadCallCount);
            IReadOnlyList<Order> orders = [CreateOrder(), CreateOrder()];
            if (!DelayLoads)
            {
                return Task.FromResult(orders);
            }

            var completion = new TaskCompletionSource<IReadOnlyList<Order>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pendingLoads.Enqueue(completion);
            return completion.Task;
        }

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task WaitForLoadCallAsync(int expectedCall)
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (LoadCallCount < expectedCall)
            {
                if (DateTime.UtcNow >= timeout)
                {
                    throw new TimeoutException("Die erwartete Kartenaktualisierung wurde nicht gestartet.");
                }

                await Task.Delay(10);
            }
        }

        public async Task CompleteNextLoadAsync()
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            TaskCompletionSource<IReadOnlyList<Order>>? completion;
            while (!_pendingLoads.TryDequeue(out completion))
            {
                if (DateTime.UtcNow >= timeout)
                {
                    throw new TimeoutException("Die erwartete verzögerte Kartenaktualisierung wurde nicht registriert.");
                }

                await Task.Delay(10);
            }

            completion.SetResult([CreateOrder(), CreateOrder()]);
        }
    }

    private static Order CreateOrder() => new()
    {
        Id = "222016",
        CustomerName = "Testkunde",
        Address = "Teststrasse 1, 8000 Zürich",
        DeliveryType = "Frei Bordsteinkante",
        AssignedTourId = "19",
        Location = new GeoPoint(47.3769, 8.5417)
    };

    private sealed class EmptyTourStore : ITourRecordStore
    {
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TourRecord>>([]);

        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SingleTourStore : ITourRecordStore
    {
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TourRecord>>(
            [
                new TourRecord
                {
                    Id = 19,
                    Name = "Tour 19",
                    Date = DateTime.Today.AddDays(1).ToString("dd.MM.yyyy"),
                    StartTime = "07:30",
                    Stops =
                    [
                        new TourStopRecord { Id = "__company_start__", Auftragsnummer = "__company_start__", Order = 1 },
                        new TourStopRecord { Id = "222016", Auftragsnummer = "222016", Order = 2 },
                        new TourStopRecord { Id = "__company_end__", Auftragsnummer = "__company_end__", Order = 3 }
                    ]
                }
            ]);

        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyEmployeeStore : IEmployeeDataStore
    {
        public Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Employee>>([]);

        public Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyVehicleStore : IVehicleDataStore
    {
        public Task<VehicleDataRecord> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VehicleDataRecord());

        public Task SaveAsync(VehicleDataRecord payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SettingsStore : IAppSettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
