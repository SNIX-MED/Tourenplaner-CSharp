using System.IO.Compression;

namespace Tourenplaner.CSharp.Application.Services;

internal static class BackupArchiveWriter
{
    public static void WriteSnapshotEntries(
        ZipArchive archive,
        IReadOnlyDictionary<string, string> fileMap,
        IReadOnlySet<string>? includePaths = null)
    {
        var includedPaths = includePaths ?? fileMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in includedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (fileMap.TryGetValue(archivePath, out var sourcePath) && File.Exists(sourcePath))
            {
                archive.CreateEntryFromFile(sourcePath, archivePath, CompressionLevel.Optimal);
            }
        }
    }

    public static void CopyEntriesWithoutMetadata(
        ZipArchive source,
        ZipArchive target,
        IReadOnlySet<string> changedPaths)
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

    public static void WriteLog(ZipArchive archive, IReadOnlyList<string> skippedMessages)
    {
        var entry = archive.CreateEntry("meta/log.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine($"Backup created at {DateTimeOffset.UtcNow:O}");
        foreach (var message in skippedMessages)
        {
            writer.WriteLine(message);
        }
    }
}
