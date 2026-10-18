using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderProductFormatterTests
{
    [Fact]
    public void EmptyOrUnnamedProducts_ReturnNotAvailable()
    {
        Assert.Equal("N/A", OrderProductFormatter.BuildSummary(null));
        Assert.Equal("N/A", OrderProductFormatter.BuildDetails([]));
        Assert.Equal(
            "N/A",
            OrderProductFormatter.BuildSummary([new OrderProductInfo { Name = "   " }]));
    }

    [Fact]
    public void BuildSummary_PreservesProductOrderAndInvariantWeightFormatting()
    {
        var products = new[]
        {
            new OrderProductInfo
            {
                Name = " Produkt A ",
                Supplier = " Lieferant A ",
                Quantity = 2,
                UnitWeightKg = 1.25
            },
            new OrderProductInfo
            {
                Name = "Produkt B",
                Quantity = 0,
                WeightKg = 3.5
            }
        };

        var summary = OrderProductFormatter.BuildSummary(products);

        Assert.Equal("2x Produkt A (2.5 kg) [Lieferant A], 1x Produkt B (3.5 kg)", summary);
    }

    [Fact]
    public void BuildDetails_UsesCentralWeightsAndKeepsOptionalFieldOrder()
    {
        var product = new OrderProductInfo
        {
            Name = "Produkt A",
            Supplier = "Lieferant A",
            Dimensions = "10x20 cm",
            Quantity = 4,
            WeightKg = 10,
            DeliveryStatus = OrderProductInfo.InTransitStatus
        };

        var details = OrderProductFormatter.BuildDetails([product]);

        Assert.Equal(
            "4x Produkt A | 2.5 kg/Stk | Total: 10 kg | Lieferant: Lieferant A | Masse: 10x20 cm | Status: Auf dem Weg",
            details);
    }
}
