using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderStatusDerivationTests
{
    [Fact]
    public void DeliveryStatusOptions_ContainPendingPreparation()
    {
        Assert.Contains(OrderProductInfo.PendingPreparationStatus, OrderProductInfo.DeliveryStatusOptions);
    }

    [Fact]
    public void NormalizeDeliveryStatus_NormalizesPendingPreparation()
    {
        var normalized = OrderProductInfo.NormalizeDeliveryStatus("zu richten");

        Assert.Equal(OrderProductInfo.PendingPreparationStatus, normalized);
    }

    [Fact]
    public void ResolveOrderStatusFromProducts_DerivesPendingPreparationStatuses()
    {
        var allPendingPreparation = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.PendingPreparationStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.PendingPreparationStatus }
        };

        var mixedReadyFamily = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.PendingPreparationStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.OrderedStatus }
        };

        var mixedReadyAndPendingPreparation = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.PendingPreparationStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.InStockStatus }
        };

        Assert.Equal(Order.PendingPreparationStatus, Order.ResolveOrderStatusFromProducts(allPendingPreparation));
        Assert.Equal(Order.PartiallyPendingPreparationStatus, Order.ResolveOrderStatusFromProducts(mixedReadyFamily));
        Assert.Equal(Order.PartiallyPendingPreparationStatus, Order.ResolveOrderStatusFromProducts(mixedReadyAndPendingPreparation));
    }

    [Fact]
    public void ResolvePartiallyPendingPreparationBaseStatus_PrefersInStockOverPendingPreparation()
    {
        var products = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.PendingPreparationStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.InStockStatus }
        };

        var baseStatus = Order.ResolvePartiallyPendingPreparationBaseStatus(products);

        Assert.Equal(Order.ReadyToDeliverStatus, baseStatus);
    }

    [Fact]
    public void ResolvePartiallyPendingPreparationBaseStatus_UsesInTransitWhenMixedWithPendingPreparation()
    {
        var products = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.PendingPreparationStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.InTransitStatus }
        };

        var baseStatus = Order.ResolvePartiallyPendingPreparationBaseStatus(products);

        Assert.Equal(Order.InTransitStatus, baseStatus);
    }

    [Fact]
    public void ResolvePartiallyPendingPreparationBaseStatus_UsesOrderedWhenMixedWithPendingPreparation()
    {
        var products = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.PendingPreparationStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.OrderedStatus }
        };

        var baseStatus = Order.ResolvePartiallyPendingPreparationBaseStatus(products);

        Assert.Equal(Order.OrderedStatus, baseStatus);
    }
}
