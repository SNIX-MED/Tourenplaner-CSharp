using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.Tests.Application;

public class NewXmlOrderReviewTests
{
    [Fact]
    public async Task CreationIsDurableAndReimportsAndAddressChangesDoNotRenewIt()
    {
        var file = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"), "orders.json");
        var repository = new JsonOrderRepository(file);
        var xml = Xml(" A-NEW ");
        var service = new OrderImportService();
        var preview = await service.PreviewImportAsync([xml], repository);
        Assert.Equal(1, preview.CreatedOrders);
        Assert.Empty(await repository.GetAllAsync());
        var result = await service.ImportOrdersAsync([xml, xml], repository, markAsXmlImported: true);
        Assert.Equal(1, result.CreatedOrders);
        Assert.Empty(result.Errors);
        var order = Assert.Single(await repository.GetAllAsync());
        Assert.NotNull(order.XmlCreatedAtUtc);
        var key = NewXmlOrderReviewService.ReviewKey(order);
        Assert.True(NewXmlOrderReviewService.IsPending(order, new HashSet<string>()));
        Assert.Equal(0, (await service.ImportOrdersAsync([xml], repository, markAsXmlImported: true)).CreatedOrders);
        xml.LieferStrasse = "Neue Strasse";
        await service.ImportOrdersAsync([xml], repository, markAsXmlImported: true);
        order = Assert.Single(await new JsonOrderRepository(file).GetAllAsync());
        Assert.Equal(key, NewXmlOrderReviewService.ReviewKey(order));
        Assert.False(NewXmlOrderReviewService.IsPending(order, new HashSet<string> { key }));
    }

    [Fact]
    public async Task LegacyAndManualOrdersDoNotBecomeNewWhenImportedAgain()
    {
        var file = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"), "orders.json");
        var repository = new JsonOrderRepository(file);
        await repository.SaveAllAsync([new Order { Id = "OLD", IsXmlImported = true }, new Order { Id = "MANUAL" }]);
        await new OrderImportService().ImportOrdersAsync([Xml("OLD"), Xml("MANUAL")], repository, markAsXmlImported: true);
        Assert.All(await repository.GetAllAsync(), order =>
        {
            Assert.Null(order.XmlCreatedAtUtc);
            Assert.False(NewXmlOrderReviewService.IsPending(order, new HashSet<string>()));
        });
    }

    [Fact]
    public async Task PlanningAcknowledgementSurvivesUnplanningAndReimport()
    {
        var file = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"), "orders.json");
        var repository = new JsonOrderRepository(file);
        var xml = Xml("NEW");
        await new OrderImportService().ImportOrdersAsync([xml], repository, markAsXmlImported: true);
        var order = Assert.Single(await repository.GetAllAsync());
        order.AssignedTourId = "42";
        Assert.True(NewXmlOrderReviewService.MarkPlanned(order));
        order.AssignedTourId = null;
        await repository.SaveAllAsync([order]);
        await new OrderImportService().ImportOrdersAsync([xml], repository, markAsXmlImported: true);
        order = Assert.Single(await repository.GetAllAsync());
        Assert.True(order.XmlNewOrderWasPlanned);
        Assert.False(NewXmlOrderReviewService.IsPending(order, new HashSet<string>()));
    }

    [Fact]
    public void PersonalReviewKeysAreDeepCopiedAndDoNotAffectAnotherUser()
    {
        var settings = new AppSettings();
        var anna = settings.ResolveUserPreference("Anna");
        anna.SeenXmlImportOrderKeys.Add("NEW:123");
        settings.SetUserPreference("Anna", anna);
        anna.SeenXmlImportOrderKeys.Clear();
        Assert.Contains("NEW:123", settings.ResolveUserPreference("Anna").SeenXmlImportOrderKeys);
        Assert.Empty(settings.ResolveUserPreference("Bob").SeenXmlImportOrderKeys);
        var copy = settings.ResolveUserPreference("Anna");
        copy.SeenXmlImportOrderKeys.Clear();
        Assert.Single(settings.ResolveUserPreference("Anna").SeenXmlImportOrderKeys);
    }

    private static XmlOrderImportData Xml(string id) => new()
    {
        AuftragNr = id, KundeFirma = "Testkunde", KundeStrasse = "Hauptstrasse", KundePLZ = "8000", KundeOrt = "Zürich",
        LieferStrasse = "Hauptstrasse", LieferPLZ = "8000", LieferOrt = "Zürich", Lieferbedingung = "Frei Bordsteinkante",
        AuftragsDatum = DateTime.Today, Produkte = [new() { Bezeichnung = "Ware", Menge = 1, Gewicht = 10 }]
    };

    [Fact]
    public void SynchronizingAnAlreadyAssignedPartialDeliveryPersistsPlanningAcknowledgement()
    {
        var order = new Order { Id = "NEW", IsXmlImported = true, XmlCreatedAtUtc = DateTimeOffset.UtcNow,
            AssignedTourId = "42", Products = [new() { PositionId = "P", Name = "Ware", Quantity = 1 }],
            Deliveries = [new() { Id = "D", AssignedTourId = "42", Status = OrderDeliveryStatus.Planned,
                Lines = [new() { OrderProductPositionId = "P", Quantity = 1 }] }] };
        var tour = new TourRecord { Id = 42, Stops = [new() { Id = "stop", Auftragsnummer = "NEW", DeliveryId = "D" }] };
        Assert.True(new OrderDeliveryService().SynchronizeTourAssignments([order], [tour]));
        Assert.True(order.XmlNewOrderWasPlanned);
        Assert.False(new OrderDeliveryService().SynchronizeTourAssignments([order], [tour]));
        order.Deliveries[0].AssignedTourId = null;
        order.AssignedTourId = null;
        Assert.False(NewXmlOrderReviewService.IsPending(order, new HashSet<string>()));
    }
}
