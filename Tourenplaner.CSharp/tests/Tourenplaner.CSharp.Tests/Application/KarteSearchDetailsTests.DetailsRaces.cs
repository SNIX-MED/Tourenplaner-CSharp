using System.Reflection;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Fact]
    public async Task OldDetailsRequestCannotOpenDialogAfterSameTourLeaseWasReplaced()
    {
        var (orders, tours) = RemoteStores();
        var employees = new DelayedDetailsEmployeeStore();
        var vm = CreateViewModel(orders, tours, employeeRepository: employees);
        await vm.RefreshAsync();
        await vm.FocusTourAsync(42);
        employees.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;
        vm.ShowTourDetailsDialog = _ => { opened++; return null; };
        var request = OpenDetailsForTestAsync(vm, 42);
        await employees.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            ReleaseEditing(vm);
            Assert.True(await vm.PrepareTourDetailsEditingAsync(42));
            var replacementLease = GetPrivateField<object>(vm, "_loadedTourLease");
            employees.Pending.SetResult([]);
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, opened);
            Assert.True(vm.IsEditingTour(42));
            Assert.Same(replacementLease, GetPrivateField<object>(vm, "_loadedTourLease"));
        }
        finally
        {
            employees.Pending.TrySetResult([]);
            ReleaseEditing(vm);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailsSaveCannotContinueAfterEditingSessionChangedDuringDatabaseRead(bool sameTour)
    {
        var (orders, tours) = RemoteStores();
        foreach (var order in orders.Items) order.Products[0].UnitWeightKg = 10;
        var tour = tours.Items.Single(t => t.Id == 42);
        tour.Stops.Insert(2, new() { Id = "pause:break", StopKind = "pause", ServiceMinutes = 15 });
        for (var i = 0; i < tour.Stops.Count; i++) tour.Stops[i].Order = i + 1;
        new Tourenplaner.CSharp.Application.Services.TourScheduleService().ApplySchedule(tour);
        var delayed = new DelayedDetailsTourStore(tours);
        var vm = CreateViewModel(orders, delayed);
        await vm.RefreshAsync();
        vm.ShowTourDetailsDialog = _ =>
        {
            delayed.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new CreateTourDialogResult(tour.Name, tour.Date, tour.StartTime, null, null, null, null, [], [], true) { IsFull = true };
        };
        var request = OpenDetailsForTestAsync(vm, 42);
        await delayed.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = delayed.Pending!;
        delayed.Pending = null;
        try
        {
            ReleaseEditing(vm);
            if (sameTour) Assert.True(await vm.PrepareTourDetailsEditingAsync(42));
            else await vm.FocusTourAsync(43);
            var replacementLease = GetPrivateField<object>(vm, "_loadedTourLease");
            pending.SetResult(CloneRemote(tours.Items));
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(tours.Items.Single(t => t.Id == 42).IsFull);
            Assert.Equal(sameTour ? 42 : 43, vm.LoadedTourId);
            Assert.Same(replacementLease, GetPrivateField<object>(vm, "_loadedTourLease"));
        }
        finally { pending.TrySetResult(CloneRemote(tours.Items)); ReleaseEditing(vm); }
    }

    private static Task OpenDetailsForTestAsync(KarteSectionViewModel vm, int tourId) =>
        (Task)typeof(KarteSectionViewModel).GetMethod("OpenEditTourDetailsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [tourId])!;

    [Fact]
    public async Task DetailsSaveCompletionCannotClearAnotherToursDraftOrSwitchBack()
    {
        var (orders, tours) = RemoteStores();
        foreach (var order in orders.Items) order.Products[0].UnitWeightKg = 10;
        var tour = tours.Items.Single(t => t.Id == 42);
        tour.Stops.Insert(2, new() { Id = "pause:break", StopKind = "pause", ServiceMinutes = 15 });
        for (var i = 0; i < tour.Stops.Count; i++) tour.Stops[i].Order = i + 1;
        new Tourenplaner.CSharp.Application.Services.TourScheduleService().ApplySchedule(tour);
        var delayed = new DelayedDetailsTourStore(tours);
        var vm = CreateViewModel(orders, delayed);
        await vm.RefreshAsync();
        vm.ShowTourDetailsDialog = _ =>
        {
            delayed.PendingSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new CreateTourDialogResult(tour.Name, tour.Date, tour.StartTime, null, null, null, null, [], [], true) { IsFull = true };
        };
        var request = OpenDetailsForTestAsync(vm, 42);
        await delayed.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = delayed.PendingSave!;
        delayed.PendingSave = null;
        try
        {
            ReleaseEditing(vm);
            await vm.FocusTourAsync(43);
            vm.RouteStops.Single(stop => stop.OrderId == "B").PlannedStayMinutes = 77;
            SetPrivateField(vm, "_hasUnsavedRouteChanges", true);
            pending.SetResult();
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(tours.Items.Single(t => t.Id == 42).IsFull);
            Assert.Equal(43, vm.LoadedTourId);
            Assert.True(GetPrivateField<bool>(vm, "_hasUnsavedRouteChanges"));
            Assert.Equal(77, vm.RouteStops.Single(stop => stop.OrderId == "B").PlannedStayMinutes);
        }
        finally { pending.TrySetResult(); ReleaseEditing(vm); }
    }

    private sealed class DelayedDetailsEmployeeStore : IEmployeeDataStore
    {
        public TaskCompletionSource<IReadOnlyList<Employee>>? Pending;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (Pending is null) return Task.FromResult<IReadOnlyList<Employee>>([]);
            ReadStarted.TrySetResult();
            return Pending.Task;
        }
        public Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DelayedDetailsTourStore(RemoteToursStore inner) : ITourRecordStore
    {
        public TaskCompletionSource<IReadOnlyList<TourRecord>>? Pending;
        public TaskCompletionSource? PendingSave;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (Pending is null) return inner.LoadAsync(cancellationToken);
            ReadStarted.TrySetResult();
            return Pending.Task;
        }
        public async Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default)
        {
            var pending = PendingSave;
            if (pending is not null)
            {
                SaveStarted.TrySetResult();
                await pending.Task;
            }
            await inner.SaveAsync(tours, cancellationToken);
        }
    }
}
