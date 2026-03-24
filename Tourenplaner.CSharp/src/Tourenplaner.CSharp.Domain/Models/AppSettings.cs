namespace Tourenplaner.CSharp.Domain.Models;

public sealed class AppSettings
{
    public const string DefaultAvisoEmailSubjectTemplate = "Lieferung von Auftrag X";
    public const string DefaultUpdateFeedUrl = "https://github.com/SNIX-MED/Tourenplaner-CSharp/releases";
    public const string DefaultStatusColorNotSpecified = "#A855F7";
    public const string DefaultStatusColorOrdered = "#0EA5E9";
    public const string DefaultStatusColorOnTheWay = "#F59E0B";
    public const string DefaultStatusColorInStock = "#16A34A";
    public const string DefaultStatusColorPlanned = "#64748B";
    public const string DefaultCalendarLoadWarningColor = "#F59E0B";
    public const string DefaultCalendarLoadCriticalColor = "#DC2626";

    public string AppearanceMode { get; set; } = "System";
    public string AvisoEmailSubjectTemplate { get; set; } = DefaultAvisoEmailSubjectTemplate;
    public string CompanyName { get; set; } = "Firma";
    public string CompanyStreet { get; set; } = string.Empty;
    public string CompanyPostalCode { get; set; } = string.Empty;
    public string CompanyCity { get; set; } = string.Empty;
    public string StatusColorNotSpecified { get; set; } = DefaultStatusColorNotSpecified;
    public string StatusColorOrdered { get; set; } = DefaultStatusColorOrdered;
    public string StatusColorOnTheWay { get; set; } = DefaultStatusColorOnTheWay;
    public string StatusColorInStock { get; set; } = DefaultStatusColorInStock;
    public string StatusColorPlanned { get; set; } = DefaultStatusColorPlanned;
    public string CalendarLoadWarningColor { get; set; } = DefaultCalendarLoadWarningColor;
    public string CalendarLoadCriticalColor { get; set; } = DefaultCalendarLoadCriticalColor;
    public int CalendarLoadWarningPeopleThreshold { get; set; } = 1;
    public int CalendarLoadCriticalPeopleThreshold { get; set; } = 2;
    public bool MapDetailsPanelExpanded { get; set; } = true;
    public List<string> QuickAccessItems { get; set; } = new() { "action:export_route", string.Empty, string.Empty, string.Empty };
    public bool BackupsEnabled { get; set; }
    public string BackupDir { get; set; } = string.Empty;
    public string BackupModeDefault { get; set; } = "full";
    public int BackupRetentionDays { get; set; } = 30;
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupIntervalDays { get; set; } = 7;
    public string LastBackupIso { get; set; } = string.Empty;
    public string UpdateFeedUrl { get; set; } = DefaultUpdateFeedUrl;
}
