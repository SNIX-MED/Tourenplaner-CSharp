using System.Security.Cryptography;

namespace Tourenplaner.CSharp.Application.Services;

internal static class BackupSnapshotProvider
{
    private static readonly string[] ExcludePatterns = ["*.key", "*token*", "secrets.json"];

    private static readonly IReadOnlyDictionary<string, string> RootMutableFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["settings.json"] = "config/settings.json",
            ["pins.json"] = "data_root/pins.json",
            ["tours.json"] = "data_root/tours.json",
            ["config.json"] = "data_root/config.json"
        };

    public static BackupSnapshot Capture(BackupContext context)
    {
        var fileIndex = new List<BackupFileIndexEntry>();
        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skippedMessages = new List<string>();

        foreach (var (archivePath, sourcePath) in ScanFiles(context))
        {
            try
            {
                var info = new FileInfo(sourcePath);
                fileIndex.Add(new BackupFileIndexEntry(
                    archivePath,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    ComputeSha256(sourcePath)));
                fileMap[archivePath] = sourcePath;
            }
            catch (UnauthorizedAccessException)
            {
                skippedMessages.Add($"Permission denied: {sourcePath}");
            }
            catch (Exception ex)
            {
                skippedMessages.Add($"Skipped {sourcePath}: {ex.Message}");
            }
        }

        return new BackupSnapshot(
            fileIndex.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            fileMap,
            skippedMessages);
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
            var relativePath = Path.GetRelativePath(context.DataDirectory, file).Replace('\\', '/');
            if (IsIncluded(relativePath))
            {
                yield return ($"data/{relativePath}", file);
            }
        }
    }

    private static bool IsIncluded(string relativePath) =>
        !ExcludePatterns.Any(pattern => MatchesPattern(relativePath, pattern));

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
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
