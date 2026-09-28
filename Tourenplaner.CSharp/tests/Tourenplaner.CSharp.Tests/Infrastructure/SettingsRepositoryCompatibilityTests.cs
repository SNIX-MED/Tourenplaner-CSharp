using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public sealed class SettingsRepositoryCompatibilityTests
{
    [Fact]
    public async Task XmlImportSettingsRoundTrip_PreservesKeyAndOtherSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            var editor = new JsonAppSettingsRepository(path);
            var importer = new JsonSettingsRepository(path);
            await editor.SaveAsync(new AppSettings
            {
                TomTomApiKey = "test-placeholder-key", CompanyName = "Test Company",
                CompanyPostalCode = "6440"
            });
            var settings = await importer.GetAsync();
            Assert.Equal("test-placeholder-key", settings.TomTomApiKey);
            Assert.Equal("Test Company", settings.CompanyName);
            settings.XmlImportFilePath = "Export.xml";
            await importer.SaveAsync(settings);
            var reloaded = await editor.LoadAsync();
            Assert.Equal("test-placeholder-key", reloaded.TomTomApiKey);
            Assert.Equal("6440", reloaded.CompanyPostalCode);
            Assert.Equal("Export.xml", reloaded.XmlImportFilePath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
