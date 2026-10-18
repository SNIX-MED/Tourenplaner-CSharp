using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class BackupManagerTests
{
    [Fact]
    public async Task CreateBackupAsync_RejectsEmptyBackupDirectory()
    {
        var manager = new BackupManager();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.CreateBackupAsync("gawela", "config", "data", string.Empty, "full"));

        Assert.Equal("backupDirectory", exception.ParamName);
    }

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

    [Fact]
    public async Task CreateBackupAsync_WritesBackwardCompatibleVersionOneManifest()
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

            var manager = new BackupManager();
            var backupPath = await manager.CreateBackupAsync(
                "gawela",
                configDir,
                dataDir,
                backupDir,
                "full");

            using var archive = ZipFile.OpenRead(backupPath);
            var manifestEntry = archive.GetEntry("manifest.json");
            Assert.NotNull(manifestEntry);
            using var manifestStream = manifestEntry.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            var rootElement = manifest.RootElement;

            Assert.Equal(1, rootElement.GetProperty("BackupVersion").GetInt32());
            Assert.Equal("full", rootElement.GetProperty("BackupType").GetString());
            Assert.Equal("gawela", rootElement.GetProperty("AppName").GetString());
            Assert.Equal(configDir, rootElement.GetProperty("SourcePaths").GetProperty("config_dir").GetString());
            Assert.Equal(dataDir, rootElement.GetProperty("SourcePaths").GetProperty("data_dir").GetString());
            Assert.Equal(JsonValueKind.Array, rootElement.GetProperty("FileIndex").ValueKind);
            Assert.Equal(JsonValueKind.Array, rootElement.GetProperty("DeletedPaths").ValueKind);
            Assert.Equal(JsonValueKind.Null, rootElement.GetProperty("IncrementalBase").ValueKind);
            Assert.True(rootElement.TryGetProperty("CreatedAtIso", out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_ThrowsInvalidDataExceptionWhenManifestIsMissing()
    {
        var root = CreateTempRoot();
        var backupPath = Path.Combine(root, "missing-manifest.bak");

        try
        {
            CreateArchive(backupPath, manifestJson: null);
            var manager = new BackupManager();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                manager.RestoreBackupAsync(
                    backupPath,
                    Path.Combine(root, "restore-data"),
                    Path.Combine(root, "restore-config")));

            Assert.Equal("manifest.json missing in backup archive.", exception.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_ThrowsInvalidDataExceptionWhenManifestJsonIsMalformed()
    {
        var root = CreateTempRoot();
        var backupPath = Path.Combine(root, "malformed-manifest.bak");

        try
        {
            CreateArchive(backupPath, "{\"BackupVersion\":1,");
            var manager = new BackupManager();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                manager.RestoreBackupAsync(
                    backupPath,
                    Path.Combine(root, "restore-data"),
                    Path.Combine(root, "restore-config")));

            Assert.Equal("manifest.json is invalid.", exception.Message);
            Assert.IsType<JsonException>(exception.InnerException);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"BackupVersion\":2,\"SourcePaths\":{},\"FileIndex\":[],\"DeletedPaths\":[]}")]
    [InlineData("{\"BackupVersion\":1,\"SourcePaths\":null,\"FileIndex\":[],\"DeletedPaths\":[]}")]
    [InlineData("{\"BackupVersion\":1,\"SourcePaths\":{},\"FileIndex\":null,\"DeletedPaths\":[]}")]
    [InlineData("{\"BackupVersion\":1,\"SourcePaths\":{},\"FileIndex\":[],\"DeletedPaths\":null}")]
    public async Task RestoreBackupAsync_ThrowsInvalidDataExceptionWhenManifestStructureIsInvalid(
        string manifestJson)
    {
        var root = CreateTempRoot();
        var backupPath = Path.Combine(root, "invalid-manifest.bak");

        try
        {
            CreateArchive(backupPath, manifestJson);
            var manager = new BackupManager();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                manager.RestoreBackupAsync(
                    backupPath,
                    Path.Combine(root, "restore-data"),
                    Path.Combine(root, "restore-config")));

            Assert.Equal("manifest.json is invalid.", exception.Message);
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

    private static void CreateArchive(string backupPath, string? manifestJson)
    {
        using var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        if (manifestJson is null)
        {
            var dataEntry = archive.CreateEntry("data/employees.json");
            using var dataWriter = new StreamWriter(dataEntry.Open(), Encoding.UTF8);
            dataWriter.Write("[]");
            return;
        }

        var manifestEntry = archive.CreateEntry("manifest.json");
        using var manifestWriter = new StreamWriter(manifestEntry.Open(), Encoding.UTF8);
        manifestWriter.Write(manifestJson);
    }

    [Fact]
    public void CleanupOldBackups_DeletesFilesOlderThanRetention()
    {
        var root = CreateTempRoot();
        var backupDir = Path.Combine(root, "backups");
        Directory.CreateDirectory(backupDir);

        try
        {
            var oldFile = Path.Combine(backupDir, "old.bak");
            var newFile = Path.Combine(backupDir, "new.bak");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(newFile, "new");

            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow.AddDays(-1));

            var manager = new BackupManager();
            manager.CleanupOldBackups(backupDir, retentionDays: 5);

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
