using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

internal sealed class UnifiedBackupRestoreService
{
    private static readonly Dictionary<string, string> LocalFileToPostgreSqlTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orders.json"] = "orders",
        ["tours.json"] = "tour_records",
        ["employees.json"] = "employees",
        ["vehicles.json"] = "vehicles",
        ["kalender-manuelle-eintraege.json"] = "calendar_manual_entries"
    };

    public async Task RestoreAsync(
        string backupPath,
        string dataRoot,
        AppStorageMode targetMode,
        PostgreSqlStorageSettings? targetPostgreSqlSettings,
        CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        var sourceMode = ReadSourceMode(archive);

        if (targetMode == AppStorageMode.JsonFiles)
        {
            await RestoreToLocalAsync(archive, dataRoot, sourceMode, cancellationToken);
            return;
        }

        if (targetPostgreSqlSettings?.IsConfigured() != true)
        {
            throw new InvalidDataException("Die PostgreSQL-Zieleinstellungen sind unvollständig.");
        }

        if (sourceMode == AppStorageMode.PostgreSql)
        {
            await new PostgreSqlBackupRestoreService().RestoreAsync(
                backupPath,
                targetPostgreSqlSettings,
                cancellationToken);
            return;
        }

        var convertedPath = await ConvertLocalBackupToPostgreSqlBackupAsync(
            archive,
            targetPostgreSqlSettings,
            cancellationToken);
        try
        {
            await new PostgreSqlBackupRestoreService().RestoreAsync(
                convertedPath,
                targetPostgreSqlSettings,
                cancellationToken);
        }
        finally
        {
            File.Delete(convertedPath);
        }
    }

    public async Task RestoreLegacyBakAsync(
        string backupPath,
        string dataRoot,
        AppStorageMode targetMode,
        PostgreSqlStorageSettings? targetPostgreSqlSettings,
        CancellationToken cancellationToken = default)
    {
        var conversionRoot = Path.Combine(Path.GetTempPath(), $"tourenplaner-legacy-backup-{Guid.NewGuid():N}");
        var convertedDataRoot = Path.Combine(conversionRoot, "data");
        Directory.CreateDirectory(convertedDataRoot);
        try
        {
            await new BackupManager().RestoreBackupAsync(
                backupPath,
                convertedDataRoot,
                convertedDataRoot,
                selectedGroups: ["all"],
                cancellationToken);
            var settingsRepository = new JsonAppSettingsRepository(Path.Combine(convertedDataRoot, "settings.json"));
            var settings = await settingsRepository.LoadAsync(cancellationToken);
            settings.StorageMode = AppStorageMode.JsonFiles;
            await settingsRepository.SaveAsync(settings, cancellationToken);
            var convertedBackup = await PreUpdateBackupService.CreateAsync(
                convertedDataRoot,
                "legacy",
                "converted",
                cancellationToken,
                conversionRoot,
                "legacy-converted");
            await RestoreAsync(
                convertedBackup,
                dataRoot,
                targetMode,
                targetPostgreSqlSettings,
                cancellationToken);
        }
        finally
        {
            if (Directory.Exists(conversionRoot))
            {
                Directory.Delete(conversionRoot, recursive: true);
            }
        }
    }

    internal static AppStorageMode ReadSourceMode(string backupPath)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        return ReadSourceMode(archive);
    }

    private static AppStorageMode ReadSourceMode(ZipArchive archive)
    {
        var entry = archive.GetEntry("metadata.json")
            ?? throw new InvalidDataException("metadata.json fehlt in der Sicherung.");
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("StorageMode", out var modeElement) ||
            !Enum.TryParse<AppStorageMode>(modeElement.GetString(), ignoreCase: true, out var mode))
        {
            throw new InvalidDataException("Der Speichermodus der Sicherung ist ungültig.");
        }

        return mode;
    }

    private static async Task RestoreToLocalAsync(
        ZipArchive archive,
        string dataRoot,
        AppStorageMode sourceMode,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataRoot);
        var currentSettingsRepository = new JsonAppSettingsRepository(Path.Combine(dataRoot, "settings.json"));
        var currentSettings = await currentSettingsRepository.LoadAsync(cancellationToken);

        await ExtractLocalDataAsync(archive, dataRoot, cancellationToken);
        if (sourceMode == AppStorageMode.PostgreSql)
        {
            foreach (var mapping in LocalFileToPostgreSqlTable)
            {
                await WritePayloadArrayAsync(
                    archive,
                    mapping.Value,
                    Path.Combine(dataRoot, mapping.Key),
                    cancellationToken);
            }

            var sharedSettings = await ReadPostgreSqlSettingsPayloadAsync(archive, cancellationToken);
            if (sharedSettings is not null)
            {
                sharedSettings.StorageMode = AppStorageMode.JsonFiles;
                sharedSettings.PostgreSqlStorage = currentSettings.PostgreSqlStorage;
                await currentSettingsRepository.SaveAsync(sharedSettings, cancellationToken);
                return;
            }
        }

        var restoredSettings = await currentSettingsRepository.LoadAsync(cancellationToken);
        restoredSettings.StorageMode = AppStorageMode.JsonFiles;
        restoredSettings.PostgreSqlStorage = currentSettings.PostgreSqlStorage;
        await currentSettingsRepository.SaveAsync(restoredSettings, cancellationToken);
    }

    private static async Task ExtractLocalDataAsync(
        ZipArchive archive,
        string dataRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries.Where(x => x.FullName.StartsWith("local-data/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Name)))
        {
            var relativePath = entry.FullName["local-data/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(dataRoot, relativePath));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Die Sicherung enthält einen ungültigen Dateipfad.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static async Task WritePayloadArrayAsync(
        ZipArchive archive,
        string table,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry($"postgresql/{table}.json")
            ?? throw new InvalidDataException($"postgresql/{table}.json fehlt in der Sicherung.");
        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var payloads = document.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("payload").Clone())
            .ToArray();
        await File.WriteAllTextAsync(
            destinationPath,
            JsonSerializer.Serialize(payloads, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static async Task<AppSettings?> ReadPostgreSqlSettingsPayloadAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("postgresql/singletons.json");
        if (entry is null)
        {
            return null;
        }

        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var row = document.RootElement.EnumerateArray().FirstOrDefault(x =>
            x.TryGetProperty("key", out var key) && key.GetString() == "app_settings");
        return row.ValueKind == JsonValueKind.Undefined
            ? null
            : row.GetProperty("payload").Deserialize<AppSettings>();
    }

    private static async Task<string> ConvertLocalBackupToPostgreSqlBackupAsync(
        ZipArchive source,
        PostgreSqlStorageSettings targetSettings,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tourenplaner-local-to-postgresql-{Guid.NewGuid():N}.zip");
        using var target = ZipFile.Open(path, ZipArchiveMode.Create);
        await WriteJsonEntryAsync(target, "metadata.json", new { StorageMode = AppStorageMode.PostgreSql.ToString() }, cancellationToken);

        foreach (var table in new[] { "orders", "tours", "employees", "vehicles", "tour_records", "calendar_manual_entries" })
        {
            var localFile = LocalFileToPostgreSqlTable.FirstOrDefault(x => x.Value == table).Key;
            var rows = string.IsNullOrWhiteSpace(localFile)
                ? new JsonArray()
                : await BuildRowsFromLocalEntryAsync(source, localFile, cancellationToken);
            await WriteJsonEntryAsync(target, $"postgresql/{table}.json", rows, cancellationToken);
        }

        var settings = await ReadLocalSettingsAsync(source, cancellationToken) ?? new AppSettings();
        settings.StorageMode = AppStorageMode.PostgreSql;
        settings.PostgreSqlStorage = targetSettings;
        var singletonRows = new JsonArray(CreateRow("app_settings", JsonSerializer.SerializeToNode(settings)!));
        await WriteJsonEntryAsync(target, "postgresql/singletons.json", singletonRows, cancellationToken);
        return path;
    }

    private static async Task<JsonArray> BuildRowsFromLocalEntryAsync(
        ZipArchive archive,
        string fileName,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry($"local-data/{fileName}");
        if (entry is null)
        {
            return new JsonArray();
        }

        await using var stream = entry.Open();
        var array = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonArray
            ?? throw new InvalidDataException($"{fileName} enthält keine gültige Liste.");
        var rows = new JsonArray();
        foreach (var item in array)
        {
            if (item is not JsonObject payload)
            {
                continue;
            }

            var id = payload.FirstOrDefault(x => string.Equals(x.Key, "Id", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                rows.Add(CreateRow(id, payload.DeepClone()));
            }
        }

        return rows;
    }

    private static async Task<AppSettings?> ReadLocalSettingsAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("local-data/settings.json");
        if (entry is null)
        {
            return null;
        }

        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken);
    }

    private static JsonObject CreateRow(string key, JsonNode payload)
    {
        return new JsonObject
        {
            ["id"] = key,
            ["key"] = key,
            ["payload"] = payload,
            ["updated_at"] = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static async Task WriteJsonEntryAsync(
        ZipArchive archive,
        string name,
        object value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, value.GetType(), cancellationToken: cancellationToken);
    }
}
