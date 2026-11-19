using System.Text.Json;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Theory]
    [InlineData(OrderDeliveryStatus.Planned)]
    [InlineData(OrderDeliveryStatus.InTransit)]
    public async Task ChangingOnlyFullMarkerPreservesTourMetadataAndDeliveryStatuses(OrderDeliveryStatus status)
    {
        var (orders, tours) = RemoteStores();
        foreach (var order in orders.Items) order.Products[0].UnitWeightKg = 10;
        orders.Items.Single(order => order.Id == "A").Deliveries[0].Status = status;
        var tour = tours.Items.Single(t => t.Id == 42);
        tour.RouteMode = "truck";
        tour.RouteGeometrySignature = "saved-geometry";
        tour.WebfleetDispatch = new() { State = "sent", ObjectUid = "truck-uid", Stops = [new() { StopId = "removed-stop", WebfleetOrderId = "remote-order", StateLabel = "Gesendet" }] };
        tour.TravelTimeCache["saved-leg"] = 20;
        tour.TravelTimeProfileCache["saved-leg"] = new() { OptimisticMinutes = 10, RealisticMinutes = 20, PessimisticMinutes = 30 };
        tour.Stops.Insert(2, new() { Id = "pause:break", StopKind = "pause", ServiceMinutes = 15 });
        for (var i = 0; i < tour.Stops.Count; i++) tour.Stops[i].Order = i + 1;
        new Tourenplaner.CSharp.Application.Services.TourScheduleService().ApplySchedule(tour);
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        // Model a delivery status received from another workplace after startup maintenance.
        orders.Items.Single(order => order.Id == "A").Deliveries[0].Status = status;
        string? beforeTour = null;
        string? beforeOrders = null;
        vm.ShowTourDetailsDialog = _ =>
        {
            Assert.Equal(status, orders.Items.Single(order => order.Id == "A").Deliveries[0].Status);
            beforeTour = TourContentExceptFullMarker(tours.Items.Single(t => t.Id == 42));
            beforeOrders = JsonSerializer.Serialize(orders.Items);
            return new CreateTourDialogResult(tour.Name, tour.Date, tour.StartTime, null, null, null, null, [], [], true) { IsFull = true };
        };
        try
        {
            await OpenDetailsForTestAsync(vm, 42);
            Assert.True(tours.Items.Single(t => t.Id == 42).IsFull);
            Assert.Equal(beforeTour, TourContentExceptFullMarker(tours.Items.Single(t => t.Id == 42)));
            Assert.Equal(beforeOrders, JsonSerializer.Serialize(orders.Items));
            Assert.Equal(status, orders.Items.Single(order => order.Id == "A").Deliveries[0].Status);
        }
        finally { ReleaseEditing(vm); }
    }

    private static string TourContentExceptFullMarker(TourRecord tour)
    {
        var node = JsonSerializer.SerializeToNode(tour)!.AsObject();
        node.Remove(nameof(TourRecord.IsFull));
        node.Remove(nameof(TourRecord.ConcurrencyToken));
        return node.ToJsonString();
    }

    [Fact]
    public async Task DetailsDialogCannotOverwriteNewerServerTour()
    {
        var (orders, tours) = RemoteStores();
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        vm.ShowTourDetailsDialog = _ =>
        {
            var remote = tours.Items.Single(t => t.Id == 42);
            remote.Name = "Neuer Serverstand";
            remote.ConcurrencyToken = "changed-on-server";
            return new CreateTourDialogResult("Alter Dialogstand", remote.Date, remote.StartTime, null, null, null, null, [], [], true) { IsFull = true };
        };
        try
        {
            await OpenDetailsForTestAsync(vm, 42);
            var stored = tours.Items.Single(t => t.Id == 42);
            Assert.Equal("Neuer Serverstand", stored.Name);
            Assert.Equal("changed-on-server", stored.ConcurrencyToken);
            Assert.False(stored.IsFull);
        }
        finally { ReleaseEditing(vm); }
    }
}
