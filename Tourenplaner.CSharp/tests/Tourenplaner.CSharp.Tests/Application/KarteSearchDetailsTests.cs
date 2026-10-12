using System.Collections.Concurrent;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class KarteSearchDetailsTests
{
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
    public async Task RefreshingTourOverview_DoesNotRequestMapRecentering()
    {
        var viewModel = CreateViewModel(tourRepository: new SingleTourStore());
        await viewModel.RefreshAsync();

        var tour = Assert.Single(viewModel.TourOverviewItems);
        viewModel.SelectTourOverviewById(tour.TourId);
        var focusRevision = viewModel.TourOverviewFocusRevision;
        Assert.True(focusRevision > 0);

        await viewModel.RefreshAsync();

        Assert.Equal(focusRevision, viewModel.TourOverviewFocusRevision);
        Assert.Equal(tour.TourId, viewModel.SelectedTourOverviewItem?.TourId);
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
