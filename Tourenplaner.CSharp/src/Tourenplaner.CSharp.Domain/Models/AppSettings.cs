namespace Tourenplaner.CSharp.Domain.Models;

public sealed class AppSettings
{
    public const string DefaultAvisoEmailSubjectTemplate = "Lieferung von Auftrag X";

    public string AppearanceMode { get; set; } = "System";
    public string GoogleMapsApiKey { get; set; } = string.Empty;
    public string AvisoEmailSubjectTemplate { get; set; } = DefaultAvisoEmailSubjectTemplate;
    public string CompanyName { get; set; } = "Firma";
    public string CompanyStreet { get; set; } = string.Empty;
    public string CompanyPostalCode { get; set; } = string.Empty;
    public string CompanyCity { get; set; } = string.Empty;
    public List<string> QuickAccessItems { get; set; } = new() { "action:export_route", string.Empty, string.Empty, string.Empty };
    public bool BackupsEnabled { get; set; }
    public string BackupDir { get; set; } = string.Empty;
    public string BackupModeDefault { get; set; } = "full";
    public int BackupRetentionDays { get; set; } = 30;
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupIntervalDays { get; set; } = 7;
    public string LastBackupIso { get; set; } = string.Empty;
}
