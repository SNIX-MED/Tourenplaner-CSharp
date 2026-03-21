using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class JsonToursRepositoryTests
{
    [Fact]
    public async Task LoadAsync_NormalizesDatesAndStops()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "tours.json");

        try
        {
            var payload =
                """
                [
                  {
                    "name": " Morgenroute ",
                    "date": "2026-03-21",
                    "stops": [
                      {
                        "auftragsnummer": "A-100",
                        "name": " Kunde 1 ",
                        "address": " Hauptstr. 1 ",
                        "order": 0,
                        "serviceMinutes": -5
                      }
                    ]
                  }
                ]
                """;
            await File.WriteAllTextAsync(file, payload);

            var repository = new JsonToursRepository(file);
            var tours = await repository.LoadAsync();

            Assert.Single(tours);
            Assert.Equal("21.03.2026", tours[0].Date);
            Assert.Equal("Morgenroute", tours[0].Name);
            Assert.Single(tours[0].Stops);
            Assert.Equal("auftrag:A-100", tours[0].Stops[0].Id);
            Assert.Equal(1, tours[0].Stops[0].Order);
            Assert.Equal(0, tours[0].Stops[0].ServiceMinutes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
