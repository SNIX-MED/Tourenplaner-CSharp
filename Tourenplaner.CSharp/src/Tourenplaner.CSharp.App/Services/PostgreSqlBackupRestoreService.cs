using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Npgsql;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.Services;

internal sealed class PostgreSqlBackupRestoreService
{
    private static readonly string[] Tables =
    [
        "orders",
        "tours",
        "employees",
        "vehicles",
        "tour_records",
        "calendar_manual_entries",
        "singletons"
    ];

    public async Task RestoreAsync(
        string backupPath,
        PostgreSqlStorageSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentNullException.ThrowIfNull(settings);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Die ausgewählte Sicherung wurde nicht gefunden.", backupPath);
        }

        using var archive = ZipFile.OpenRead(backupPath);
        ValidateArchive(archive);

        await using var connection = new PostgreSqlConnectionFactory().CreateConnection(settings);
        await connection.OpenAsync(cancellationToken);
        await new PostgreSqlSchemaInitializer().EnsureSchemaAsync(connection, settings, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(settings.Schema);

        foreach (var table in Tables)
        {
            await using var lockCommand = connection.CreateCommand();
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = $"LOCK TABLE \"{schema}\".\"{table}\" IN ACCESS EXCLUSIVE MODE;";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var table in Tables)
        {
            var entry = archive.GetEntry($"postgresql/{table}.json")!;
            await using var entryStream = entry.Open();
            using var document = await JsonDocument.ParseAsync(entryStream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Die Sicherungsdatei für {table} ist ungültig.");
            }

            await using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = $"DELETE FROM \"{schema}\".\"{table}\";";
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var row in document.RootElement.EnumerateArray())
            {
                var keyName = table == "singletons" ? "key" : "id";
                if (!row.TryGetProperty(keyName, out var keyElement) ||
                    !row.TryGetProperty("payload", out var payloadElement) ||
                    !row.TryGetProperty("updated_at", out var updatedAtElement))
                {
                    throw new InvalidDataException($"In der Sicherungstabelle {table} fehlen Pflichtfelder.");
                }

                var key = keyElement.GetString();
                var updatedAtText = updatedAtElement.GetString();
                if (string.IsNullOrWhiteSpace(key) ||
                    !DateTimeOffset.TryParse(updatedAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
                {
                    throw new InvalidDataException($"In der Sicherungstabelle {table} ist ein Schlüssel oder Zeitstempel ungültig.");
                }

                await using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = $"""
                    INSERT INTO "{schema}"."{table}" ("{keyName}", payload, updated_at)
                    VALUES (@key, CAST(@payload AS jsonb), @updatedAt);
                    """;
                insertCommand.Parameters.AddWithValue("key", key);
                insertCommand.Parameters.AddWithValue("payload", payloadElement.GetRawText());
                insertCommand.Parameters.AddWithValue("updatedAt", updatedAt);
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    internal static void ValidateBackupFile(string backupPath)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        ValidateArchive(archive);
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        var metadataEntry = archive.GetEntry("metadata.json")
            ?? throw new InvalidDataException("metadata.json fehlt in der Sicherung.");
        using (var stream = metadataEntry.Open())
        using (var document = JsonDocument.Parse(stream))
        {
            if (!document.RootElement.TryGetProperty("StorageMode", out var storageMode) ||
                !string.Equals(storageMode.GetString(), AppStorageMode.PostgreSql.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Die ausgewählte Datei ist keine PostgreSQL-Sicherung.");
            }
        }

        foreach (var table in Tables)
        {
            if (archive.GetEntry($"postgresql/{table}.json") is null)
            {
                throw new InvalidDataException($"Die Sicherung ist unvollständig: postgresql/{table}.json fehlt.");
            }
        }
    }
}
