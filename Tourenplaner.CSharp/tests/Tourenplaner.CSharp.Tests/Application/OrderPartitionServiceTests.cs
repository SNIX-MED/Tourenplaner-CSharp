using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderPartitionServiceTests
{
    [Fact]
    public void MergeMapOrders_PreservesNonMapOrders()
    {
        var service = new OrderPartitionService();
        var existing = new[]
        {
            new Order { Id = "M1", Type = OrderType.Map, CustomerName = "Map A" },
            new Order { Id = "N1", Type = OrderType.NonMap, CustomerName = "NonMap A" }
        };
        var updatedMap = new[]
        {
            new Order { Id = "M2", Type = OrderType.Map, CustomerName = "Map B" }
        };

        var merged = service.MergeMapOrders(existing, updatedMap);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, o => o.Id == "N1" && o.Type == OrderType.NonMap);
        Assert.Contains(merged, o => o.Id == "M2" && o.Type == OrderType.Map);
    }

    [Fact]
    public void MergeMapOrders_ReclassifiesUpdatedOrderFromDeliveryType()
    {
        var service = new OrderPartitionService();
        var existing = new[]
        {
            new Order { Id = "N1", Type = OrderType.NonMap, CustomerName = "NonMap A" }
        };
        var updatedMap = new[]
        {
            new Order
            {
                Id = "M1",
                Type = OrderType.Map,
                CustomerName = "Map A",
                DeliveryType = DeliveryMethodExtensions.Spediteur,
                Location = new GeoPoint(47.3, 8.5),
                AssignedTourId = "10"
            }
        };

        var merged = service.MergeMapOrders(existing, updatedMap);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, o => o.Id == "N1" && o.Type == OrderType.NonMap);
        Assert.Contains(merged, o =>
            o.Id == "M1" &&
            o.Type == OrderType.NonMap &&
            o.Location is null &&
            string.IsNullOrWhiteSpace(o.AssignedTourId));
    }

    [Fact]
    public void MergeNonMapOrders_PreservesMapOrders_AndClearsLocation()
    {
        var service = new OrderPartitionService();
        var existing = new[]
        {
            new Order
            {
                Id = "M1",
                Type = OrderType.Map,
                CustomerName = "Map A",
                Location = new GeoPoint(47.3, 8.5)
            }
        };
        var updatedNonMap = new[]
        {
            new Order
            {
                Id = "N1",
                Type = OrderType.NonMap,
                CustomerName = "NonMap A",
                DeliveryType = DeliveryMethodExtensions.Spediteur,
                Location = new GeoPoint(47.1, 8.1)
            }
        };

        var merged = service.MergeNonMapOrders(existing, updatedNonMap);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, o => o.Id == "M1" && o.Type == OrderType.Map);
        Assert.Contains(merged, o => o.Id == "N1" && o.Type == OrderType.NonMap && o.Location is null);
    }

    [Fact]
    public void MergeNonMapOrders_ReclassifiesUpdatedOrderFromDeliveryType()
    {
        var service = new OrderPartitionService();
        var existing = new[]
        {
            new Order { Id = "M1", Type = OrderType.Map, CustomerName = "Map A" }
        };
        var updatedNonMap = new[]
        {
            new Order
            {
                Id = "N1",
                Type = OrderType.NonMap,
                CustomerName = "NonMap A",
                DeliveryType = DeliveryMethodExtensions.MitVerteilung
            }
        };

        var merged = service.MergeNonMapOrders(existing, updatedNonMap);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, o => o.Id == "M1" && o.Type == OrderType.Map);
        Assert.Contains(merged, o => o.Id == "N1" && o.Type == OrderType.Map);
    }
}
