using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class UpdateStorageModeGuardTests
{
    [Theory]
    [InlineData(AppStorageMode.JsonFiles, AppStorageMode.PostgreSql)]
    [InlineData(AppStorageMode.PostgreSql, AppStorageMode.JsonFiles)]
    public async Task RestoreAsync_RestoresModeCapturedBeforeUpdate(
        AppStorageMode modeBeforeUpdate,
        AppStorageMode modeWrittenByUpdate)
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-update-mode-tests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "data");
        var stateRoot = Path.Combine(root, "state");
        Directory.CreateDirectory(dataRoot);
        var settingsPath = Path.Combine(dataRoot, "settings.json");

        try
        {
            var originalPostgreSqlSettings = new PostgreSqlStorageSettings
            {
                Host = "database.internal",
                Database = "tourenplaner",
                Username = "app",
                Password = "test-password"
            };
            await WriteSettingsAsync(settingsPath, modeBeforeUpdate, originalPostgreSqlSettings);
            await UpdateStorageModeGuard.CaptureAsync(dataRoot, stateRoot, "1.2.3");

            await WriteSettingsAsync(settingsPath, modeWrittenByUpdate, new PostgreSqlStorageSettings());

            var restoredMode = await UpdateStorageModeGuard.RestoreAsync(dataRoot, stateRoot);
            var restored = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(settingsPath))!;

            Assert.Equal(modeBeforeUpdate, restoredMode);
            Assert.Equal(modeBeforeUpdate, restored.StorageMode);
            Assert.False(File.Exists(Path.Combine(stateRoot, "storage-mode-before-update.json")));
            if (modeBeforeUpdate == AppStorageMode.PostgreSql)
            {
                Assert.Equal("database.internal", restored.PostgreSqlStorage.Host);
                Assert.Equal("test-password", restored.PostgreSqlStorage.Password);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static Task WriteSettingsAsync(
        string path,
        AppStorageMode mode,
        PostgreSqlStorageSettings postgreSqlStorage)
    {
        return File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new AppSettings
            {
                StorageMode = mode,
                PostgreSqlStorage = postgreSqlStorage
            }));
    }
}
