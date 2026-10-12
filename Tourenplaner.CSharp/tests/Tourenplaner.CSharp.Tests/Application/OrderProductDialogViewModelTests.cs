using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderProductDialogViewModelTests
{
    [Fact]
    public void ExpectedDelivery_IsLoadedTrimmedAndSavedWithProduct()
    {
        var source = new ProductLineInput
        {
            Name = "Schrank",
            Supplier = "Lieferant AG",
            ExpectedDelivery = "KW 42",
            Quantity = 2,
            UnitWeightKg = 10,
            DeliveryStatus = OrderProductInfo.OrderedStatus
        };
        var viewModel = new OrderProductDialogViewModel(source);

        Assert.Equal("KW 42", viewModel.ExpectedDelivery);
        viewModel.ExpectedDelivery = "  Mitte Oktober  ";

        Assert.True(viewModel.TryBuildResult(out var result, out var error), error);
        Assert.Equal("Mitte Oktober", result.ExpectedDelivery);
        Assert.Equal("Mitte Oktober", result.ToOrderProductInfo().ExpectedDelivery);
    }

    [Fact]
    public void NegativeQuantity_IsAcceptedAndPreserved()
    {
        var viewModel = new OrderProductDialogViewModel
        {
            Name = "Endkappe rechts",
            QuantityText = "-5",
            UnitWeightKgText = "2"
        };

        Assert.True(viewModel.TryBuildResult(out var result, out var error), error);
        Assert.Equal(-5, result.Quantity);

        var product = result.ToOrderProductInfo();
        Assert.Equal(-5, product.Quantity);
        Assert.Equal(-10d, product.WeightKg);
        Assert.Contains("-5x Endkappe rechts", viewModel.SummaryText);
    }

    [Fact]
    public void ZeroQuantity_IsRejected()
    {
        var viewModel = new OrderProductDialogViewModel
        {
            Name = "Endkappe rechts",
            QuantityText = "0",
            UnitWeightKgText = "2"
        };

        Assert.False(viewModel.TryBuildResult(out _, out var error));
        Assert.Contains("ungleich 0", error);
    }

    [Theory]
    [InlineData(OrderProductInfo.OrderedStatus, true)]
    [InlineData(OrderProductInfo.InTransitStatus, true)]
    [InlineData(OrderProductInfo.PendingPreparationStatus, true)]
    [InlineData(OrderProductInfo.InStockStatus, false)]
    public void ExpectedDelivery_IsHiddenOnlyWhenProductIsInStock(string status, bool expectedVisible)
    {
        Assert.Equal(
            expectedVisible,
            OrderProductInfo.ShouldShowExpectedDelivery("KW 42", status));
    }
}
