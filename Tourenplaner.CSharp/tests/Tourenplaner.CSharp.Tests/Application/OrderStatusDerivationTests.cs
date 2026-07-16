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
    public void ResolveOrderStatusFromProducts_KeepsNotSpecifiedProductStatus()
    {
        var notSpecifiedProducts = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.DefaultDeliveryStatus }
        };

        var mixedWithOrderedProducts = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.DefaultDeliveryStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.OrderedStatus }
        };

        var mixedWithPendingPreparationProducts = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.DefaultDeliveryStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.PendingPreparationStatus }
        };

        var mixedWithInStockProducts = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.DefaultDeliveryStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.InStockStatus }
        };

        var mixedWithInTransitProducts = new[]
        {
            new OrderProductInfo { Name = "Produkt A", DeliveryStatus = OrderProductInfo.DefaultDeliveryStatus },
            new OrderProductInfo { Name = "Produkt B", DeliveryStatus = OrderProductInfo.InTransitStatus }
        };

        Assert.Equal(Order.DefaultOrderStatus, Order.ResolveOrderStatusFromProducts(notSpecifiedProducts));
        Assert.Equal(Order.DefaultOrderStatus, Order.ResolveOrderStatusFromProducts(mixedWithOrderedProducts));
        Assert.Equal(Order.DefaultOrderStatus, Order.ResolveOrderStatusFromProducts(mixedWithPendingPreparationProducts));
        Assert.Equal(Order.DefaultOrderStatus, Order.ResolveOrderStatusFromProducts(mixedWithInStockProducts));
        Assert.Equal(Order.DefaultOrderStatus, Order.ResolveOrderStatusFromProducts(mixedWithInTransitProducts));
    }

    [Fact]
    public void DefaultNotSpecifiedStatusColor_IsWhite()
    {
        Assert.Equal("#FFFFFF", AppSettings.DefaultStatusColorNotSpecified);
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
