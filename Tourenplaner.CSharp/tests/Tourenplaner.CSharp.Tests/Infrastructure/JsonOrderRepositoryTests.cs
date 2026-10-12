using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class JsonOrderRepositoryTests
{
    [Fact]
    public async Task SaveAndLoad_PersistsOrdersToJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "orders.json");
            var repository = new JsonOrderRepository(file);

            var orders = new[]
            {
                new Order
                {
                    Id = "O-001",
                    CustomerName = "Musterkunde",
                    Address = "Bahnhofstrasse 1",
                    Type = OrderType.Map,
                    ScheduledDate = new DateOnly(2026, 3, 21),
                    Location = new GeoPoint(47.0, 8.0),
                    IstVorauszahlung = true,
                    IsAlternativeDeliveryEnabled = true,
                    Products =
                    [
                        new OrderProductInfo
                        {
                            Name = "Schrank",
                            ExpectedDelivery = "Mitte Oktober"
                        }
                    ]
                },
                new Order
                {
                    Id = "O-002",
                    CustomerName = "NonMap Kunde",
                    Address = "Lager",
                    Type = OrderType.NonMap,
                    ScheduledDate = new DateOnly(2026, 3, 22)
                }
            };

            await repository.SaveAllAsync(orders);
            var loaded = await repository.GetAllAsync();

            Assert.Equal(2, loaded.Count);
            Assert.Contains(loaded, o => o.Id == "O-001" && o.Location is not null);
            Assert.Contains(loaded, o => o.Id == "O-001" && o.IstVorauszahlung);
            Assert.Contains(loaded, o => o.Id == "O-001" && o.IsAlternativeDeliveryEnabled);
            Assert.Contains(loaded, o => o.Id == "O-001" && o.Products.Single().ExpectedDelivery == "Mitte Oktober");
            Assert.Contains(loaded, o => o.Id == "O-002" && o.Type == OrderType.NonMap);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_KeepsLegacyOrdersWithoutPrepaymentFlagCompatible()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "orders.json");
            await File.WriteAllTextAsync(
                file,
                """
                [
                  {
                    "id": "O-LEGACY",
                    "customerName": "Altkunde",
                    "address": "Lager",
                    "type": 0,
                    "scheduledDate": "2026-03-23"
                  }
                ]
                """);

            var repository = new JsonOrderRepository(file);

            var loaded = await repository.GetAllAsync();

            var order = Assert.Single(loaded);
            Assert.Equal("O-LEGACY", order.Id);
            Assert.False(order.IstVorauszahlung);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentRepositoryInstances_SerializeReadsAndWritesForSameFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "orders.json");
            var writer = new JsonOrderRepository(file);
            var reader = new JsonOrderRepository(file);
            await writer.SaveAllAsync([]);

            var writes = Enumerable.Range(1, 40).Select(index =>
                writer.SaveAllAsync([new Order { Id = $"O-{index}", ScheduledDate = new DateOnly(2026, 10, 12) }]));
            var reads = Enumerable.Range(1, 40).Select(_ => reader.GetAllAsync());

            await Task.WhenAll(writes.Cast<Task>().Concat(reads));
            var loaded = await reader.GetAllAsync();
            Assert.Single(loaded);
            Assert.StartsWith("O-", loaded[0].Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentUpserts_DoNotLoseOrders()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "orders.json");
            var firstRepository = new JsonOrderRepository(file);
            var secondRepository = new JsonOrderRepository(file);
            await firstRepository.SaveAllAsync([]);

            var writes = Enumerable.Range(1, 40).Select(index =>
                (index % 2 == 0 ? firstRepository : secondRepository)
                .UpsertAsync(new Order { Id = $"O-{index}", ScheduledDate = new DateOnly(2026, 10, 12) }));
            await Task.WhenAll(writes);

            var orders = await firstRepository.GetAllAsync();
            Assert.Equal(40, orders.Count);
            Assert.Equal(40, orders.Select(x => x.Id).Distinct().Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
