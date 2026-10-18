using System.IO.Compression;
using System.Text.Json;

namespace Tourenplaner.CSharp.Application.Services;

internal static class BackupManifestProcessor
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static BackupManifest Create(
        BackupContext context,
        IReadOnlyList<BackupFileIndexEntry> fileIndex,
        string mode,
        string? baseBackup,
        IReadOnlyList<string> deletedPaths) =>
        new()
        {
            BackupVersion = BackupManager.BackupVersion,
            CreatedAtIso = DateTimeOffset.UtcNow.ToString("O"),
            BackupType = mode,
            AppName = context.AppName,
            SourcePaths = new Dictionary<string, string>
            {
                ["config_dir"] = context.ConfigDirectory,
                ["data_dir"] = context.DataDirectory
            },
            FileIndex = fileIndex.ToList(),
            DeletedPaths = deletedPaths.ToList(),
            IncrementalBase = baseBackup
        };

    public static void Write(ZipArchive archive, BackupManifest manifest)
    {
        var entry = archive.CreateEntry("manifest.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(manifest, SerializerOptions));
    }

    public static async Task<BackupManifest> ReadAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("manifest.json") ??
                    throw new InvalidDataException("manifest.json missing in backup archive.");

        try
        {
            await using var stream = entry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (manifest is null ||
                manifest.BackupVersion != BackupManager.BackupVersion ||
                manifest.FileIndex is null ||
                manifest.DeletedPaths is null ||
                manifest.SourcePaths is null)
            {
                throw new InvalidDataException("manifest.json is invalid.");
            }

            return manifest;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("manifest.json is invalid.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException("manifest.json is invalid.", ex);
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
