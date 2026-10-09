using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Data;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.Services;

internal static class PreUpdateBackupService
{
    private static readonly string[] PostgreSqlTables =
    [
        "orders",
        "tours",
        "employees",
        "vehicles",
        "tour_records",
        "calendar_manual_entries",
        "singletons"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<string> CreateAsync(
        string dataRoot,
        string currentVersion,
        string targetVersion,
        CancellationToken cancellationToken = default,
        string? destinationDirectory = null,
        string fileNamePurpose = "pre-update")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        string? tempPath = null;

        try
        {
            var settingsPath = Path.Combine(dataRoot, "settings.json");
            var bootstrapSettings = await new JsonAppSettingsRepository(settingsPath).LoadAsync(cancellationToken);
            var settings = await LoadEffectiveSettingsAsync(bootstrapSettings, cancellationToken);
            var backupDirectory = string.IsNullOrWhiteSpace(destinationDirectory)
                ? ResolveBackupDirectory(settings.BackupDir)
                : Path.GetFullPath(destinationDirectory.Trim());
            Directory.CreateDirectory(backupDirectory);

            var safeCurrentVersion = SanitizeFileNamePart(currentVersion);
            var safeTargetVersion = SanitizeFileNamePart(targetVersion);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            var machineName = SanitizeFileNamePart(Environment.MachineName);
            var backupPath = Path.Combine(
                backupDirectory,
                $"GAWELA-Tourenplaner_{SanitizeFileNamePart(fileNamePurpose)}_{safeCurrentVersion}_to_{safeTargetVersion}_{machineName}_{timestamp}.zip");
            tempPath = backupPath + ".tmp";

            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteMetadataAsync(archive, settings.StorageMode, currentVersion, targetVersion, cancellationToken);
                await WriteLocalDataAsync(archive, dataRoot, cancellationToken);

                if (settings.StorageMode == AppStorageMode.PostgreSql)
                {
                    if (settings.PostgreSqlStorage is null || !settings.PostgreSqlStorage.IsConfigured())
                    {
                        throw new InvalidDataException("Die PostgreSQL-Einstellungen sind unvollständig.");
                    }

                    await WritePostgreSqlDataAsync(archive, settings.PostgreSqlStorage, cancellationToken);
                }
            }

            File.Move(tempPath, backupPath);
            return backupPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                TryDelete(tempPath);
            }
            throw new InvalidDataException(
                $"Vor dem Update konnte keine Sicherheitskopie erstellt werden: {ex.Message}",
                ex);
        }
    }

    private static async Task<AppSettings> LoadEffectiveSettingsAsync(
        AppSettings bootstrapSettings,
        CancellationToken cancellationToken)
    {
        if (bootstrapSettings.StorageMode != AppStorageMode.PostgreSql ||
            bootstrapSettings.PostgreSqlStorage is null ||
            !bootstrapSettings.PostgreSqlStorage.IsConfigured())
        {
            return bootstrapSettings;
        }

        var sharedSettings = await new PostgreSqlAppSettingsRepository(bootstrapSettings.PostgreSqlStorage)
            .LoadAsync(cancellationToken);
        sharedSettings.PostgreSqlStorage = bootstrapSettings.PostgreSqlStorage;
        sharedSettings.StorageMode = AppStorageMode.PostgreSql;
        return sharedSettings;
    }

    private static string ResolveBackupDirectory(string? configuredDirectory)
    {
        var root = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tourenplaner.CSharp",
                "backups")
            : configuredDirectory.Trim();
        return Path.Combine(root, "pre-update");
    }

    private static async Task WriteMetadataAsync(
        ZipArchive archive,
        AppStorageMode storageMode,
        string currentVersion,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry("metadata.json", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, new
        {
            FormatVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CurrentVersion = currentVersion,
            TargetVersion = targetVersion,
            StorageMode = storageMode.ToString()
        }, JsonOptions, cancellationToken);
    }

    private static async Task WriteLocalDataAsync(
        ZipArchive archive,
        string dataRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dataRoot))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(dataRoot, filePath).Replace('\\', '/');
            var entry = archive.CreateEntry($"local-data/{relativePath}", CompressionLevel.Optimal);
            await using var source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            await using var destination = entry.Open();
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static async Task WritePostgreSqlDataAsync(
        ZipArchive archive,
        PostgreSqlStorageSettings settings,
        CancellationToken cancellationToken)
    {
        await using var connection = new PostgreSqlConnectionFactory().CreateConnection(settings);
        await connection.OpenAsync(cancellationToken);
        await new PostgreSqlSchemaInitializer().EnsureSchemaAsync(connection, settings, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(settings.Schema);

        foreach (var table in PostgreSqlTables)
        {
            var entry = archive.CreateEntry($"postgresql/{table}.json", CompressionLevel.Optimal);
            await using var destination = entry.Open();
            await using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync("[".AsMemory(), cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT to_jsonb(row_data)::text FROM \"{schema}\".\"{table}\" AS row_data ORDER BY 1;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var first = true;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!first)
                {
                    await writer.WriteAsync(",".AsMemory(), cancellationToken);
                }

                await writer.WriteAsync(reader.GetString(0).AsMemory(), cancellationToken);
                first = false;
            }

            await writer.WriteAsync("]".AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string SanitizeFileNamePart(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalidCharacter, '-');
        }

        return normalized;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
