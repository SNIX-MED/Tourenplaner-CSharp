using System.Collections.Concurrent;
using System.Globalization;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class ToursSectionViewModelTests
{
    [Fact]
    public async Task Refresh_DeduplicatesRepositoryRowsByTourId()
    {
        var repository = new ControlledTourStore([CreateTour(19), CreateTour(19)]);
        var viewModel = new ToursSectionViewModel(
            repository,
            new SingleOrderRepository(),
            new EmptyEmployeeStore(),
            new EmptyVehicleStore(),
            new DefaultSettingsStore());

        await viewModel.RefreshAsync();

        var tour = Assert.Single(GetLoadedTours(viewModel));
        Assert.Equal(19, tour.Id);
    }

    [Fact]
    public async Task ConcurrentRefreshes_DoNotDuplicateTours()
    {
        var repository = new ControlledTourStore([CreateTour(19)]);
        var viewModel = new ToursSectionViewModel(
            repository,
            new SingleOrderRepository(),
            new EmptyEmployeeStore(),
            new EmptyVehicleStore(),
            new DefaultSettingsStore());

        await viewModel.RefreshAsync();
        Assert.Single(GetLoadedTours(viewModel));

        var firstRefresh = viewModel.RefreshAsync();
        await repository.WaitForLoadCallAsync(3);

        var secondRefresh = viewModel.RefreshAsync();
        await Task.Delay(50);
        Assert.Equal(3, repository.LoadCallCount);

        repository.CompleteNextLoad();
        await firstRefresh;
        await repository.WaitForLoadCallAsync(4);
        repository.CompleteNextLoad();
        await secondRefresh;

        var tour = Assert.Single(GetLoadedTours(viewModel));
        Assert.Equal(19, tour.Id);
    }

    private static IReadOnlyList<TourRecord> GetLoadedTours(ToursSectionViewModel viewModel)
    {
        var field = typeof(ToursSectionViewModel).GetField(
            "_loadedTours",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return Assert.IsType<List<TourRecord>>(field?.GetValue(viewModel));
    }

    private static TourRecord CreateTour(int id) => new()
    {
        Id = id,
        Name = $"Tour {id}",
        Date = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        StartTime = "07:30",
        Stops =
        [
            CreateCompanyStop(TourStopIdentity.CompanyStartStopId, TourStopIdentity.CompanyStartOrderNumber, 1),
            new TourStopRecord
            {
                Id = "A-1",
                Auftragsnummer = "A-1",
                Order = 2,
                PlannedArrival = "08:00",
                PlannedArrivalOptimistic = "08:00",
                PlannedArrivalPessimistic = "08:00",
                PlannedDeparture = "08:10"
            },
            CreateCompanyStop(TourStopIdentity.CompanyEndStopId, TourStopIdentity.CompanyEndOrderNumber, 3)
        ]
    };

    private static TourStopRecord CreateCompanyStop(string id, string orderNumber, int order) => new()
    {
        Id = id,
        Auftragsnummer = orderNumber,
        Order = order,
        PlannedArrival = "07:30",
        PlannedArrivalOptimistic = "07:30",
        PlannedArrivalPessimistic = "07:30",
        PlannedDeparture = "07:30"
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Die erwartete asynchrone Aktualisierung wurde nicht rechtzeitig erreicht.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class ControlledTourStore(IReadOnlyList<TourRecord> tours) : ITourRecordStore
    {
        private readonly ConcurrentQueue<TaskCompletionSource<IReadOnlyList<TourRecord>>> _pendingLoads = new();
        private int _loadCallCount;

        public int LoadCallCount => Volatile.Read(ref _loadCallCount);

        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _loadCallCount);
            if (call <= 2)
            {
                return Task.FromResult(tours);
            }

            var completion = new TaskCompletionSource<IReadOnlyList<TourRecord>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pendingLoads.Enqueue(completion);
            return completion.Task;
        }

        public Task SaveAsync(IEnumerable<TourRecord> items, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task WaitForLoadCallAsync(int expectedCall) =>
            WaitUntilAsync(() => LoadCallCount >= expectedCall);

        public void CompleteNextLoad()
        {
            Assert.True(_pendingLoads.TryDequeue(out var completion));
            completion.SetResult(tours);
        }
    }

    private sealed class SingleOrderRepository : IOrderRepository
    {
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(
            [
                new Order
                {
                    Id = "A-1",
                    CustomerName = "Kunde Eins",
                    Address = "Teststrasse 1, 8000 Zürich",
                    DeliveryType = "Frei Bordsteinkante"
                }
            ]);

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) =>
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

    private sealed class DefaultSettingsStore : IAppSettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
