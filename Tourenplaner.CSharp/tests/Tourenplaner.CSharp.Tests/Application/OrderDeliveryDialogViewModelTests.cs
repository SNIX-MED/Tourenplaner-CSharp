using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderDeliveryDialogViewModelTests
{
    [Fact]
    public void NewPartialDelivery_PrefillsBothDatesFromMainDeliveryDate()
    {
        var order = new Order
        {
            ScheduledDate = new DateOnly(2026, 10, 12),
            DeliveryDate = new DateOnly(2026, 10, 20),
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };

        var viewModel = new OrderDeliveryDialogViewModel(order);

        Assert.Equal(new DateTime(2026, 10, 20), viewModel.Delivery1PlannedDate);
        Assert.Equal(new DateTime(2026, 10, 20), viewModel.Delivery2PlannedDate);
    }

    [Fact]
    public void NewPartialDelivery_DoesNotUseOrderDateAsDeliveryDate()
    {
        var order = new Order
        {
            ScheduledDate = new DateOnly(2026, 10, 12),
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };

        var viewModel = new OrderDeliveryDialogViewModel(order);

        Assert.Null(viewModel.Delivery1PlannedDate);
        Assert.Null(viewModel.Delivery2PlannedDate);
    }

    [Fact]
    public void NewPartialDelivery_PrefillsBothDeliveryNotesFromOrderNote()
    {
        var order = new Order
        {
            Notes = "Hauptauftragsnotiz",
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };

        var viewModel = new OrderDeliveryDialogViewModel(order);

        Assert.Equal("Hauptauftragsnotiz", viewModel.Delivery1Notes);
        Assert.Equal("Hauptauftragsnotiz", viewModel.Delivery2Notes);
    }

    [Fact]
    public void SelectingProduct_UsesFullRemainingQuantityAndBuildsLine()
    {
        var order = new Order
        {
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 3 }]
        };
        var viewModel = new OrderDeliveryDialogViewModel(order);

        viewModel.Lines[0].IsSelected = true;

        Assert.Equal("3", viewModel.Lines[0].QuantityText);
        Assert.True(viewModel.TryBuild(out var lines, out _));
        Assert.Equal(3, Assert.Single(lines).Quantity);
    }

    [Fact]
    public void SelectingProduct_UsesRemainingQuantityInsteadOfOriginalOrderQuantity()
    {
        var order = new Order
        {
            Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 10 }],
            Deliveries =
            [
                new OrderDelivery
                {
                    Id = "D1",
                    SequenceNumber = 1,
                    Lines = [new OrderDeliveryLine { OrderProductPositionId = "P1", Quantity = 4 }]
                }
            ]
        };
        var viewModel = new OrderDeliveryDialogViewModel(order);

        viewModel.Lines[0].IsSelected = true;

        Assert.Equal("6", viewModel.Lines[0].QuantityText);
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
