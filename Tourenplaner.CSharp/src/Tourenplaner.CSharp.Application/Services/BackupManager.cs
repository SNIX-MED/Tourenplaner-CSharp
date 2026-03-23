using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class BackupManager
{
    public const int BackupVersion = 1;

    private static readonly string[] ExcludePatterns = ["*.key", "*token*", "secrets.json"];

    private static readonly Dictionary<string, string> RootMutableFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["settings.json"] = "config/settings.json",
        ["pins.json"] = "data_root/pins.json",
        ["tours.json"] = "data_root/tours.json",
        ["config.json"] = "data_root/config.json"
    };

    public static readonly IReadOnlyDictionary<string, string> RestoreLabels = new Dictionary<string, string>
    {
        ["orders"] = "Auftraege & Adressen",
        ["tours"] = "Liefertouren",
        ["employees"] = "Mitarbeiter",
        ["vehicles"] = "Fahrzeuge",
        ["settings"] = "Einstellungen",
        ["misc"] = "Zusatzdaten",
        ["other_data"] = "Weitere Daten"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<string> CreateBackupAsync(
        string appName,
        string configDirectory,
        string dataDirectory,
        string backupDirectory,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var normalizedMode = string.Equals(mode, "incremental", StringComparison.OrdinalIgnoreCase) ? "incremental" : "full";
        var context = new BackupContext(appName, configDirectory, dataDirectory, backupDirectory);

        return normalizedMode == "incremental"
            ? await CreateIncrementalBackupAsync(context, cancellationToken)
            : await CreateFullBackupAsync(context, cancellationToken);
    }

    public void CleanupOldBackups(string backupDirectory, int retentionDays)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        if (retentionDays <= 0)
        {
            return;
        }

        var threshold = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        foreach (var file in Directory.GetFiles(backupDirectory, "*.bak", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var modified = File.GetLastWriteTimeUtc(file);
                if (modified < threshold.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    public async Task RestoreBackupAsync(
        string backupPath,
        string targetDataDirectory,
        string targetConfigDirectory,
        IReadOnlyCollection<string>? selectedGroups = null,
        CancellationToken cancellationToken = default)
    {
        var allowedGroups = selectedGroups is null || selectedGroups.Count == 0 || selectedGroups.Contains("all")
            ? null
            : new HashSet<string>(selectedGroups, StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(targetDataDirectory);
        Directory.CreateDirectory(targetConfigDirectory);

        await using var stream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var manifest = await ReadManifestAsync(archive, cancellationToken);

        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.FullName, "meta/log.txt", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (allowedGroups is not null && !allowedGroups.Contains(ClassifyArchiveMember(entry.FullName)))
            {
                continue;
            }

            string? destination = null;
            if (string.Equals(entry.FullName, "config/settings.json", StringComparison.OrdinalIgnoreCase))
            {
                destination = Path.Combine(targetConfigDirectory, "settings.json");
            }
            else if (entry.FullName.StartsWith("data_root/", StringComparison.OrdinalIgnoreCase))
            {
                destination = Path.Combine(targetConfigDirectory, entry.FullName["data_root/".Length..].Replace('/', Path.DirectorySeparatorChar));
            }
            else if (entry.FullName.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            {
                destination = Path.Combine(targetDataDirectory, entry.FullName["data/".Length..].Replace('/', Path.DirectorySeparatorChar));
            }

            if (destination is null)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target, cancellationToken);
        }

        foreach (var deleted in manifest.DeletedPaths)
        {
            if (allowedGroups is not null && !allowedGroups.Contains(ClassifyArchiveMember(deleted)))
            {
                continue;
            }

            string? destination = null;
            if (deleted.StartsWith("data_root/", StringComparison.OrdinalIgnoreCase))
            {
                destination = Path.Combine(targetConfigDirectory, deleted["data_root/".Length..].Replace('/', Path.DirectorySeparatorChar));
            }
            else if (deleted.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            {
                destination = Path.Combine(targetDataDirectory, deleted["data/".Length..].Replace('/', Path.DirectorySeparatorChar));
            }

            if (destination is null || !File.Exists(destination))
            {
                continue;
            }

            try
            {
                File.Delete(destination);
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task<string> CreateFullBackupAsync(BackupContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.BackupDirectory);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var target = Path.Combine(context.BackupDirectory, $"{context.AppName}_backup_FULL_{timestamp}.bak");

        var snapshot = BuildSnapshot(context);
        var manifest = BuildManifest(context, snapshot.FileIndex, "full", null, []);
        var tempPath = Path.Combine(context.BackupDirectory, $"{Guid.NewGuid():N}.bak.tmp");

        try
        {
            await using var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
            WriteSnapshotEntries(archive, snapshot.FileMap);
            WriteManifest(archive, manifest);
            WriteLog(archive, snapshot.SkippedMessages);
            archive.Dispose();
            fs.Dispose();

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(tempPath, target);
            return target;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private async Task<string> CreateIncrementalBackupAsync(BackupContext context, CancellationToken cancellationToken)
    {
        var latest = FindLatestBackup(context.BackupDirectory);
        if (latest is null)
        {
            return await CreateFullBackupAsync(context, cancellationToken);
        }

        BackupManifest previousManifest;
        try
        {
            await using var latestStream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var latestArchive = new ZipArchive(latestStream, ZipArchiveMode.Read, leaveOpen: false);
            previousManifest = await ReadManifestAsync(latestArchive, cancellationToken);
        }
        catch
        {
            return await CreateFullBackupAsync(context, cancellationToken);
        }

        var previousIndex = previousManifest.FileIndex
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .ToDictionary(x => x.Path, x => x, StringComparer.OrdinalIgnoreCase);

        var snapshot = BuildSnapshot(context);
        var currentIndex = snapshot.FileIndex.ToDictionary(x => x.Path, x => x, StringComparer.OrdinalIgnoreCase);

        var changed = currentIndex
            .Where(kv => !previousIndex.TryGetValue(kv.Key, out var prev) || !string.Equals(prev.Sha256, kv.Value.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deleted = previousIndex.Keys.Where(key => !currentIndex.ContainsKey(key)).OrderBy(x => x).ToList();

        var tempPath = Path.Combine(context.BackupDirectory, $"{Guid.NewGuid():N}.bak.tmp");

        try
        {
            await using var sourceStream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);

            await using var targetStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Create, leaveOpen: false);

            CopyZipWithoutMeta(sourceArchive, targetArchive, changed);
            WriteSnapshotEntries(targetArchive, snapshot.FileMap, changed);
            var manifest = BuildManifest(context, snapshot.FileIndex, "incremental", Path.GetFileName(latest), deleted);
            WriteManifest(targetArchive, manifest);
            WriteLog(targetArchive, snapshot.SkippedMessages);

            targetArchive.Dispose();
            targetStream.Dispose();
            sourceArchive.Dispose();
            sourceStream.Dispose();

            File.Delete(latest);
            File.Move(tempPath, latest);
            return latest;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static BackupSnapshot BuildSnapshot(BackupContext context)
    {
        var fileIndex = new List<BackupFileIndexEntry>();
        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();

        foreach (var (archivePath, sourcePath) in ScanFiles(context))
        {
            try
            {
                var info = new FileInfo(sourcePath);
                var hash = ComputeSha256(sourcePath);
                fileIndex.Add(new BackupFileIndexEntry(archivePath, info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(), hash));
                fileMap[archivePath] = sourcePath;
            }
            catch (UnauthorizedAccessException)
            {
                skipped.Add($"Permission denied: {sourcePath}");
            }
            catch (Exception ex)
            {
                skipped.Add($"Skipped {sourcePath}: {ex.Message}");
            }
        }

        fileIndex = fileIndex.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        return new BackupSnapshot(fileIndex, fileMap, skipped);
    }

    private static IEnumerable<(string ArchivePath, string SourcePath)> ScanFiles(BackupContext context)
    {
        foreach (var (rootName, archivePath) in RootMutableFiles)
        {
            var file = Path.Combine(context.ConfigDirectory, rootName);
            if (File.Exists(file) && IsIncluded(Path.GetFileName(file)))
            {
                yield return (archivePath, file);
            }
        }

        if (!Directory.Exists(context.DataDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(context.DataDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(context.DataDirectory, file).Replace('\\', '/');
            if (!IsIncluded(relative))
            {
                continue;
            }

            yield return ($"data/{relative}", file);
        }
    }

    private static bool IsIncluded(string relativePath)
    {
        return !ExcludePatterns.Any(pattern => MatchesPattern(relativePath, pattern));
    }

    private static bool MatchesPattern(string text, string pattern)
    {
        if (pattern == "*.key")
        {
            return text.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
        }

        if (pattern == "*token*")
        {
            return text.Contains("token", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static BackupManifest BuildManifest(
        BackupContext context,
        IReadOnlyList<BackupFileIndexEntry> fileIndex,
        string mode,
        string? baseBackup,
        IReadOnlyList<string> deletedPaths)
    {
        return new BackupManifest
        {
            BackupVersion = BackupVersion,
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
    }

    private static void WriteManifest(ZipArchive archive, BackupManifest manifest)
    {
        var entry = archive.CreateEntry("manifest.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static async Task<BackupManifest> ReadManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("manifest.json missing in backup archive.");
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken);
        return manifest ?? throw new InvalidDataException("manifest.json is invalid.");
    }

    private static void WriteLog(ZipArchive archive, IReadOnlyList<string> skippedMessages)
    {
        var entry = archive.CreateEntry("meta/log.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine($"Backup created at {DateTimeOffset.UtcNow:O}");
        foreach (var line in skippedMessages)
        {
            writer.WriteLine(line);
        }
    }

    private static void WriteSnapshotEntries(ZipArchive archive, IReadOnlyDictionary<string, string> fileMap, IReadOnlySet<string>? includePaths = null)
    {
        var include = includePaths is null
            ? fileMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : includePaths;

        foreach (var archivePath in include.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!fileMap.TryGetValue(archivePath, out var sourcePath) || !File.Exists(sourcePath))
            {
                continue;
            }

            archive.CreateEntryFromFile(sourcePath, archivePath, CompressionLevel.Optimal);
        }
    }

    private static void CopyZipWithoutMeta(ZipArchive source, ZipArchive target, IReadOnlySet<string> changedPaths)
    {
        foreach (var entry in source.Entries)
        {
            var path = entry.FullName;
            if (string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "meta/log.txt", StringComparison.OrdinalIgnoreCase) ||
                changedPaths.Contains(path))
            {
                continue;
            }

            var copy = target.CreateEntry(path, CompressionLevel.Optimal);
            using var sourceStream = entry.Open();
            using var targetStream = copy.Open();
            sourceStream.CopyTo(targetStream);
        }
    }

    private static string? FindLatestBackup(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return null;
        }

        return Directory.GetFiles(backupDirectory, "*.bak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(x => File.GetLastWriteTimeUtc(x))
            .FirstOrDefault();
    }

    private static string ClassifyArchiveMember(string member)
    {
        var path = (member ?? string.Empty).Replace('\\', '/');
        if (path == "config/settings.json") return "settings";
        if (path == "data_root/pins.json") return "orders";
        if (path == "data_root/tours.json") return "tours";
        if (path == "data_root/config.json") return "misc";
        if (path == "data/employees.json") return "employees";
        if (path == "data/vehicles.json") return "vehicles";
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) return "other_data";
        return "misc";
    }

    private sealed record BackupContext(string AppName, string ConfigDirectory, string DataDirectory, string BackupDirectory);

    private sealed record BackupSnapshot(
        List<BackupFileIndexEntry> FileIndex,
        Dictionary<string, string> FileMap,
        List<string> SkippedMessages);

    private sealed class BackupManifest
    {
        public int BackupVersion { get; set; }
        public string CreatedAtIso { get; set; } = string.Empty;
        public string BackupType { get; set; } = "full";
        public string AppName { get; set; } = string.Empty;
        public Dictionary<string, string> SourcePaths { get; set; } = new();
        public List<BackupFileIndexEntry> FileIndex { get; set; } = new();
        public List<string> DeletedPaths { get; set; } = new();
        public string? IncrementalBase { get; set; }
    }

    private sealed record BackupFileIndexEntry(string Path, long Size, long MTime, string Sha256);
}
