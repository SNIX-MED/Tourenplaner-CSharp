namespace Tourenplaner.CSharp.Application.Services;

internal sealed record BackupContext(
    string AppName,
    string ConfigDirectory,
    string DataDirectory,
    string BackupDirectory);

internal sealed record BackupSnapshot(
    IReadOnlyList<BackupFileIndexEntry> FileIndex,
    IReadOnlyDictionary<string, string> FileMap,
    IReadOnlyList<string> SkippedMessages);

internal sealed class BackupManifest
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

internal sealed record BackupFileIndexEntry(
    string Path,
    long Size,
    long MTime,
    string Sha256);
