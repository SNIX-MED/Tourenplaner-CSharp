using System.IO.Compression;
using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class PreUpdateBackupServiceTests
{
    [Fact]
    public async Task CreateAsync_ArchivesLocalDataAndVersionMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-pre-update-tests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "data");
        var configuredBackupRoot = Path.Combine(root, "shared-backups");
        Directory.CreateDirectory(dataRoot);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataRoot, "settings.json"),
                JsonSerializer.Serialize(new AppSettings
                {
                    StorageMode = AppStorageMode.JsonFiles,
                    BackupDir = configuredBackupRoot
                }));
            await File.WriteAllTextAsync(Path.Combine(dataRoot, "tours.json"), "[{\"Id\":42}]");

            var backupPath = await PreUpdateBackupService.CreateAsync(dataRoot, "1.0.88", "1.0.89");

            Assert.True(File.Exists(backupPath));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(configuredBackupRoot, "pre-update")),
                Path.GetDirectoryName(Path.GetFullPath(backupPath)));
            using (var archive = ZipFile.OpenRead(backupPath))
            {
                Assert.NotNull(archive.GetEntry("metadata.json"));
                Assert.NotNull(archive.GetEntry("local-data/settings.json"));
                Assert.NotNull(archive.GetEntry("local-data/tours.json"));

                var metadataEntry = archive.GetEntry("metadata.json")!;
                using var reader = new StreamReader(metadataEntry.Open());
                var metadata = await reader.ReadToEndAsync();
                Assert.Contains("1.0.88", metadata);
                Assert.Contains("1.0.89", metadata);
            }

            File.Delete(backupPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
