using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderImportServiceTests
{
    [Theory]
    [InlineData("Frei Bordsteinkante", "Frei Bordsteinkante", true)]
    [InlineData("Frei Bordsteinkante", "Mit Verteilung", false)]
    [InlineData("Frei Bordsteinkante", "Spediteur", false)]
    [InlineData("Spediteur", "Spediteur", true)]
    [InlineData("Spediteur", "Post", false)]
    public async Task XmlReimport_PreservesAlternativeOnlyWhileOrderedDeliveryTypeIsUnchanged(
        string existingDeliveryType,
        string importedDeliveryType,
        bool expectedAlternative)
    {
        var existing = CreateOrder("A-1", "Kunde", existingDeliveryType, "Notiz");
        existing.IsAlternativeDeliveryEnabled = true;
        var repository = new FakeOrderRepository([existing]);
        var xmlOrder = CreateSqlOrder("A-1", "Kunde", importedDeliveryType, "Notiz");

        var result = await new OrderImportService().ImportOrdersAsync([xmlOrder], repository);

        Assert.Empty(result.Errors);
        Assert.Equal(expectedAlternative, Assert.Single(repository.StoredOrders).IsAlternativeDeliveryEnabled);
    }

    [Fact]
    public async Task XmlArchiving_SpediteurWithAlternativeLiefertour_IsProtectedWhileTourIsActive()
    {
        var existing = CreateOrder("A-1", "Kunde", "Spediteur", "Notiz");
        existing.IsAlternativeDeliveryEnabled = true;
        existing.AssignedTourId = "7";
        var repository = new FakeOrderRepository([existing]);
        var xmlOrder = CreateSqlOrder("A-1", "Kunde", "Spediteur", "Notiz");
        xmlOrder.Archiviert = true;
        var tours = new[] { new TourRecord { Id = 7, Stops = [new TourStopRecord { Auftragsnummer = "A-1" }] } };

        var activeResult = await new OrderImportService().ImportOrdersAsync([xmlOrder], repository, tours: tours);

        Assert.Equal(1, activeResult.UpdatedOrders);
        Assert.False(Assert.Single(repository.StoredOrders).IsArchived);

        tours[0].IsArchived = true;
        var completedResult = await new OrderImportService().ImportOrdersAsync([xmlOrder], repository, tours: tours);
        Assert.Equal(1, completedResult.UpdatedOrders);
        Assert.True(Assert.Single(repository.StoredOrders).IsArchived);
    }

    [Theory]
    [InlineData("Frei Bordsteinkante", false, false)]
    [InlineData("Mit Verteilung", false, false)]
    [InlineData("Mit Verteilung und Montage", false, false)]
    [InlineData("Tresor Bordstein", false, false)]
    [InlineData("Tresor Verwendung", false, false)]
    [InlineData("Frei Bordsteinkante", true, true)]
    [InlineData("Post", false, true)]
    [InlineData("Selbstabholung", false, true)]
    [InlineData("Spediteur", false, true)]
    [InlineData("Post", true, true)]
    [InlineData("Selbstabholung", true, true)]
    [InlineData("Spediteur", true, true)]
    public async Task XmlArchiving_RespectsTourStateAndDeliveryMethod(string deliveryType, bool completed, bool expectedArchived)
    {
        var repository = new FakeOrderRepository([CreateOrder("A-1", "Kunde", deliveryType, "Alt")]);
        var xmlOrder = CreateSqlOrder("A-1", "Kunde", deliveryType, "Neu");
        xmlOrder.Archiviert = true;
        var tours = new[] { new TourRecord { IsArchived = completed, Stops = [new TourStopRecord { Auftragsnummer = " a-1 " }] } };
        var service = new OrderImportService();

        var preview = await service.PreviewImportAsync([xmlOrder], repository, tours);
        Assert.Empty(preview.Errors);
        Assert.Equal(expectedArchived, Assert.Single(preview.Items).Changes.Any(change => change.StartsWith("Archiviert:")));
        Assert.False(Assert.Single(repository.StoredOrders).IsArchived);

        var result = await service.ImportOrdersAsync([xmlOrder], repository, tours: tours);
        Assert.Empty(result.Errors);
        var stored = Assert.Single(repository.StoredOrders);
        Assert.Equal(expectedArchived, stored.IsArchived);
        Assert.Equal("Neu", stored.Notes);
    }

    [Fact]
    public async Task XmlArchiving_ActiveTourWinsUntilCompleted_ThenReimportArchivesOnce()
    {
        var repository = new FakeOrderRepository([CreateOrder("A-1", "Kunde", "Frei Bordsteinkante", "Notiz")]);
        var xmlOrder = CreateSqlOrder("A-1", "Kunde", "Frei Bordsteinkante", "Notiz");
        xmlOrder.Archiviert = true;
        var activeTour = new TourRecord { Stops = [new TourStopRecord { Auftragsnummer = "A-1" }] };
        var tours = new[] { new TourRecord { IsArchived = true, Stops = [new TourStopRecord { Auftragsnummer = "A-1" }] }, activeTour };
        var service = new OrderImportService();

        var preview = await service.PreviewImportAsync([xmlOrder], repository, tours);
        Assert.Equal(1, preview.UnchangedOrders);
        var protectedResult = await service.ImportOrdersAsync([xmlOrder], repository, tours: tours);
        Assert.Equal(1, protectedResult.UnchangedOrders);
        Assert.Equal(0, repository.SaveAllCalls);

        activeTour.IsArchived = true;
        var result = await service.ImportOrdersAsync([xmlOrder], repository, tours: tours);
        Assert.Equal(1, result.UpdatedOrders);
        Assert.True(Assert.Single(repository.StoredOrders).IsArchived);
        var repeated = await service.ImportOrdersAsync([xmlOrder], repository, tours: tours);
        Assert.Equal(1, repeated.UnchangedOrders);
        Assert.Equal(1, repository.SaveAllCalls);
    }

    [Fact]
    public async Task XmlArchiving_WithoutActualTourStopAndForNewOrders_FollowsXml()
    {
        var existing = CreateOrder("A-1", "Kunde", "Frei Bordsteinkante", "Notiz");
        existing.AssignedTourId = "1";
        var repository = new FakeOrderRepository([existing]);
        var imports = new[] { "A-1", "A-2" }.Select(id => CreateSqlOrder(id, "Kunde", "Frei Bordsteinkante", "Notiz")).ToList();
        imports.ForEach(order => order.Archiviert = true);
        var tours = new[] { new TourRecord { Id = 1, Stops = [new TourStopRecord { Auftragsnummer = "A-2" }] } };

        var result = await new OrderImportService().ImportOrdersAsync(imports, repository, tours: tours);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.CreatedOrders);
        Assert.Equal(1, result.UpdatedOrders);
        Assert.All(repository.StoredOrders, order => Assert.True(order.IsArchived));
    }

    [Fact]
    public async Task PreviewImportAsync_ClassifiesCreatedUpdatedAndUnchangedOrders()
    {
        var existingUnchangedOrder = CreateOrder("A-2", "Kunde Zwei", "Frei Bordsteinkante", "Bleibt gleich");
        existingUnchangedOrder.Products[0].DeliveryStatus = OrderProductInfo.InTransitStatus;
        existingUnchangedOrder.OrderStatus = Order.ResolveOrderStatusFromProducts(existingUnchangedOrder.Products);

        var repository = new FakeOrderRepository(
        [
            CreateOrder("A-1", "Kunde Eins", "Frei Bordsteinkante", "Hinweis alt"),
            existingUnchangedOrder
        ]);
        var service = new OrderImportService();

        var result = await service.PreviewImportAsync(
        [
            CreateSqlOrder("A-1", "Kunde Eins", "Post", "Hinweis neu"),
            CreateSqlOrder("A-2", "Kunde Zwei", "Frei Bordsteinkante", "Bleibt gleich"),
            CreateSqlOrder("A-3", "Kunde Drei", "Frei Bordsteinkante", "Neu")
        ],
        repository);

        Assert.Equal(3, result.ValidOrders);
        Assert.Equal(1, result.CreatedOrders);
        Assert.Equal(1, result.UpdatedOrders);
        Assert.Equal(1, result.UnchangedOrders);

        var updated = Assert.Single(result.Items, x => x.Action == ImportPreviewAction.Update);
        Assert.Equal("A-1", updated.OrderId);
        Assert.Contains(updated.Changes, x => x.Contains("Lieferart", StringComparison.Ordinal));
        Assert.Contains(updated.Changes, x => x.Contains("Notiz", StringComparison.Ordinal));

        var unchanged = Assert.Single(result.Items, x => x.Action == ImportPreviewAction.Unchanged);
        Assert.Equal("A-2", unchanged.OrderId);
        Assert.DoesNotContain(unchanged.Changes, x => x.Contains("Status", StringComparison.Ordinal));
        Assert.DoesNotContain(unchanged.Changes, x => x.Contains("Produkt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportOrdersAsync_AppliesDeliveryTimeProductStatus_AndPreservesProductMetadata()
    {
        var existingChangedOrder = CreateOrder("A-1", "Kunde Eins", "Frei Bordsteinkante", "Hinweis alt");
        existingChangedOrder.Products[0].DeliveryStatus = "An Lager";
        existingChangedOrder.Products[0].Supplier = "Lieferant A";
        existingChangedOrder.Products[0].Dimensions = "120x80";
        existingChangedOrder.OrderStatus = Order.ResolveOrderStatusFromProducts(existingChangedOrder.Products);

        var existingUnchangedOrder = CreateOrder("A-2", "Kunde Zwei", "Frei Bordsteinkante", "Bleibt gleich");
        existingUnchangedOrder.Products[0].DeliveryStatus = "Auf dem Weg";
        existingUnchangedOrder.OrderStatus = Order.ResolveOrderStatusFromProducts(existingUnchangedOrder.Products);

        var repository = new FakeOrderRepository([existingChangedOrder, existingUnchangedOrder]);
        var service = new OrderImportService();

        var result = await service.ImportOrdersAsync(
        [
            CreateSqlOrder("A-1", "Kunde Eins", "Frei Bordsteinkante", "Hinweis neu", "ab Lager (Zwischenverkauf vorbehalten)"),
            CreateSqlOrder("A-2", "Kunde Zwei", "Frei Bordsteinkante", "Bleibt gleich")
        ],
        repository);

        Assert.Equal(0, result.CreatedOrders);
        Assert.Equal(1, result.UpdatedOrders);
        Assert.Equal(1, result.UnchangedOrders);
        Assert.Equal(1, repository.SaveAllCalls);

        var storedChanged = Assert.Single(repository.StoredOrders, x => x.Id == "A-1");
        Assert.Equal("Hinweis neu", storedChanged.Notes);
        Assert.Equal("An Lager", storedChanged.Products[0].DeliveryStatus);
        Assert.Equal("Lieferant A", storedChanged.Products[0].Supplier);
        Assert.Equal("120x80", storedChanged.Products[0].Dimensions);

        var storedUnchanged = Assert.Single(repository.StoredOrders, x => x.Id == "A-2");
        Assert.Equal("Bleibt gleich", storedUnchanged.Notes);
        Assert.Equal(OrderProductInfo.InTransitStatus, storedUnchanged.Products[0].DeliveryStatus);
        Assert.Equal(Order.InTransitStatus, storedUnchanged.OrderStatus);
    }

    [Fact]
    public async Task ImportOrdersAsync_UsesSupplierFromXmlProduct_WhenPresent()
    {
        var existingOrder = CreateOrder("A-15", "Kunde Fuenfzehn", "Post", "Hinweis alt");
        existingOrder.Products[0].Supplier = "Manuell gepflegt";
        var importedOrder = CreateSqlOrder("A-15", "Kunde Fuenfzehn", "Post", "Hinweis alt");
        importedOrder.Produkte[0].Lieferant = "Esnova Racks S.A.";
        var repository = new FakeOrderRepository([existingOrder]);

        var preview = await new OrderImportService().PreviewImportAsync([importedOrder], repository);
        await new OrderImportService().ImportOrdersAsync([importedOrder], repository);

        Assert.Equal(ImportPreviewAction.Update, Assert.Single(preview.Items).Action);
        Assert.Contains(preview.Items[0].Changes, change => change.Contains("Esnova Racks S.A.", StringComparison.Ordinal));
        Assert.Equal("Esnova Racks S.A.", Assert.Single(repository.StoredOrders).Products[0].Supplier);
    }

    [Fact]
    public async Task ImportOrdersAsync_UsesProductDeliveryTimeAndSupplierForInitialProductStatus()
    {
        var xmlOrder = CreateSqlOrder("A-17", "Kunde Siebzehn", "Mit Verteilung", "Neu");
        xmlOrder.Produkte =
        [
            new XmlOrderProductData
            {
                PosNummer = 1,
                Bezeichnung = "Direkt ab Lager",
                Lieferzeit = "ab Lager (Zwischenverkauf vorbehalten)",
                Lieferant = "Anderer Lieferant",
                Menge = 1,
                Gewicht = 1
            },
            new XmlOrderProductData
            {
                PosNummer = 2,
                Bezeichnung = "Esnova ab Lager",
                Lieferzeit = "ab Lager (Zwischenverkauf vorbehalten)",
                Lieferant = "Esnova Racks S.A. | Plg. Los Campones s/n - Tremanes | ES-33211 Gijón | 111362",
                Menge = 1,
                Gewicht = 1
            }
        ];

        var repository = new FakeOrderRepository([]);
        await new OrderImportService().ImportOrdersAsync([xmlOrder], repository);

        var products = Assert.Single(repository.StoredOrders).Products;
        Assert.Equal(OrderProductInfo.InStockStatus, products[0].DeliveryStatus);
        Assert.Equal(OrderProductInfo.PendingPreparationStatus, products[1].DeliveryStatus);
    }

    [Fact]
    public async Task ImportOrdersAsync_WhenMarkedAsXmlImport_MarksCreatedAndUpdatedOrders()
    {
        var existingOrder = CreateOrder("A-1", "Kunde Eins", "Frei Bordsteinkante", "Hinweis alt");
        var repository = new FakeOrderRepository([existingOrder]);
        var service = new OrderImportService();

        await service.ImportOrdersAsync(
        [
            CreateSqlOrder("A-1", "Kunde Eins", "Frei Bordsteinkante", "Hinweis neu", "ab Lager (Zwischenverkauf vorbehalten)"),
            CreateSqlOrder("A-2", "Kunde Zwei", "Frei Bordsteinkante", "Neu")
        ],
        repository,
        markAsXmlImported: true);

        Assert.All(repository.StoredOrders, order => Assert.True(order.IsXmlImported));
    }

    [Fact]
    public async Task ImportOrdersAsync_StoresImportedAddressNumbers()
    {
        var repository = new FakeOrderRepository([]);
        var service = new OrderImportService();
        var xmlOrder = CreateSqlOrder("A-10", "Kunde Zehn", "Frei Bordsteinkante", "Neu");
        xmlOrder.KundeAdressNummer = "100";
        xmlOrder.LieferFirma = "Lieferstandort";
        xmlOrder.LieferStrasse = "Lieferweg";
        xmlOrder.LieferPLZ = "9000";
        xmlOrder.LieferOrt = "St. Gallen";
        xmlOrder.LieferAdressNummer = "200";

        await service.ImportOrdersAsync([xmlOrder], repository);

        var stored = Assert.Single(repository.StoredOrders);
        Assert.Equal("100", stored.OrderAddress.HouseNumber);
        Assert.Equal("200", stored.DeliveryAddress.HouseNumber);
    }

    [Fact]
    public async Task ImportOrdersAsync_StoresCompanyContactPersonsInPersonFields()
    {
        var repository = new FakeOrderRepository([]);
        var service = new OrderImportService();
        var xmlOrder = CreateSqlOrder("A-14", "Kurhaus am Sarnersee", "Frei Bordsteinkante", "Neu");
        xmlOrder.KundeKontaktperson = "Andreas Gehrig";
        xmlOrder.LieferFirma = "Kurhaus am Sarnersee";
        xmlOrder.LieferKontaktperson = "Andreas Gehrig";
        xmlOrder.LieferStrasse = "Wilerstrasse 35";
        xmlOrder.LieferPLZ = "6062";
        xmlOrder.LieferOrt = "Wilen (Sarnen)";

        await service.ImportOrdersAsync([xmlOrder], repository);

        var stored = Assert.Single(repository.StoredOrders);
        Assert.Equal("Kurhaus am Sarnersee", stored.OrderAddress.Name);
        Assert.Equal("Andreas Gehrig", stored.OrderAddress.ContactPerson);
        Assert.Equal("Kurhaus am Sarnersee", stored.DeliveryAddress.Name);
        Assert.Equal("Andreas Gehrig", stored.DeliveryAddress.ContactPerson);
    }

    [Fact]
    public async Task ImportOrdersAsync_StoresPrepaymentFlag()
    {
        var repository = new FakeOrderRepository([]);
        var service = new OrderImportService();
        var xmlOrder = CreateSqlOrder("A-11", "Kunde Elf", "Post", "Vorkasse");
        xmlOrder.IstVorauszahlung = true;

        await service.ImportOrdersAsync([xmlOrder], repository);

        var stored = Assert.Single(repository.StoredOrders);
        Assert.True(stored.IstVorauszahlung);
    }

    [Fact]
    public async Task ImportOrdersAsync_StoresPaidPrepaymentWithoutOpenPrepaymentFlag()
    {
        var repository = new FakeOrderRepository([]);
        var xmlOrder = CreateSqlOrder("A-16", "Kunde Sechzehn", "Post", "Bezahlt");
        xmlOrder.IstVorauszahlungBezahlt = true;

        await new OrderImportService().ImportOrdersAsync([xmlOrder], repository);

        var stored = Assert.Single(repository.StoredOrders);
        Assert.False(stored.IstVorauszahlung);
        Assert.True(stored.IstVorauszahlungBezahlt);
    }

    [Fact]
    public async Task ImportOrdersAsync_ClearsDeliveryDateWhenXmlHasNoExplicitDeliveryDate()
    {
        var existingOrder = CreateOrder("A-12", "Kunde Zwoelf", "Frei Bordsteinkante", "Alt");
        existingOrder.DeliveryDate = new DateOnly(2026, 6, 15);
        var repository = new FakeOrderRepository([existingOrder]);
        var service = new OrderImportService();

        await service.ImportOrdersAsync(
        [
            CreateSqlOrder("A-12", "Kunde Zwoelf", "Frei Bordsteinkante", "Neu")
        ],
        repository);

        var stored = Assert.Single(repository.StoredOrders);
        Assert.Null(stored.DeliveryDate);
    }

    [Fact]
    public async Task ImportOrdersAsync_TreatsMinValueDeliveryDateAsMissing()
    {
        var repository = new FakeOrderRepository([]);
        var service = new OrderImportService();
        var xmlOrder = CreateSqlOrder("A-13", "Kunde Dreizehn", "Frei Bordsteinkante", "Neu");
        xmlOrder.Lieferdatum = DateTime.MinValue;

        await service.ImportOrdersAsync([xmlOrder], repository);

        var stored = Assert.Single(repository.StoredOrders);
        Assert.Null(stored.DeliveryDate);
    }

    private static XmlOrderImportData CreateSqlOrder(
        string id,
        string customerName,
        string deliveryType,
        string notes,
        string? deliveryTime = null)
    {
        return new XmlOrderImportData
        {
            AuftragNr = id,
            AuftragsDatum = new DateTime(2026, 6, 10),
            KundeFirma = customerName,
            KundeStrasse = "Musterstrasse",
            KundeHausnummer = "1",
            KundePLZ = "8000",
            KundeOrt = "Zuerich",
            Lieferbedingung = deliveryType,
            Lieferzeit = deliveryTime ?? string.Empty,
            Notiz = notes,
            Produkte =
            [
                new XmlOrderProductData
                {
                    PosNummer = 1,
                    Bezeichnung = "Produkt A",
                    Menge = 2,
                    Gewicht = 10
                }
            ]
        };
    }

    private static Order CreateOrder(string id, string customerName, string deliveryType, string notes)
    {
        return new Order
        {
            Id = id,
            CustomerName = customerName,
            Address = "Musterstrasse 1, 8000 Zuerich",
            ScheduledDate = new DateOnly(2026, 6, 10),
            Type = OrderType.Map,
            OrderAddress = new OrderAddressInfo
            {
                Name = customerName,
                Street = "Musterstrasse 1",
                PostalCode = "8000",
                City = "Zuerich"
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = customerName,
                Street = "Musterstrasse 1",
                PostalCode = "8000",
                City = "Zuerich"
            },
            DeliveryType = deliveryType,
            Notes = notes,
            Products =
            [
                new OrderProductInfo
                {
                    Name = "Produkt A",
                    Quantity = 2,
                    UnitWeightKg = 10,
                    WeightKg = 20,
                    DeliveryStatus = OrderProductInfo.OrderedStatus
                }
            ],
            OrderStatus = Order.OrderedStatus
        };
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private List<Order> _orders;

        public FakeOrderRepository(IEnumerable<Order> orders)
        {
            _orders = orders.Select(CloneOrder).ToList();
        }

        public int SaveAllCalls { get; private set; }

        public IReadOnlyList<Order> StoredOrders => _orders;

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Order>>(_orders.Select(CloneOrder).ToList());
        }

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
        {
            SaveAllCalls++;
            _orders = orders.Select(CloneOrder).ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsRepository : ISettingsRepository
    {
        public AppSettings Settings { get; private set; } = new();

        public Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private static Order CloneOrder(Order order)
    {
        return new Order
        {
            Id = order.Id,
            CustomerName = order.CustomerName,
            Address = order.Address,
            ScheduledDate = order.ScheduledDate,
            DeliveryDate = order.DeliveryDate,
            Type = order.Type,
            Location = order.Location is null ? null : new GeoPoint(order.Location.Latitude, order.Location.Longitude),
            AssignedTourId = order.AssignedTourId,
            OrderAddress = new OrderAddressInfo
            {
                Name = order.OrderAddress.Name,
                ContactPerson = order.OrderAddress.ContactPerson,
                Additional = order.OrderAddress.Additional,
                Street = order.OrderAddress.Street,
                HouseNumber = order.OrderAddress.HouseNumber,
                PostalCode = order.OrderAddress.PostalCode,
                City = order.OrderAddress.City
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = order.DeliveryAddress.Name,
                ContactPerson = order.DeliveryAddress.ContactPerson,
                Additional = order.DeliveryAddress.Additional,
                Street = order.DeliveryAddress.Street,
                HouseNumber = order.DeliveryAddress.HouseNumber,
                PostalCode = order.DeliveryAddress.PostalCode,
                City = order.DeliveryAddress.City
            },
            Email = order.Email,
            Phone = order.Phone,
            Products = order.Products.Select(x => new OrderProductInfo
            {
                Name = x.Name,
                Supplier = x.Supplier,
                Quantity = x.Quantity,
                UnitWeightKg = x.UnitWeightKg,
                WeightKg = x.WeightKg,
                Dimensions = x.Dimensions,
                DeliveryStatus = x.DeliveryStatus
            }).ToList(),
            DeliveryType = order.DeliveryType,
            IsAlternativeDeliveryEnabled = order.IsAlternativeDeliveryEnabled,
            OrderStatus = order.OrderStatus,
            AvisoStatus = order.AvisoStatus,
            Notes = order.Notes,
            IstVorauszahlung = order.IstVorauszahlung,
            IstVorauszahlungBezahlt = order.IstVorauszahlungBezahlt,
            IsArchived = order.IsArchived,
            IsXmlImported = order.IsXmlImported
        };
    }
}
