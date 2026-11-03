using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderDeliveryWeightServiceTests
{
    [Fact]
    public void ReimportCannotChangeAlreadyDeliveredNegativeAllocation()
    {
        var service = new OrderDeliveryService();
        var order = new Order { Products = [new() { PositionId = "N", Name = "Abzug", Quantity = -10,
            UnitWeightKg = 2, WeightKg = -20 }], Deliveries = [
            new() { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Delivered,
                Lines = [new() { OrderProductPositionId = "N", Quantity = -3 }] },
            new() { Id = "D2", SequenceNumber = 2, Lines = [new() { OrderProductPositionId = "N", Quantity = -7 }] }
        ] };
        Assert.True(service.TryReconcileImportedProducts(order, order.Products, out var error), error);
        var changed = new List<OrderProductInfo> { new() { PositionId = "N", Name = "Abzug", Quantity = -12,
            UnitWeightKg = 2, WeightKg = -24 } };
        Assert.False(service.TryReconcileImportedProducts(order, changed, out _));
        Assert.Equal(-3, order.Deliveries[0].Lines.Single().Quantity);
        Assert.Equal(-7, order.Deliveries[1].Lines.Single().Quantity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TourStopWeightLabelPreservesNegativeSign(bool hasCurrentOrder)
    {
        var order = new Order { Products = [new() { PositionId = "N", Name = "Abzug", Quantity = -6, UnitWeightKg = 2, WeightKg = -12 }] };
        var stop = new TourStopRecord { Gewicht = "-12 kg" };
        var method = typeof(Tourenplaner.CSharp.App.ViewModels.Sections.ToursSectionViewModel).GetMethod("BuildStopWeightText",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal("-12 kg", method.Invoke(null, [hasCurrentOrder ? order : null, stop, null]));
    }

    [Fact]
    public void NegativeQuantitiesSurviveSplitReimportAndMerge()
    {
        var order = Order();
        order.ManualTotalWeightKg = null;
        order.Deliveries.Clear();
        order.Products.Add(new() { PositionId = "N", Name = "Abzug", Quantity = -10, UnitWeightKg = 2, WeightKg = -20 });
        var service = new OrderDeliveryService();
        var first = service.Create(order, [new() { OrderProductPositionId = "P", Quantity = 3 },
            new() { OrderProductPositionId = "N", Quantity = -3 }], null);
        Assert.Equal(-7, service.GetRemainingQuantities(order)["N"]);
        var second = service.EnsureRemainderDelivery(order)!;
        Assert.True(service.TryValidate(order, out var error), error);
        Assert.Equal(9, service.ResolveWeightKg(order, first));
        Assert.Equal(21, service.ResolveWeightKg(order, second));
        Assert.Equal(-6, OrderDeliveryWeightService.ResolveStopProducts(order, first.Id).Single(p => p.PositionId == "N").WeightKg);
        var imported = new List<OrderProductInfo> { order.Products[0],
            new() { PositionId = "N", Name = "Abzug", Quantity = -12, UnitWeightKg = 2, WeightKg = -24 } };
        Assert.True(service.TryReconcileImportedProducts(order, imported, out error), error);
        order.Products = imported;
        Assert.Equal(-3, first.Lines.Single(l => l.OrderProductPositionId == "N").Quantity);
        Assert.Equal(-9, second.Lines.Single(l => l.OrderProductPositionId == "N").Quantity);
        Assert.Equal(order.ResolveTotalWeightKg(), order.Deliveries.Sum(d => service.ResolveWeightKg(order, d)));
        var merged = service.MergePartialDeliveries(order);
        Assert.Equal(-12, merged.Lines.Single(l => l.OrderProductPositionId == "N").Quantity);
        Assert.Equal(26, service.ResolveWeightKg(order, merged));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-11)]
    public void InvalidSignedAllocationsAreRejected(int quantity)
    {
        var order = new Order { Products = [new() { PositionId = "N", Name = "Abzug", Quantity = -10 }] };
        var service = new OrderDeliveryService();
        Assert.Throws<InvalidOperationException>(() => service.Create(order,
            [new() { OrderProductPositionId = "N", Quantity = quantity }], null));
        order.Deliveries = [new() { Id = "D", Lines = [new() { OrderProductPositionId = "N", Quantity = quantity }] }];
        Assert.False(service.TryValidate(order, out _));
    }

    [Fact]
    public void NegativeOrderTotalMatchesSingleAndSplitDeliveries()
    {
        var order = Order();
        order.ManualTotalWeightKg = null;
        order.Products[0].UnitWeightKg = -2;
        order.Products[0].WeightKg = -20;
        var service = new OrderDeliveryService();
        Assert.Equal(-20, order.ResolveTotalWeightKg());
        Assert.Equal(-20, order.Deliveries.Sum(d => service.ResolveWeightKg(order, d)));
        var merged = service.MergePartialDeliveries(order);
        Assert.Equal(-20, service.ResolveWeightKg(order, merged));
    }

    [Theory]
    [InlineData(-2, -20)]
    [InlineData(0, -20)]
    [InlineData(-2, 0)]
    public void PartialDeliveriesPreserveNegativeWeightsAcrossProductsAndTours(double unitWeight, double totalWeight)
    {
        var order = Order();
        order.ManualTotalWeightKg = null;
        order.Products.Add(new() { PositionId = "N", Name = "Gewichtsabzug", Quantity = 10,
            UnitWeightKg = unitWeight, WeightKg = totalWeight });
        order.Deliveries[0].Lines.Add(new() { OrderProductPositionId = "N", Quantity = 3 });
        order.Deliveries[1].Lines.Add(new() { OrderProductPositionId = "N", Quantity = 7 });
        Assert.Equal(9, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D1"));
        Assert.Equal(21, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D2"));
        Assert.Equal(30, order.ResolveTotalWeightKg());
        Assert.Equal(-6, OrderDeliveryWeightService.ResolveStopProducts(order, "D1").Single(p => p.PositionId == "N").WeightKg);
        Assert.Equal(-14, OrderDeliveryWeightService.ResolveStopProducts(order, "D2").Single(p => p.PositionId == "N").WeightKg);
        var tour = new TourRecord { Id = 7, Stops = [
            new() { Auftragsnummer = "A", DeliveryId = "D1" },
            new() { Auftragsnummer = "A", DeliveryId = "D1" }
        ] };
        Assert.Equal(9, OrderDeliveryWeightService.ResolveTourOrderWeightKg(tour, [order]));
        order.Deliveries[1].AssignedTourId = "7";
        tour.Stops.Add(new() { Auftragsnummer = "A", DeliveryId = "D2" });
        Assert.Equal(30, OrderDeliveryWeightService.ResolveTourOrderWeightKg(tour, [order]));
        Assert.Equal(30, OrderDeliveryWeightService.ResolveStopWeightKg(order, null, "7"));
    }

    [Fact]
    public void NegativeOnlyPartialDeliveryRetainsItsSignedWeight()
    {
        var order = Order();
        order.Products[0].UnitWeightKg = -2;
        order.Products[0].WeightKg = -20;
        Assert.Equal(-6, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D1"));
        Assert.Equal(-14, OrderDeliveryWeightService.ResolveStopWeightKg(order, "D2"));
    }

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
