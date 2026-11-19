using System.Reflection;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchAdditionAsksOnceAndCancellationLeavesAllStopsUntouched(bool accept)
    {
        var order = CreateOrder();
        order.AssignedTourId = null;
        var second = CreateOrder();
        second.Id = "222017";
        second.AssignedTourId = null;
        var vm = CreateViewModel(new FullWarningOrderStore([order, second]));
        await vm.RefreshAsync();
        GetPrivateField<List<TourRecord>>(vm, "_savedTours").Add(new() { Id = 42, IsFull = true });
        SetPrivateField(vm, "_activeTourId", 42);
        SetPrivateField(vm, "_loadedTourLeaseId", 42);
        SetPrivateField(vm, "_isLoadedTourEditing", true);
        GetPrivateField<HashSet<string>>(vm, "_selectedBatchOrderIds").Add(order.Id);
        GetPrivateField<HashSet<string>>(vm, "_selectedBatchOrderIds").Add(second.Id);
        var confirmations = 0;
        vm.ConfirmFullTourWarning = _ => { confirmations++; return accept; };
        var before = vm.RouteStops.ToArray();
        vm.AddSelectedOrdersToRouteCommand.Execute(null);
        Assert.Equal(1, confirmations);
        Assert.Equal(accept, vm.RouteStops.Any(s => s.OrderId == order.Id));
        Assert.Equal(accept, vm.RouteStops.Any(s => s.OrderId == second.Id));
        if (!accept) Assert.Equal(before, vm.RouteStops.ToArray());
    }

    private sealed class FullWarningOrderStore(IReadOnlyList<Order> orders) : Tourenplaner.CSharp.Application.Abstractions.IOrderRepository
    {
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(orders);
        public Task SaveAllAsync(IEnumerable<Order> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddingOrderToFullTourRequiresDecisionBeforeRouteChanges(bool accept)
    {
        var order = CreateOrder();
        order.AssignedTourId = null;
        order.IsXmlImported = true;
        order.XmlCreatedAtUtc = DateTimeOffset.UtcNow;
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        GetPrivateField<List<TourRecord>>(vm, "_savedTours").Add(new() { Id = 42, IsFull = true });
        SetPrivateField(vm, "_activeTourId", 42);
        SetPrivateField(vm, "_loadedTourLeaseId", 42);
        SetPrivateField(vm, "_isLoadedTourEditing", true);
        var before = vm.RouteStops.ToArray();
        var confirmations = 0;
        vm.ConfirmFullTourWarning = tour => { Assert.Equal(42, tour!.Id); confirmations++; return accept; };
        vm.SelectedOrder = vm.MapOrders.First(o => o.OrderId == order.Id);
        vm.AddToRouteCommand.Execute(null);
        Assert.Equal(1, confirmations);
        Assert.Equal(accept, vm.RouteStops.Any(s => s.OrderId == order.Id));
        if (!accept) Assert.Equal(before, vm.RouteStops.ToArray());
    }

    [Fact]
    public void EditingClonesKeepFullMarkerAndReason()
    {
        var tour = new TourRecord { IsFull = true, FullReason = "Gewicht ausgeschöpft", FullNote = "Keine Zuladung" };
        foreach (var (type, method) in new[] { (typeof(KarteSectionViewModel), "CloneTourRecord"), (typeof(ToursSectionViewModel), "CloneTour") })
        {
            var clone = (TourRecord)type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [tour])!;
            Assert.True(clone.IsFull);
            Assert.Equal(tour.FullReason, clone.FullReason);
            Assert.Equal(tour.FullNote, clone.FullNote);
        }
    }

    [Fact]
    public async Task RemoteFullMarkerRefreshesOverviewAndLoadedSummaryWithoutChangingRouteGeometry()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        await vm.FocusTourAsync(42);
        var geometry = vm.RouteGeometryPoints.ToArray();
        var tour = tours.Items.Single(t => t.Id == 42);
        tour.IsFull = true;
        tour.FullReason = "Platz ausgeschöpft";
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Tours));
        Assert.True(vm.TourOverviewItems.Single(t => t.TourId == 42).IsFull);
        Assert.True(vm.LoadedTourSummary!.IsFull);
        Assert.Contains("voll markiert", vm.LoadedTourSummary.FullDescription);
        Assert.DoesNotContain("Platz ausgeschöpft", vm.LoadedTourSummary.FullDescription);
        Assert.Equal(geometry, vm.RouteGeometryPoints.ToArray());
    }
}
