using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class ManualOrderDialogViewModelTests
{
    [Fact]
    public void Constructor_TreatsMinValueDeliveryDateAsEmpty()
    {
        var order = CreateOrder();
        order.DeliveryDate = DateOnly.MinValue;

        var viewModel = new ManualOrderDialogViewModel(order);

        Assert.Equal(string.Empty, viewModel.DeliveryDateText);
    }

    [Fact]
    public void TryBuildOrder_KeepsEmptyDeliveryDateNull()
    {
        var viewModel = new ManualOrderDialogViewModel(CreateOrder())
        {
            DeliveryDateText = string.Empty
        };

        var success = viewModel.TryBuildOrder(out var order, out var error);

        Assert.True(success, error);
        Assert.NotNull(order);
        Assert.Null(order.DeliveryDate);
    }

    private static Order CreateOrder()
    {
        return new Order
        {
            Id = "A-1",
            ScheduledDate = new DateOnly(2026, 3, 8),
            DeliveryDate = null,
            Type = OrderType.Map,
            CustomerName = "Muster AG",
            DeliveryType = DeliveryMethodExtensions.FreiBordsteinkante,
            OrderStatus = Order.DefaultOrderStatus,
            OrderAddress = new OrderAddressInfo
            {
                Name = "Muster AG",
                Street = "Woelferstrasse",
                HouseNumber = "8",
                PostalCode = "4414",
                City = "Fuellinsdorf"
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = "Muster AG",
                Street = "Woelferstrasse",
                HouseNumber = "8",
                PostalCode = "4414",
                City = "Fuellinsdorf"
            },
            Products =
            [
                new OrderProductInfo
                {
                    Name = "Produkt A",
                    Quantity = 1,
                    UnitWeightKg = 1,
                    WeightKg = 1,
                    DeliveryStatus = OrderProductInfo.OrderedStatus
                }
            ]
        };
    }
}
