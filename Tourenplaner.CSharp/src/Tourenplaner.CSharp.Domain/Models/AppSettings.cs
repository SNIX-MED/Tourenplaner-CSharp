namespace Tourenplaner.CSharp.Domain.Models;

public sealed class AppSettings
{
    public string SqlDataDir { get; set; } = @"C:\Program Files\Microsoft SQL Server\MSSQL15.SQLEXPRESS\MSSQL\DATA";
    public string SqlServerInstance { get; set; } = @".\SQLEXPRESS";
    public string SqlDatabase { get; set; } = string.Empty;
    public string AppearanceMode { get; set; } = "System";
    public List<string> QuickAccessItems { get; set; } = new() { "action:export_route", "action:import_sql", string.Empty, string.Empty };
    public bool BackupsEnabled { get; set; }
    public string BackupDir { get; set; } = string.Empty;
    public string BackupModeDefault { get; set; } = "full";
    public int BackupRetentionDays { get; set; } = 30;
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupIntervalDays { get; set; } = 7;
    public string LastBackupIso { get; set; } = string.Empty;
}
