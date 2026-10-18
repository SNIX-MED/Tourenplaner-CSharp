using System.IO.Compression;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class BackupManager
{
    public const int BackupVersion = 1;

    public static readonly IReadOnlyDictionary<string, string> RestoreLabels =
        new Dictionary<string, string>
        {
            ["orders"] = "Aufträge & Adressen",
            ["tours"] = "Liefertouren",
            ["employees"] = "Mitarbeiter",
            ["vehicles"] = "Fahrzeuge",
            ["settings"] = "Einstellungen",
            ["misc"] = "Zusatzdaten",
            ["other_data"] = "Weitere Daten"
        };

    public async Task<string> CreateBackupAsync(
        string appName,
        string configDirectory,
        string dataDirectory,
        string backupDirectory,
        string mode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new ArgumentException("Backup directory must not be empty.", nameof(backupDirectory));
        }

        var normalizedMode = string.Equals(mode, "incremental", StringComparison.OrdinalIgnoreCase)
            ? "incremental"
            : "full";
        var context = new BackupContext(appName, configDirectory, dataDirectory, backupDirectory);

        return normalizedMode == "incremental"
            ? await CreateIncrementalBackupAsync(context, cancellationToken).ConfigureAwait(false)
            : await CreateFullBackupAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public void CleanupOldBackups(string backupDirectory, int retentionDays)
    {
        if (!Directory.Exists(backupDirectory) || retentionDays <= 0)
        {
            return;
        }

        var threshold = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var backupFiles = Directory.GetFiles(backupDirectory, "*.bak", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(backupDirectory, "*.zip", SearchOption.TopDirectoryOnly));

        foreach (var file in backupFiles)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < threshold.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
            catch
            {
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
        var allowedGroups = selectedGroups is null ||
                            selectedGroups.Count == 0 ||
                            selectedGroups.Contains("all")
            ? null
            : new HashSet<string>(selectedGroups, StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(targetDataDirectory);
        Directory.CreateDirectory(targetConfigDirectory);

        await using var stream = new FileStream(
            backupPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifest = await BackupManifestProcessor.ReadAsync(archive, cancellationToken).ConfigureAwait(false);

        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.FullName, "meta/log.txt", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (allowedGroups is not null &&
                !allowedGroups.Contains(BackupPathMapper.ClassifyArchiveMember(entry.FullName)))
            {
                continue;
            }

            var destination = BackupPathMapper.ResolveRestoreDestination(
                entry.FullName,
                targetDataDirectory,
                targetConfigDirectory);
            if (destination is null)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        foreach (var deletedPath in manifest.DeletedPaths)
        {
            if (allowedGroups is not null &&
                !allowedGroups.Contains(BackupPathMapper.ClassifyArchiveMember(deletedPath)))
            {
                continue;
            }

            var destination = ResolveDeletedPath(
                deletedPath,
                targetDataDirectory,
                targetConfigDirectory);
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
            }
        }
    }

    private static async Task<string> CreateFullBackupAsync(
        BackupContext context,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.BackupDirectory);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var targetPath = Path.Combine(
            context.BackupDirectory,
            $"{context.AppName}_backup_FULL_{timestamp}.bak");
        var snapshot = BackupSnapshotProvider.Capture(context);
        var manifest = BackupManifestProcessor.Create(
            context,
            snapshot.FileIndex,
            "full",
            null,
            []);
        var temporaryPath = Path.Combine(
            context.BackupDirectory,
            $"{Guid.NewGuid():N}.bak.tmp");

        try
        {
            await using var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

            BackupArchiveWriter.WriteSnapshotEntries(archive, snapshot.FileMap);
            BackupManifestProcessor.Write(archive, manifest);
            BackupArchiveWriter.WriteLog(archive, snapshot.SkippedMessages);

            archive.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
            PromoteTempFile(temporaryPath, targetPath);
            return targetPath;
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static async Task<string> CreateIncrementalBackupAsync(
        BackupContext context,
        CancellationToken cancellationToken)
    {
        var latestBackupPath = FindLatestBackup(context.BackupDirectory);
        if (latestBackupPath is null)
        {
            return await CreateFullBackupAsync(context, cancellationToken).ConfigureAwait(false);
        }

        BackupManifest previousManifest;
        try
        {
            await using var stream = new FileStream(
                latestBackupPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            previousManifest = await BackupManifestProcessor.ReadAsync(archive, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return await CreateFullBackupAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var previousIndex = previousManifest.FileIndex
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .ToDictionary(entry => entry.Path, entry => entry, StringComparer.OrdinalIgnoreCase);
        var snapshot = BackupSnapshotProvider.Capture(context);
        var currentIndex = snapshot.FileIndex
            .ToDictionary(entry => entry.Path, entry => entry, StringComparer.OrdinalIgnoreCase);
        var changedPaths = currentIndex
            .Where(pair =>
                !previousIndex.TryGetValue(pair.Key, out var previousEntry) ||
                !string.Equals(
                    previousEntry.Sha256,
                    pair.Value.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deletedPaths = previousIndex.Keys
            .Where(path => !currentIndex.ContainsKey(path))
            .OrderBy(path => path)
            .ToList();
        var temporaryPath = Path.Combine(
            context.BackupDirectory,
            $"{Guid.NewGuid():N}.bak.tmp");

        try
        {
            await using var sourceStream = new FileStream(
                latestBackupPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var sourceArchive = new ZipArchive(
                sourceStream,
                ZipArchiveMode.Read,
                leaveOpen: false);
            await using var targetStream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using var targetArchive = new ZipArchive(
                targetStream,
                ZipArchiveMode.Create,
                leaveOpen: false);

            BackupArchiveWriter.CopyEntriesWithoutMetadata(
                sourceArchive,
                targetArchive,
                changedPaths);
            BackupArchiveWriter.WriteSnapshotEntries(
                targetArchive,
                snapshot.FileMap,
                changedPaths);
            var manifest = BackupManifestProcessor.Create(
                context,
                snapshot.FileIndex,
                "incremental",
                Path.GetFileName(latestBackupPath),
                deletedPaths);
            BackupManifestProcessor.Write(targetArchive, manifest);
            BackupArchiveWriter.WriteLog(targetArchive, snapshot.SkippedMessages);

            targetArchive.Dispose();
            await targetStream.DisposeAsync().ConfigureAwait(false);
            sourceArchive.Dispose();
            await sourceStream.DisposeAsync().ConfigureAwait(false);

            PromoteTempFile(temporaryPath, latestBackupPath);
            return latestBackupPath;
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static string? ResolveDeletedPath(
        string archivePath,
        string targetDataDirectory,
        string targetConfigDirectory)
    {
        if (!archivePath.StartsWith("data_root/", StringComparison.OrdinalIgnoreCase) &&
            !archivePath.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return BackupPathMapper.ResolveRestoreDestination(
            archivePath,
            targetDataDirectory,
            targetConfigDirectory);
    }

    private static string? FindLatestBackup(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return null;
        }

        return Directory.GetFiles(backupDirectory, "*.bak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void PromoteTempFile(string temporaryPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            File.Replace(
                temporaryPath,
                targetPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, targetPath);
    }

    private static void DeleteTemporaryFile(string temporaryPath)
    {
        if (!File.Exists(temporaryPath))
        {
            return;
        }

        try
        {
            File.Delete(temporaryPath);
        }
        catch
        {
        }
    }
}
