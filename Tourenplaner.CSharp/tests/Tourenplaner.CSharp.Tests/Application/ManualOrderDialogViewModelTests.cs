using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class ManualOrderDialogViewModelTests
{
    [Fact]
    public void DeliveryPeriod_LoadsAndSavesFreeTextWithoutDeliveryDate()
    {
        var source = CreateOrder();
        source.DeliveryPeriod = "KW 43–44";
        var viewModel = new ManualOrderDialogViewModel(source);
        Assert.Equal("KW 43–44", viewModel.DeliveryPeriod);
        Assert.True(viewModel.TryBuildOrder(out var unchanged, out var error), error);
        Assert.Equal("KW 43–44", unchanged!.DeliveryPeriod);

        viewModel.DeliveryPeriod = "  Ende Oktober  ";
        viewModel.DeliveryDateText = string.Empty;
        Assert.True(viewModel.TryBuildOrder(out var edited, out error), error);
        Assert.Equal("Ende Oktober", edited!.DeliveryPeriod);
        Assert.Null(edited.DeliveryDate);
    }

    [Fact]
    public void Constructor_TreatsMinValueDeliveryDateAsEmpty()
    {
        var order = CreateOrder();
        order.DeliveryDate = DateOnly.MinValue;

        var viewModel = new ManualOrderDialogViewModel(order);

        Assert.Equal(string.Empty, viewModel.DeliveryDateText);
    }

    [Theory]
    [InlineData(DeliveryMethodExtensions.MitVerteilung)]
    [InlineData(DeliveryMethodExtensions.Spediteur)]
    public void SelfPickupAlternative_IsAvailableForMapAndNonMapOrders(string deliveryType)
    {
        var source = CreateOrder();
        source.DeliveryType = deliveryType;
        var viewModel = new ManualOrderDialogViewModel(source);

        Assert.True(viewModel.ShowSelfPickupAlternativeOption);
        viewModel.IsSelfPickupAlternativeEnabled = true;

        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.True(order!.IsSelfPickupAlternativeEnabled);
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
    public void ClearingDeliveryDate_DisablesEarlierDeliveryAndPersistsNoDate()
    {
        var source = CreateOrder();
        source.DeliveryDate = new DateOnly(2026, 9, 25);
        source.DeliveryCanOccurEarlier = true;
        var viewModel = new ManualOrderDialogViewModel(source);

        viewModel.DeliveryDateText = string.Empty;

        Assert.False(viewModel.DeliveryCanOccurEarlier);
        Assert.False(viewModel.CanEditDeliveryCanOccurEarlier);
        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.Null(order!.DeliveryDate);
        Assert.False(order.DeliveryCanOccurEarlier);
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
        viewModel.Notes = "Manuell ergänzte Notiz";
        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.True(order!.IsAlternativeDeliveryEnabled);
        Assert.Equal("Manuell ergänzte Notiz", order.Notes);
    }

    [Fact]
    public void ProductStatus_CanBeChangedForLockedXmlOrder()
    {
        var source = CreateOrder();
        source.IsXmlImported = true;
        var viewModel = new ManualOrderDialogViewModel(source);
        var product = Assert.Single(viewModel.ProductLines);
        viewModel.SetSelectedProductLines([product]);

        Assert.False(viewModel.IsEditingEnabled);
        Assert.True(viewModel.CanChangeSelectedProductStatus);

        viewModel.SelectedProductDeliveryStatus = OrderProductInfo.PendingPreparationStatus;

        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.Equal(
            OrderProductInfo.PendingPreparationStatus,
            Assert.Single(order!.Products).DeliveryStatus);
    }

    [Fact]
    public void ManualTotalWeight_IsLoadedAndSaved()
    {
        var source = CreateOrder();
        source.ManualTotalWeightKg = 125.5d;
        var viewModel = new ManualOrderDialogViewModel(source);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.ManualTotalWeightKgText));
        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.Equal(125.5d, order!.ManualTotalWeightKg);
        Assert.Equal(125.5d, order.ResolveTotalWeightKg());
    }

    [Fact]
    public void EmptyManualTotalWeight_UsesProductWeights()
    {
        var viewModel = new ManualOrderDialogViewModel(CreateOrder())
        {
            ManualTotalWeightKgText = string.Empty
        };

        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.Null(order!.ManualTotalWeightKg);
        Assert.Equal(1d, order.ResolveTotalWeightKg());
    }

    [Fact]
    public void EditingOtherOrderData_PreservesManualPinAndItsMetadata()
    {
        var source = CreateOrder();
        source.Location = new GeoPoint(47.271092, 8.741467);
        source.IsLocationManuallySet = true;
        source.ManualLocationAddress = "Woelferstrasse 8, 4414 Fuellinsdorf";
        var viewModel = new ManualOrderDialogViewModel(source)
        {
            Notes = "Neue Notiz"
        };

        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.Equal(source.Location, order!.Location);
        Assert.True(order.IsLocationManuallySet);
        Assert.Equal(source.ManualLocationAddress, order.ManualLocationAddress);
        Assert.False(order.ManualLocationRequiresReview);
    }

    [Fact]
    public void ChangingDeliveryAddress_PreservesManualPinAndMarksItForReview()
    {
        var source = CreateOrder();
        source.Location = new GeoPoint(47.271092, 8.741467);
        source.IsLocationManuallySet = true;
        source.ManualLocationAddress = "Woelferstrasse 8, 4414 Fuellinsdorf";
        var viewModel = new ManualOrderDialogViewModel(source)
        {
            DeliveryStreet = "Neue Strasse",
            DeliveryHouseNumber = "10"
        };

        Assert.True(viewModel.TryBuildOrder(out var order, out var error), error);
        Assert.Equal(source.Location, order!.Location);
        Assert.True(order.IsLocationManuallySet);
        Assert.Equal(source.ManualLocationAddress, order.ManualLocationAddress);
        Assert.True(order.ManualLocationRequiresReview);
    }

    [Fact]
    public void SupplierSelection_FiltersProductListImmediately()
    {
        var source = CreateOrder();
        source.Products =
        [
            new OrderProductInfo { Name = "Produkt A", Supplier = "Lieferant A", Quantity = 1 },
            new OrderProductInfo { Name = "Produkt B", Supplier = "Lieferant B", Quantity = 1 }
        ];
        var viewModel = new ManualOrderDialogViewModel(source);
        var supplierA = Assert.Single(viewModel.SupplierFilterItems, item => item.Label == "Lieferant A");

        supplierA.IsSelected = false;

        var visibleProduct = Assert.Single(viewModel.FilteredProductLines.Cast<ProductLineInput>());
        Assert.Equal("Produkt B", visibleProduct.Name);
    }

    [Fact]
    public void TryBuildOrder_PreservesSeparateDeliveryNotes()
    {
        var source = CreateOrder();
        source.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Notes = "Notiz eins" },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Notes = "Notiz zwei" }
        ];
        var viewModel = new ManualOrderDialogViewModel(source);
        viewModel.DeliveryNotes[1].Notes = "Geänderte Notiz zwei";

        Assert.True(viewModel.TryBuildOrder(out var result, out var error), error);
        Assert.NotNull(result);
        Assert.Equal("Notiz eins", result!.Deliveries[0].Notes);
        Assert.Equal("Geänderte Notiz zwei", result.Deliveries[1].Notes);
    }

    [Fact]
    public void ExistingPartialDeliveries_AreShownAsSeparateProductGroups()
    {
        var source = CreateOrder();
        source.Products =
        [
            new OrderProductInfo { PositionId = "P1", Name = "Regal", Quantity = 3, UnitWeightKg = 10 }
        ];
        source.Deliveries =
        [
            new OrderDelivery
            {
                Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Delivered,
                Lines = [new OrderDeliveryLine { OrderProductPositionId = "P1", Quantity = 1 }]
            },
            new OrderDelivery
            {
                Id = "D2", SequenceNumber = 2, Status = OrderDeliveryStatus.ReadyForPlanning,
                Lines = [new OrderDeliveryLine { OrderProductPositionId = "P1", Quantity = 2 }]
            }
        ];

        var viewModel = new ManualOrderDialogViewModel(source);

        Assert.Equal(2, viewModel.DeliveryProductGroups.Count);
        Assert.Equal("Produkte Lieferung 1/2", viewModel.DeliveryProductGroups[0].Heading);
        Assert.Equal("Geliefert", viewModel.DeliveryProductGroups[0].Status);
        /* Legacy string assertions replaced by product-card assertions.
        Assert.Equal("1× Regal", Assert.Single(viewModel.DeliveryProductGroups[0].ProductLines));
        Assert.Equal("Produkte Lieferung 2/2", viewModel.DeliveryProductGroups[1].Heading);
        Assert.Equal("Offen", viewModel.DeliveryProductGroups[1].Status);
        Assert.Equal("2× Regal", Assert.Single(viewModel.DeliveryProductGroups[1].ProductLines));
        */
        var firstProduct = Assert.Single(viewModel.DeliveryProductGroups[0].ProductLines);
        Assert.Equal("Regal", firstProduct.Name);
        Assert.Equal(1, firstProduct.Quantity);
        Assert.Equal("Produkte Lieferung 2/2", viewModel.DeliveryProductGroups[1].Heading);
        Assert.Equal("Offen", viewModel.DeliveryProductGroups[1].Status);
        var secondProduct = Assert.Single(viewModel.DeliveryProductGroups[1].ProductLines);
        Assert.Equal("Regal", secondProduct.Name);
        Assert.Equal(2, secondProduct.Quantity);
    }

    [Theory]
    [InlineData("Auftragsnotiz", "", "Auftragsnotiz")]
    [InlineData("", "Liefernotiz", "Liefernotiz")]
    [InlineData("Identisch", "Identisch", "Identisch")]
    [InlineData("", "Lieferung 1/1: Liefernotiz", "Liefernotiz")]
    public void SingleInternalDeliveryUsesNormalNotesAndPreservesAssignment(string notes, string deliveryNotes, string expected)
    {
        var source = CreateOrder();
        source.Notes = notes;
        source.Products[0].PositionId = "P1";
        source.Deliveries = [new OrderDelivery
        {
            Id = "D1", SequenceNumber = 1, AssignedTourId = "42", Notes = deliveryNotes,
            Lines = [new OrderDeliveryLine { OrderProductPositionId = "P1", Quantity = 1 }]
        }];
        var vm = new ManualOrderDialogViewModel(source);
        Assert.Empty(vm.DeliveryNotes);
        Assert.Empty(vm.DeliveryProductGroups);
        Assert.Equal(expected, vm.Notes);
        Assert.True(vm.TryBuildOrder(out var result, out var error), error);
        Assert.Equal(expected, result!.Notes);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal("D1", delivery.Id);
        Assert.Equal("42", delivery.AssignedTourId);
        Assert.Empty(delivery.Notes);
        Assert.Equal("P1", Assert.Single(delivery.Lines).OrderProductPositionId);
        Assert.Equal(notes, source.Notes);
        Assert.Equal(deliveryNotes, source.Deliveries[0].Notes);
    }

    [Fact]
    public void DistinctSingleDeliveryAndOrderNotesBothSurviveRoundTrip()
    {
        var source = CreateOrder();
        source.Notes = "Auftragsnotiz";
        source.Deliveries = [new OrderDelivery { Id = "D1", Notes = "Liefernotiz" }];
        var vm = new ManualOrderDialogViewModel(source);
        Assert.True(vm.TryBuildOrder(out var result, out var error), error);
        Assert.Equal($"Auftragsnotiz{Environment.NewLine}Liefernotiz", result!.Notes);
        var reopened = new ManualOrderDialogViewModel(result);
        Assert.Equal(result.Notes, reopened.Notes);
    }

    [Fact]
    public void SplitOrderKeepsGeneralNotesAndPartHeadings()
    {
        var source = CreateOrder();
        source.Notes = "Allgemeine Notiz";
        source.Deliveries = [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Delivered, Notes = "Teil eins" },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Notes = "Teil zwei" }
        ];
        var vm = new ManualOrderDialogViewModel(source);
        Assert.Equal("Notizen Lieferung Teil 1", vm.DeliveryNotes[0].Heading);
        Assert.Equal("Notizen Lieferung Teil 2", vm.DeliveryNotes[1].Heading);
        Assert.True(vm.TryBuildOrder(out var result, out var error), error);
        Assert.Equal("Allgemeine Notiz", result!.Notes);
        Assert.Equal("Teil eins", result.Deliveries[0].Notes);
        Assert.Equal(OrderDeliveryStatus.Delivered, result.Deliveries[0].Status);
        Assert.Equal("Teil zwei", result.Deliveries[1].Notes);
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
