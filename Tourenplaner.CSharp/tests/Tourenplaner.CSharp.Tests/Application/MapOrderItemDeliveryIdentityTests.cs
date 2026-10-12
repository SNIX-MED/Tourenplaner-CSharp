using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class MapOrderItemDeliveryIdentityTests
{
    [Fact]
    public void PartialDeliveriesOfSameOrder_HaveDifferentMarkerIds()
    {
        var first = new MapOrderItem { OrderId = "4711", DeliveryId = "D1" };
        var second = new MapOrderItem { OrderId = "4711", DeliveryId = "D2" };

        Assert.NotEqual(first.MarkerId, second.MarkerId);
        Assert.StartsWith("4711::delivery::", first.MarkerId);
    }

    [Fact]
    public void LegacyOrder_UsesOrderIdAsMarkerId()
    {
        var item = new MapOrderItem { OrderId = "4711" };

        Assert.Equal("4711", item.MarkerId);
    }
}
