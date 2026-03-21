using System.IO.Compression;
using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class BackupManagerTests
{
    [Fact]
    public async Task CreateBackupAsync_CreatesArchiveWithManifest()
    {
        var root = CreateTempRoot();
        var configDir = Path.Combine(root, "cfg");
        var dataDir = Path.Combine(root, "data");
        var backupDir = Path.Combine(root, "backups");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(backupDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(configDir, "settings.json"), "{\"x\":1}");
            await File.WriteAllTextAsync(Path.Combine(configDir, "pins.json"), "[]");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "employees.json"), "[]");

            var manager = new BackupManager();
            var backupPath = await manager.CreateBackupAsync("gawela", configDir, dataDir, backupDir, "full");

            Assert.True(File.Exists(backupPath));

            using var zip = ZipFile.OpenRead(backupPath);
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("config/settings.json"));
            Assert.NotNull(zip.GetEntry("data/employees.json"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_RestoresSelectedGroupsOnly()
    {
        var root = CreateTempRoot();
        var configDir = Path.Combine(root, "cfg");
        var dataDir = Path.Combine(root, "data");
        var backupDir = Path.Combine(root, "backups");
        var restoreCfg = Path.Combine(root, "restoreCfg");
        var restoreData = Path.Combine(root, "restoreData");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(backupDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(configDir, "settings.json"), "{\"theme\":\"System\"}");
            await File.WriteAllTextAsync(Path.Combine(configDir, "tours.json"), "[{\"name\":\"T1\"}]");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "employees.json"), "[{\"id\":\"e1\"}]");

            var manager = new BackupManager();
            var backupPath = await manager.CreateBackupAsync("gawela", configDir, dataDir, backupDir, "full");

            await manager.RestoreBackupAsync(
                backupPath,
                restoreData,
                restoreCfg,
                selectedGroups: new[] { "settings", "employees" });

            Assert.True(File.Exists(Path.Combine(restoreCfg, "settings.json")));
            Assert.True(File.Exists(Path.Combine(restoreData, "employees.json")));
            Assert.False(File.Exists(Path.Combine(restoreCfg, "tours.json")));
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
