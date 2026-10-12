using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderDeliveryDialogViewModelTests
{
    [Fact]
    public void NewPartialDelivery_HasNoRequiredDate()
    {
        var order = new Order
        {
            ScheduledDate = new DateOnly(2026, 10, 12),
            DeliveryDate = new DateOnly(2026, 10, 20),
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };

        var viewModel = new OrderDeliveryDialogViewModel(order);

        Assert.Null(viewModel.PlannedDate);
    }

    [Fact]
    public void SelectingProduct_EnablesDefaultQuantityAndBuildsLine()
    {
        var order = new Order
        {
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };
        var viewModel = new OrderDeliveryDialogViewModel(order);

        viewModel.Lines[0].IsSelected = true;

        Assert.Equal("1", viewModel.Lines[0].QuantityText);
        Assert.True(viewModel.TryBuild(out var lines, out _));
        Assert.Equal(1, Assert.Single(lines).Quantity);
    }

    [Fact]
    public void QuantityAboveRemainder_IsRejected()
    {
        var order = new Order
        {
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };
        var viewModel = new OrderDeliveryDialogViewModel(order);
        viewModel.Lines[0].IsSelected = true;
        viewModel.Lines[0].QuantityText = "4";

        Assert.False(viewModel.TryBuild(out _, out var error));
        Assert.Contains("Restmenge", error);
    }

    [Fact]
    public void InvalidQuantityText_IsRejected()
    {
        var order = new Order
        {
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };
        var viewModel = new OrderDeliveryDialogViewModel(order);
        viewModel.Lines[0].IsSelected = true;
        viewModel.Lines[0].QuantityText = "abc";

        Assert.False(viewModel.TryBuild(out _, out _));
    }
}
