using System.IO.Compression;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class BackupManager
{
    public async Task<string> CreateBackupAsync(
        string sourceDirectory,
        string backupRootDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source data directory not found: {sourceDirectory}");
        }

        Directory.CreateDirectory(backupRootDirectory);

        var backupName = $"tourenplaner-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        var backupPath = Path.Combine(backupRootDirectory, backupName);

        await Task.Run(() => ZipFile.CreateFromDirectory(sourceDirectory, backupPath), cancellationToken);
        return backupPath;
    }

    public void PurgeOldBackups(string backupRootDirectory, int retentionDays)
    {
        if (!Directory.Exists(backupRootDirectory))
        {
            return;
        }

        var thresholdUtc = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var file in Directory.GetFiles(backupRootDirectory, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var created = File.GetCreationTimeUtc(file);
            if (created < thresholdUtc)
            {
                File.Delete(file);
            }
        }
    }
}
