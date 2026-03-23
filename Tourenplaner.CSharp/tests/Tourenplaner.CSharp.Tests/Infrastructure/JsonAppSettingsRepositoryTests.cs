using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class JsonAppSettingsRepositoryTests
{
    [Fact]
    public async Task SaveAndLoad_PersistsSettings()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "settings.json");

        try
        {
            var repository = new JsonAppSettingsRepository(file);
            var settings = await repository.LoadAsync();
            settings.BackupsEnabled = true;
            settings.BackupDir = @"C:\Backups";
            settings.AppearanceMode = "Dark";

            await repository.SaveAsync(settings);
            var loaded = await repository.LoadAsync();

            Assert.True(loaded.BackupsEnabled);
            Assert.Equal(@"C:\Backups", loaded.BackupDir);
            Assert.Equal("Dark", loaded.AppearanceMode);
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
