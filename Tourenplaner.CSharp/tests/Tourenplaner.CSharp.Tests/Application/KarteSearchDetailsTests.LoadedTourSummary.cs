using System.Reflection;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialDeliveryMapDetailsIncludeNegativeProductWeights(bool negativeQuantity)
    {
        var order = CreateOrder();
        order.Products = [new() { PositionId = "P", Name = "Ware", Quantity = 10, UnitWeightKg = 5, WeightKg = 50 },
            new() { PositionId = "N", Name = "Abzug", Quantity = negativeQuantity ? -10 : 10, UnitWeightKg = negativeQuantity ? 2 : -2, WeightKg = -20 }];
        order.Deliveries = [new() { Id = "D1", SequenceNumber = 1, Lines = [
            new() { OrderProductPositionId = "P", Quantity = 3 }, new() { OrderProductPositionId = "N", Quantity = negativeQuantity ? -3 : 3 }] },
            new() { Id = "D2", SequenceNumber = 2, Lines = [
            new() { OrderProductPositionId = "P", Quantity = 7 }, new() { OrderProductPositionId = "N", Quantity = negativeQuantity ? -7 : 7 }] }];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        var pin = vm.MapOrders.Single(p => p.DeliveryId == "D1");
        Assert.Equal("9", pin.TotalWeightKgText);
        vm.SelectedOrder = pin;
        Assert.Equal("Gewicht dieser Teillieferung: 9 kg", vm.DetailProductsTotalWeightText);
        vm.SelectedOrder = vm.MapOrders.Single(p => p.DeliveryId == "D2");
        Assert.Equal("Gewicht dieser Teillieferung: 21 kg", vm.DetailProductsTotalWeightText);
    }

    [Fact]
    public async Task LoadedSummaryUsesLoadedTourAndCurrentRouteWeightAndClearsOnUnload()
    {
        var order = CreateOrder();
        order.Products = [new() { PositionId = "P", Quantity = 10, UnitWeightKg = 5, WeightKg = 50 }];
        order.Deliveries = [new() { Id = "D1", Lines = [new() { OrderProductPositionId = "P", Quantity = 3 }] },
            new() { Id = "D2", Lines = [new() { OrderProductPositionId = "P", Quantity = 7 }] }];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        GetPrivateField<List<TourRecord>>(vm, "_savedTours").AddRange([
            new() { Id = 42, Name = "Geladene Tour", EmployeeIds = ["driver"] },
            new() { Id = 99, Name = "Andere Auswahl", EmployeeIds = ["other"] }
        ]);
        GetPrivateField<Dictionary<string, string>>(vm, "_employeeLabelsById")["driver"] = "Pascal Zander";
        SetPrivateField(vm, "_activeTourId", 42);
        SetPrivateField(vm, "_selectedTourOverviewId", 99);
        vm.RouteStops.Add(new() { OrderId = order.Id, DeliveryId = "D1" });
        var update = typeof(KarteSectionViewModel).GetMethod("UpdateRouteSummary", BindingFlags.Instance | BindingFlags.NonPublic)!;
        update.Invoke(vm, null);
        Assert.Equal(42, vm.LoadedTourSummary!.TourId);
        Assert.Equal("Pascal", vm.LoadedTourSummary.EmployeeNames);
        Assert.Equal("15 kg", vm.LoadedTourSummary.TotalWeightText);
        Assert.Equal("Nicht zugewiesen", vm.LoadedTourSummary.VehicleName);
        vm.RouteStops.Add(new() { OrderId = order.Id, DeliveryId = "D2" });
        update.Invoke(vm, null);
        Assert.Equal("50 kg", vm.LoadedTourSummary!.TotalWeightText);
        SetPrivateField(vm, "_activeTourId", 0);
        update.Invoke(vm, null);
        Assert.Null(vm.LoadedTourSummary);
    }
}
