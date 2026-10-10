using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class StorageRepositoryFactoryTests
{
    [Fact]
    public async Task CreateAsync_ForcedLocalSession_PreservesConfiguredPostgreSqlMode()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "tourenplaner-storage-factory-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        var settingsPath = Path.Combine(dataRoot, "settings.json");
        var settings = new AppSettings
        {
            StorageMode = AppStorageMode.PostgreSql,
            PostgreSqlStorage = new PostgreSqlStorageSettings
            {
                Host = "unreachable.invalid",
                Database = "tourenplaner",
                Username = "tourenplaner_app"
            }
        };

        try
        {
            await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings));

            var bundle = await new StorageRepositoryFactory().CreateAsync(
                dataRoot,
                settingsPath,
                Path.Combine(dataRoot, "orders.json"),
                Path.Combine(dataRoot, "tours.json"),
                Path.Combine(dataRoot, "employees.json"),
                Path.Combine(dataRoot, "vehicles.json"),
                Path.Combine(dataRoot, "calendar.json"),
                forceLocalStorageForSession: true);

            Assert.Equal(AppStorageMode.JsonFiles, bundle.StorageMode);
            Assert.Null(bundle.PostgreSqlStorageSettings);

            var persisted = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(settingsPath));
            Assert.NotNull(persisted);
            Assert.Equal(AppStorageMode.PostgreSql, persisted.StorageMode);
            Assert.Equal("unreachable.invalid", persisted.PostgreSqlStorage.Host);
        }
        finally
        {
            Directory.Delete(dataRoot, true);
        }
    }
}
