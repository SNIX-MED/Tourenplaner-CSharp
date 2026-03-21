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
                    Location = new GeoPoint(47.0, 8.0)
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
}
