using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class UnifiedBackupRestoreServiceTests
{
    [Fact]
    public async Task RestoreAsync_PostgreSqlBackupToLocal_WritesSqlPayloadsAndKeepsLocalMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-unified-backup-tests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "data");
        var backupPath = Path.Combine(root, "sql.zip");
        Directory.CreateDirectory(dataRoot);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataRoot, "settings.json"),
                JsonSerializer.Serialize(new AppSettings { StorageMode = AppStorageMode.JsonFiles }));
            CreatePostgreSqlBackup(backupPath);

            await new UnifiedBackupRestoreService().RestoreAsync(
                backupPath,
                dataRoot,
                AppStorageMode.JsonFiles,
                null);

            var orders = await File.ReadAllTextAsync(Path.Combine(dataRoot, "orders.json"));
            var settings = JsonSerializer.Deserialize<AppSettings>(
                await File.ReadAllTextAsync(Path.Combine(dataRoot, "settings.json")))!;
            Assert.Contains("order-1", orders);
            Assert.Equal(AppStorageMode.JsonFiles, settings.StorageMode);
            Assert.Equal("SQL Firma", settings.CompanyName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RestoreAsync_LocalBackupToLocal_KeepsTargetStorageMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-unified-backup-tests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "data");
        var sourceData = Path.Combine(root, "source");
        var backupRoot = Path.Combine(root, "backups");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(sourceData);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dataRoot, "settings.json"), JsonSerializer.Serialize(new AppSettings()));
            await File.WriteAllTextAsync(Path.Combine(sourceData, "settings.json"), JsonSerializer.Serialize(new AppSettings
            {
                StorageMode = AppStorageMode.JsonFiles,
                BackupDir = backupRoot,
                CompanyName = "Lokale Firma"
            }));
            await File.WriteAllTextAsync(Path.Combine(sourceData, "orders.json"), "[{\"Id\":\"local-1\"}]");
            var backupPath = await PreUpdateBackupService.CreateAsync(
                sourceData,
                "1.0.0",
                "manual",
                destinationDirectory: backupRoot,
                fileNamePurpose: "manual");

            await new UnifiedBackupRestoreService().RestoreAsync(
                backupPath,
                dataRoot,
                AppStorageMode.JsonFiles,
                null);

            var settings = JsonSerializer.Deserialize<AppSettings>(
                await File.ReadAllTextAsync(Path.Combine(dataRoot, "settings.json")))!;
            Assert.Equal(AppStorageMode.JsonFiles, settings.StorageMode);
            Assert.Equal("Lokale Firma", settings.CompanyName);
            Assert.Contains("local-1", await File.ReadAllTextAsync(Path.Combine(dataRoot, "orders.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void CreatePostgreSqlBackup(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "metadata.json", "{\"StorageMode\":\"PostgreSql\"}");
        foreach (var table in new[] { "orders", "tours", "employees", "vehicles", "tour_records", "calendar_manual_entries" })
        {
            var content = table == "orders"
                ? "[{\"id\":\"order-1\",\"payload\":{\"Id\":\"order-1\"},\"updated_at\":\"2026-10-09T00:00:00Z\"}]"
                : "[]";
            WriteEntry(archive, $"postgresql/{table}.json", content);
        }
        WriteEntry(
            archive,
            "postgresql/singletons.json",
            "[{\"key\":\"app_settings\",\"payload\":{\"CompanyName\":\"SQL Firma\",\"StorageMode\":1},\"updated_at\":\"2026-10-09T00:00:00Z\"}]");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
