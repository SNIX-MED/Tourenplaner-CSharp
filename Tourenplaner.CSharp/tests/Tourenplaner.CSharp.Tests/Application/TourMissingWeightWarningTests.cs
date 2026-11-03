using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourMissingWeightWarningTests
{
    [Fact]
    public void LegacyStopChecksOnlyDeliveriesAssignedToItsTour()
    {
        var order = new Order { Id = "A", Products = [
            new() { PositionId = "P1", Name = "Hier" }, new() { PositionId = "P2", Name = "Andere Tour" }],
            Deliveries = [new() { Id = "D1", AssignedTourId = "7", Lines = [new() { OrderProductPositionId = "P1", Quantity = -2 }] },
                new() { Id = "D2", AssignedTourId = "8", Lines = [new() { OrderProductPositionId = "P2", Quantity = 1 }] }] };
        var tour = new TourRecord { Id = 7, Stops = [new() { Auftragsnummer = "A" }] };
        var message = TourMissingWeightWarning.BuildMessage(tour, [order]);
        Assert.Contains("Hier (Position 1)", message);
        Assert.DoesNotContain("Andere Tour", message);
        order.Deliveries[0].Status = OrderDeliveryStatus.Cancelled;
        Assert.Empty(TourMissingWeightWarning.BuildMessage(tour, [order]));
    }

    [Fact]
    public void LegacyProductsWithoutPositionIdsKeepCorrectPositionNumbers()
    {
        var order = new Order { Id = "A", Products = [new() { Name = "Erstes" }, new() { Name = "Zweites" }] };
        var tour = new TourRecord { Stops = [new() { Auftragsnummer = "A" }] };
        var message = TourMissingWeightWarning.BuildMessage(tour, [order]);
        Assert.Contains("Erstes (Position 1)", message);
        Assert.Contains("Zweites (Position 2)", message);
    }

    [Fact]
    public void WithoutExplicitConfirmationMissingWeightsDoNotAllowSaving()
    {
        var order = new Order { Id = "A", Products = [new() { Name = "Produkt" }] };
        var tour = new TourRecord { Stops = [new() { Auftragsnummer = "A" }] };
        Assert.False(TourMissingWeightWarning.Confirm(tour, [order]));
        order.Products[0].WeightKg = -3;
        Assert.True(TourMissingWeightWarning.Confirm(tour, [order]));
    }

    [Fact]
    public void WarningListsOnlyAllocatedMissingProductsOnce()
    {
        var order = new Order { Id = "222132", CustomerName = "Kunde", Products = [
            new() { PositionId = "Z", Name = "Ohne Gewicht", Quantity = 2 },
            new() { PositionId = "N", Name = "Negativ", Quantity = 2, UnitWeightKg = -3, WeightKg = -6 },
            new() { PositionId = "X", Name = "Andere Lieferung", Quantity = 1 }],
            Deliveries = [new() { Id = "D1", Lines = [new() { OrderProductPositionId = "Z", Quantity = 1 },
                new() { OrderProductPositionId = "N", Quantity = 1 }] },
                new() { Id = "D2", Lines = [new() { OrderProductPositionId = "X", Quantity = 1 }] }] };
        var tour = new TourRecord { Id = 1, Stops = [new() { Auftragsnummer = order.Id, DeliveryId = "D1" },
            new() { Auftragsnummer = order.Id, DeliveryId = "D1" }] };
        var message = TourMissingWeightWarning.BuildMessage(tour, [order]);
        Assert.Contains("222132 – Kunde", message);
        Assert.Contains("Ohne Gewicht (Position 1)", message);
        Assert.Equal(1, message.Split("Ohne Gewicht").Length - 1);
        Assert.DoesNotContain("Andere Lieferung", message);
        Assert.DoesNotContain("Negativ", message);
        order.Products[0].WeightKg = 5;
        Assert.Empty(TourMissingWeightWarning.BuildMessage(tour, [order]));
    }

    [Fact]
    public void WholeOrdersWarnForZeroButNotNegativeOrLegacyTotalWeights()
    {
        var order = new Order { Id = "A", Products = [new() { PositionId = "P", Name = "Produkt" }] };
        var tour = new TourRecord { Stops = [new() { Auftragsnummer = "A" }] };
        Assert.NotEmpty(TourMissingWeightWarning.BuildMessage(tour, [order]));
        order.Products[0].UnitWeightKg = -2;
        Assert.Empty(TourMissingWeightWarning.BuildMessage(tour, [order]));
        order.Products[0].UnitWeightKg = 0;
        order.Products[0].WeightKg = 4;
        Assert.Empty(TourMissingWeightWarning.BuildMessage(tour, [order]));
        order.Products[0].WeightKg = 0;
        tour.Stops[0].StopKind = "manual";
        Assert.Empty(TourMissingWeightWarning.BuildMessage(tour, [order]));
    }
}
