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

    [Fact]
    public void AlternativeDeliveryOption_UsesOnlyTheOppositeDispatchChannel()
    {
        var mapViewModel = new ManualOrderDialogViewModel(CreateOrder());
        Assert.True(mapViewModel.ShowAlternativeDeliveryOption);
        Assert.Equal("Evtl. Spediteur", mapViewModel.AlternativeDeliveryLabel);

        mapViewModel.IsAlternativeDeliveryEnabled = true;
        Assert.True(mapViewModel.TryBuildOrder(out var mapOrder, out var mapError), mapError);
        Assert.True(mapOrder!.IsAlternativeDeliveryEnabled);
        Assert.True(DeliveryMethodExtensions.CanUseLiefertour(mapOrder));
        Assert.True(DeliveryMethodExtensions.CanUseSpediteurView(mapOrder));

        mapViewModel.SelectedDeliveryType = DeliveryMethodExtensions.Spediteur;
        Assert.False(mapViewModel.IsAlternativeDeliveryEnabled);
        Assert.Equal("Evtl. Liefertour", mapViewModel.AlternativeDeliveryLabel);
        mapViewModel.IsAlternativeDeliveryEnabled = true;
        Assert.True(mapViewModel.TryBuildOrder(out var carrierOrder, out var carrierError), carrierError);
        Assert.True(DeliveryMethodExtensions.CanUseLiefertour(carrierOrder!));
        Assert.True(DeliveryMethodExtensions.CanUseSpediteurView(carrierOrder!));

        mapViewModel.SelectedDeliveryType = DeliveryMethodExtensions.Post;
        Assert.False(mapViewModel.ShowAlternativeDeliveryOption);
        Assert.False(mapViewModel.IsAlternativeDeliveryEnabled);
        Assert.True(mapViewModel.TryBuildOrder(out var postOrder, out var postError), postError);
        Assert.False(postOrder!.IsAlternativeDeliveryEnabled);
        Assert.False(DeliveryMethodExtensions.CanUseLiefertour(postOrder));
        Assert.True(DeliveryMethodExtensions.CanUseSpediteurView(postOrder));
    }

    [Fact]
    public void AlternativeDeliveryOption_IsAvailableForLockedXmlOrder()
    {
        var source = CreateOrder();
        source.IsXmlImported = true;
        source.IsAlternativeDeliveryEnabled = true;

        var viewModel = new ManualOrderDialogViewModel(source);

        Assert.False(viewModel.IsEditingEnabled);
        Assert.True(viewModel.ShowAlternativeDeliveryOption);
        Assert.True(viewModel.IsAlternativeDeliveryEnabled);
        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.True(order!.IsAlternativeDeliveryEnabled);
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
