using System.Reflection;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Abstractions;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverviewEditCommandOpensClickedTourAndCancellationKeepsItsStops(bool anotherTourLoaded)
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        if (anotherTourLoaded) await vm.FocusTourAsync(43);
        vm.SelectTourOverviewById(42);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = System.Text.Json.JsonSerializer.Serialize(tours.Items.Single(t => t.Id == 42).Stops);
        vm.ShowTourDetailsDialog = _ =>
        {
            Assert.Equal(42, vm.LoadedTourId);
            Assert.True(vm.IsEditingTour(42));
            Assert.Contains(vm.RouteStops, stop => stop.OrderId == "A" && stop.DeliveryId == "DA");
            Assert.DoesNotContain(vm.RouteStops, stop => stop.OrderId == "B");
            reached.SetResult();
            return null;
        };
        vm.EditSelectedTourOverviewCommand.Execute(null);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!vm.EditSelectedTourOverviewCommand.CanExecute(null)) await Task.Delay(10, timeout.Token);
        Assert.False(vm.IsLoadedTourEditing);
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(tours.Items.Single(t => t.Id == 42).Stops));
    }

    [Fact]
    public async Task CancellingSwitchToOverviewDetailsPreservesDirtyLoadedTourAndItsLease()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        Assert.True(await vm.PrepareTourDetailsEditingAsync(43));
        try
        {
            vm.RouteStops.Single(stop => stop.OrderId == "B").PlannedStayMinutes = 77;
            SetPrivateField(vm, "_hasUnsavedRouteChanges", true);
            vm.SelectTourOverviewById(42);
            var before = vm.RouteStops.ToArray();
            var lease = GetPrivateField<object>(vm, "_loadedTourLease");
            vm.ShowTourDetailsDialog = _ => throw new Exception("A cancelled switch must not open the dialog");
            await (Task)typeof(KarteSectionViewModel).GetMethod("OpenEditTourDetailsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [42])!;
            Assert.Equal(43, vm.LoadedTourId);
            Assert.True(vm.IsEditingTour(43));
            Assert.True(GetPrivateField<bool>(vm, "_hasUnsavedRouteChanges"));
            Assert.Same(lease, GetPrivateField<object>(vm, "_loadedTourLease"));
            Assert.Equal(before, vm.RouteStops.ToArray());
            Assert.Equal(77, vm.RouteStops.Single(stop => stop.OrderId == "B").PlannedStayMinutes);
        }
        finally { ReleaseEditing(vm); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverviewDetailsSavePreservesSavedStopsOrIncludesExistingDraftChanges(bool editedRoute)
    {
        var (orders, tours) = RemoteStores();
        foreach (var order in orders.Items)
        {
            order.Products[0].UnitWeightKg = 10;
            order.Products[0].WeightKg = 10;
        }
        var saved = tours.Items.Single(t => t.Id == 42);
        saved.Stops.Insert(2, new() { Id = "pause:break", StopKind = "pause", Name = "Pause", ServiceMinutes = 15 });
        for (var index = 0; index < saved.Stops.Count; index++) saved.Stops[index].Order = index + 1;
        new TourScheduleService().ApplySchedule(saved);
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        vm.SelectTourOverviewById(42);
        var before = System.Text.Json.JsonSerializer.Serialize(tours.Items.Single(t => t.Id == 42).Stops);
        var geometry = saved.RouteGeometryPoints.ToArray();
        vm.ShowTourDetailsDialog = _ =>
        {
            Assert.True(vm.IsEditingTour(42));
            SetPrivateField(vm, "_isRouteCalculating", !editedRoute);
            if (editedRoute)
            {
                vm.RouteStops.Single(stop => stop.OrderId == "A").PlannedStayMinutes = 77;
                SetPrivateField(vm, "_hasUnsavedRouteChanges", true);
                var legs = GetPrivateField<List<OsrmRouteLeg>>(vm, "_routeLegs");
                legs.Clear();
                var coordinateStops = vm.RouteStops.Count(stop => !stop.IsPauseStop && double.IsFinite(stop.Latitude) && double.IsFinite(stop.Longitude));
                for (var index = 1; index < coordinateStops; index++) legs.Add(new(10, 5));
                typeof(KarteSectionViewModel).GetMethod("RefreshDriveTimesFromCurrentRoute", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
            }
            return new CreateTourDialogResult(saved.Name, saved.Date, saved.StartTime, null, null, null, null, [], [], true) { IsFull = true };
        };
        try
        {
            await (Task)typeof(KarteSectionViewModel).GetMethod("OpenEditTourDetailsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [42])!;
            var stored = tours.Items.Single(t => t.Id == 42);
            Assert.True(stored.IsFull);
            if (!editedRoute)
            {
                Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(stored.Stops));
                Assert.Equal(geometry, stored.RouteGeometryPoints.ToArray());
            }
            else Assert.Equal(77, stored.Stops.Single(stop => stop.Auftragsnummer == "A").ServiceMinutes);
            Assert.Contains(stored.Stops, stop => stop.Auftragsnummer == "A" && stop.DeliveryId == "DA");
            Assert.Equal("42", orders.Items.Single(order => order.Id == "A").Deliveries.Single().AssignedTourId);
        }
        finally { ReleaseEditing(vm); }
    }

    private static void ReleaseEditing(KarteSectionViewModel vm) =>
        typeof(KarteSectionViewModel).GetMethod("ReleaseLoadedTourLeases", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);

    [Theory]
    [InlineData("tour")]
    [InlineData("order")]
    public async Task OverviewDetailsRespectTourAndOrderLocks(string lockedKind)
    {
        CollaborationSessionService.Configure(new OverviewDetailsLockService(lockedKind));
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        try
        {
            await vm.RefreshAsync();
            Assert.False(await vm.PrepareTourDetailsEditingAsync(42));
            Assert.False(vm.IsLoadedTourEditing);
            Assert.Equal(0, GetPrivateField<int>(vm, "_loadedTourLeaseId"));
        }
        finally
        {
            ReleaseEditing(vm);
            await CollaborationSessionService.ShutdownAsync();
            CollaborationSessionService.Configure(null);
        }
    }

    private sealed class OverviewDetailsLockService(string lockedKind) : ICollaborationLockService
    {
        public Task<CollaborationLockResult> TryAcquireAsync(string kind, string id, string user, CancellationToken cancellationToken = default) =>
            Task.FromResult(kind == lockedKind ? CollaborationLockResult.Conflict("Anderer Arbeitsplatz") : CollaborationLockResult.Success());
        public Task ReleaseAsync(string kind, string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReleaseAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PreparingOverviewDetailsEditingLoadsSelectedSavedTourAndItsStops()
    {
        var store = new ChangingPreviewTourStore();
        var order = CreateOrder();
        order.AssignedTourId = "42";
        var vm = CreateViewModel(new FixedOrderStore(order), store);
        await vm.RefreshAsync();
        vm.SelectTourOverviewById(42);
        Assert.Equal(0, vm.LoadedTourId);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            var acquired = await vm.PrepareTourDetailsEditingAsync(42);
            Assert.True(acquired);
            Assert.Equal(42, vm.LoadedTourId);
            Assert.Contains(vm.RouteStops, stop => stop.OrderId == order.Id);
            Assert.Equal(42, vm.SelectedTourOverviewItem?.TourId);
            Assert.Equal(42, GetPrivateField<int>(vm, "_selectedTourOverviewId"));
        }
        finally { typeof(KarteSectionViewModel).GetMethod("ReleaseLoadedTourLeases", flags)!.Invoke(vm, null); }
    }
}
