using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderImportTransformationTests
{
    [Fact]
    public void Mapper_PreservesProductOrderAndExistingPositionIdentityByIndex()
    {
        var generatedPositionIds = new Queue<string>(["NEW-2"]);
        var mapper = new OrderImportMapper(() => generatedPositionIds.Dequeue());
        var existing = new Order
        {
            Products =
            [
                new OrderProductInfo
                {
                    PositionId = "EXISTING-1",
                    ExternalPositionId = "EXT-1",
                    Supplier = "Alter Lieferant",
                    ExpectedDelivery = "Morgen",
                    Dimensions = "10x20",
                    DeliveryStatus = OrderProductInfo.InStockStatus
                }
            ]
        };
        var input = CreateInput("Frei Bordsteinkante");
        input.Produkte =
        [
            new XmlOrderProductData { Bezeichnung = "Erstes Produkt", Menge = 2, Gewicht = 3 },
            new XmlOrderProductData { Bezeichnung = "Zweites Produkt", Menge = 4, Gewicht = 5 }
        ];

        var mapped = mapper.Map(input, existing);

        Assert.Equal(OrderType.Map, mapped.Type);
        Assert.Collection(
            mapped.Products,
            first =>
            {
                Assert.Equal("Erstes Produkt", first.Name);
                Assert.Equal("EXISTING-1", first.PositionId);
                Assert.Equal("EXT-1", first.ExternalPositionId);
                Assert.Equal("Alter Lieferant", first.Supplier);
                Assert.Equal("Morgen", first.ExpectedDelivery);
                Assert.Equal("10x20", first.Dimensions);
                Assert.Equal(OrderProductInfo.InStockStatus, first.DeliveryStatus);
                Assert.Equal(6, first.WeightKg);
            },
            second =>
            {
                Assert.Equal("Zweites Produkt", second.Name);
                Assert.Equal("NEW-2", second.PositionId);
                Assert.Equal(20, second.WeightKg);
                Assert.Equal(OrderProductInfo.OrderedStatus, second.DeliveryStatus);
            });
    }

    [Theory]
    [InlineData("Selbstabholung", OrderType.NonMap)]
    [InlineData("Post", OrderType.NonMap)]
    [InlineData("Frei Bordsteinkante", OrderType.Map)]
    public void Mapper_UsesExistingDeliveryMethodRouting(string deliveryType, OrderType expectedType)
    {
        var mapped = new OrderImportMapper(() => "POSITION-1").Map(CreateInput(deliveryType));

        Assert.Equal(expectedType, mapped.Type);
        Assert.Equal(
            DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(deliveryType),
            mapped.DeliveryType);
    }

    [Fact]
    public void DifferenceAnalyzer_ClassifiesCreateUpdateAndUnchanged()
    {
        var imported = new Order { Id = "A-1", CustomerName = "Kunde" };
        var unchanged = new Order { Id = "A-1", CustomerName = "Kunde" };
        var changed = new Order { Id = "A-1", CustomerName = "Anderer Kunde" };

        var createDifference = OrderImportDifferenceAnalyzer.Analyze(null, imported);
        var unchangedDifference = OrderImportDifferenceAnalyzer.Analyze(unchanged, imported);
        var updateDifference = OrderImportDifferenceAnalyzer.Analyze(changed, imported);

        Assert.Equal(ImportPreviewAction.Create, createDifference.Action);
        Assert.Equal(["Neuer Auftrag wird angelegt."], createDifference.Changes);
        Assert.Equal(ImportPreviewAction.Unchanged, unchangedDifference.Action);
        Assert.Empty(unchangedDifference.Changes);
        Assert.Equal(ImportPreviewAction.Update, updateDifference.Action);
        Assert.Equal("Kunde: Anderer Kunde -> Kunde", Assert.Single(updateDifference.Changes));
    }

    [Fact]
    public void DifferenceAnalyzer_PreservesEstablishedChangeOrder()
    {
        var existing = new Order
        {
            CustomerName = "Alt",
            ScheduledDate = new DateOnly(2026, 1, 1),
            DeliveryPeriod = "Vormittag",
            Type = OrderType.NonMap,
            DeliveryType = "Selbstabholung",
            Notes = "Alte Notiz"
        };
        var imported = new Order
        {
            CustomerName = "Neu",
            ScheduledDate = new DateOnly(2026, 1, 2),
            DeliveryPeriod = "Nachmittag",
            Type = OrderType.Map,
            DeliveryType = "Frei Bordsteinkante",
            Notes = "Neue Notiz"
        };

        var difference = OrderImportDifferenceAnalyzer.Analyze(existing, imported);

        Assert.Equal(
            ["Kunde", "Termin", "Lieferzeitraum", "Typ", "Lieferart", "Notiz"],
            difference.Changes.Select(change => change.Split(':')[0]).ToArray());
    }

    private static XmlOrderImportData CreateInput(string deliveryType) =>
        new()
        {
            AuftragNr = " A-1 ",
            AuftragsDatum = new DateTime(2026, 10, 16),
            KundeFirma = "Muster AG",
            KundeStrasse = "Musterstrasse",
            KundeHausnummer = "1",
            KundePLZ = "8000",
            KundeOrt = "Zürich",
            Lieferbedingung = deliveryType
        };
}
