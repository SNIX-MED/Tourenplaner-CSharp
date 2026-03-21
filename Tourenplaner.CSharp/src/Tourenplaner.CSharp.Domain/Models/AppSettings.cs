namespace Tourenplaner.CSharp.Domain.Models;

public sealed class AppSettings
{
    public string BaseDataPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string SqlConnectionString { get; set; } = string.Empty;
    public int AutoBackupRetentionDays { get; set; } = 14;
}
