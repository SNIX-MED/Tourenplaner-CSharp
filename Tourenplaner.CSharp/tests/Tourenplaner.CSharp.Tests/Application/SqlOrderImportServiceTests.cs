using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class SqlOrderImportServiceTests
{
    [Fact]
    public async Task PreviewImportAsync_ClassifiesCreatedUpdatedAndUnchangedOrders()
    {
        var repository = new FakeOrderRepository(
        [
            CreateOrder("A-1", "Kunde Eins", "Frei Bordsteinkante", "Hinweis alt"),
            CreateOrder("A-2", "Kunde Zwei", "Frei Bordsteinkante", "Bleibt gleich")
        ]);
        var service = new SqlOrderImportService();

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

        var updated = Assert.Single(result.Items.Where(x => x.Action == ImportPreviewAction.Update));
        Assert.Equal("A-1", updated.OrderId);
        Assert.Contains(updated.Changes, x => x.Contains("Lieferart", StringComparison.Ordinal));
        Assert.Contains(updated.Changes, x => x.Contains("Notiz", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportOrdersAsync_PreservesManualProductState_AndSkipsUnchangedOrders()
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
        var settingsRepository = new FakeSettingsRepository();
        var service = new SqlOrderImportService();

        var result = await service.ImportOrdersAsync(
        [
            CreateSqlOrder("A-1", "Kunde Eins", "Frei Bordsteinkante", "Hinweis neu"),
            CreateSqlOrder("A-2", "Kunde Zwei", "Frei Bordsteinkante", "Bleibt gleich")
        ],
        repository,
        settingsRepository);

        Assert.Equal(0, result.CreatedOrders);
        Assert.Equal(1, result.UpdatedOrders);
        Assert.Equal(1, result.UnchangedOrders);
        Assert.Equal(1, repository.SaveAllCalls);

        var storedChanged = Assert.Single(repository.StoredOrders.Where(x => x.Id == "A-1"));
        Assert.Equal("Hinweis neu", storedChanged.Notes);
        Assert.Equal("An Lager", storedChanged.Products[0].DeliveryStatus);
        Assert.Equal("Lieferant A", storedChanged.Products[0].Supplier);
        Assert.Equal("120x80", storedChanged.Products[0].Dimensions);

        var storedUnchanged = Assert.Single(repository.StoredOrders.Where(x => x.Id == "A-2"));
        Assert.Equal("Bleibt gleich", storedUnchanged.Notes);
        Assert.Equal("Auf dem Weg", storedUnchanged.Products[0].DeliveryStatus);
    }

    private static SqlOrderImportData CreateSqlOrder(string id, string customerName, string deliveryType, string notes)
    {
        return new SqlOrderImportData
        {
            AuftragNr = id,
            AuftragsDatum = new DateTime(2026, 6, 10),
            KundeFirma = customerName,
            KundeStrasse = "Musterstrasse",
            KundeHausnummer = "1",
            KundePLZ = "8000",
            KundeOrt = "Zuerich",
            Lieferbedingung = deliveryType,
            Notiz = notes,
            Produkte =
            [
                new SqlOrderProductData
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
                    DeliveryStatus = OrderProductInfo.DefaultDeliveryStatus
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
            OrderStatus = order.OrderStatus,
            AvisoStatus = order.AvisoStatus,
            Notes = order.Notes,
            IsArchived = order.IsArchived
        };
    }
}
