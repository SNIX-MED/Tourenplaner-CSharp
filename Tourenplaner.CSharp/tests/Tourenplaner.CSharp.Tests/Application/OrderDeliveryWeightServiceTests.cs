using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderDeliveryWeightServiceTests
{
    private static Order Order() => new()
    {
        Id = "A", ManualTotalWeightKg = 900,
        Products = [new() { PositionId = "P", Name = "Produkt", Quantity = 10, UnitWeightKg = 5, WeightKg = 50 }],
        Deliveries = [new() { Id = "D1", AssignedTourId = "7", Lines = [new() { OrderProductPositionId = "P", Quantity = 3 }] },
            new() { Id = "D2", AssignedTourId = "8", Lines = [new() { OrderProductPositionId = "P", Quantity = 7 }] }]
    };

    [Fact]
    public void SplitWeightsUseAllocatedQuantitiesAndIgnoreWholeOrderManualOverride()
    {
        var order = Order();
        Assert.Equal(15, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D1"));
        Assert.Equal(35, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D2"));
        Assert.Equal(900, order.ManualTotalWeightKg);
    }

    [Fact]
    public void LegacyStopsUseOnlyPartsAssignedToTheirTour()
    {
        var order = Order();
        Assert.Equal(15, OrderDeliveryWeightService.ResolveStopWeightKg(order, null, "7"));
        Assert.Equal(35, OrderDeliveryWeightService.ResolveStopWeightKg(order, null, "8"));
        Assert.Equal(0, OrderDeliveryWeightService.ResolveStopWeightKg(order, null, "9"));
        Assert.Equal(0, OrderDeliveryWeightService.ResolveStopWeightKg(order, "missing"));
    }

    [Fact]
    public void TourTotalsCountEachDeliveryOnceAndCanIncludeBothParts()
    {
        var tour = new TourRecord { Id = 7, Stops = [new() { Auftragsnummer = "A", DeliveryId = "D1" },
            new() { Auftragsnummer = "A", DeliveryId = "D1" }] };
        Assert.Equal(15, OrderDeliveryWeightService.ResolveTourOrderWeightKg(tour, [Order()]));
        tour.Stops.Add(new() { Auftragsnummer = "A", DeliveryId = "D2" });
        Assert.Equal(50, OrderDeliveryWeightService.ResolveTourOrderWeightKg(tour, [Order()]));
    }

    [Fact]
    public void LegacyAndExplicitStopForSamePartAreNotDoubleCounted()
    {
        var tour = new TourRecord { Id = 7, Stops = [new() { Auftragsnummer = "A" }, new() { Auftragsnummer = "A", DeliveryId = "D1" }] };
        Assert.Equal(15, OrderDeliveryWeightService.ResolveTourOrderWeightKg(tour, [Order()]));
    }

    [Fact]
    public void ProductTotalWeightProvidesFallbackWhenUnitWeightIsMissing()
    {
        var order = Order();
        order.Products[0].UnitWeightKg = 0;
        Assert.Equal(15, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D1"));
        var product = Assert.Single(OrderDeliveryWeightService.ResolveStopProducts(order, "D1"));
        Assert.Equal(3, product.Quantity);
        Assert.Equal(15, product.WeightKg);
        Assert.Equal(10, order.Products[0].Quantity);
        Assert.Equal(50, order.Products[0].WeightKg);
    }

    [Fact]
    public void UnsplitOrderRetainsItsManualWeightEvenWithInternalDeliveryRecord()
    {
        var order = Order();
        order.Deliveries.RemoveAt(1);
        order.Deliveries[0].Lines[0].Quantity = 10;
        Assert.Equal(900, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D1"));
        Assert.Equal(900, OrderDeliveryWeightService.ResolveStopWeightKg(order, null));
    }

    [Fact]
    public void ZeroAllocationDoesNotUseWholeOrderOrAnotherPartsWeight()
    {
        var order = Order();
        order.Deliveries[0].Lines[0].Quantity = 0;
        Assert.Equal(0, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D1"));
        Assert.Empty(OrderDeliveryWeightService.ResolveStopProducts(order, "D1"));
    }
}
