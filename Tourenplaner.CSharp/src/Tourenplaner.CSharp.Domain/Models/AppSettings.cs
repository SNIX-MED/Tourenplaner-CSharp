namespace Tourenplaner.CSharp.Domain.Models;

public sealed class AppSettings
{
    public const string DefaultAvisoEmailSubjectTemplate = "Lieferung von Auftrag X";
    public const string DefaultUpdateFeedUrl = "https://github.com/SNIX-MED/Tourenplaner-CSharp/releases";
    public const string DefaultStatusColorNotSpecified = "#A3A3A3";
    public const string DefaultStatusColorOrdered = "#0EA5E9";
    public const string DefaultStatusColorOnTheWay = "#F59E0B";
    public const string DefaultStatusColorInStock = "#16A34A";
    public const string DefaultStatusColorPlanned = "#64748B";
    public const string DefaultCalendarLoadWarningColor = "#F59E0B";
    public const string DefaultCalendarLoadCriticalColor = "#DC2626";
    public const string DefaultGpsToolUrl = "https://map.ktrac.ch/";
    public const string DefaultSpediteurToolUrl = "https://portal.haslertransport.ch/";
    public const string DefaultTourStartTime = "07:30";
    public const int DefaultMapRouteCapacityWarningThresholdPercent = 5;
    public const double DefaultPinInfoCardScale = 1.0d;
    public const string DefaultTomTomMapStyle = "main";
    public const int DefaultTomTomTrafficRefreshSeconds = 60;
    public const int DefaultTomTomRouteRecalcDebounceMs = 900;
    public const string DefaultTomTomRoutingMode = "car";
    public const double DefaultTomTomVehicleHeightMeters = 0d;
    public const string DefaultMapOverlayStyle = "standard";

    public string AppearanceMode { get; set; } = "Light";
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
    public bool MapSearchDimNonMatchingPins { get; set; } = true;
    public bool MapPinInfoCardShowName { get; set; } = true;
    public bool MapPinInfoCardShowOrderNumber { get; set; } = true;
    public bool MapPinInfoCardShowStreet { get; set; } = true;
    public bool MapPinInfoCardShowPostalCodeCity { get; set; } = true;
    public bool MapPinInfoCardShowNotes { get; set; } = true;
    public bool MapPinInfoCardShowProducts { get; set; } = true;
    public bool MapPinInfoCardShowTotalWeight { get; set; } = true;
    public double PinInfoCardScale { get; set; } = DefaultPinInfoCardScale;
    public int MapRouteCapacityWarningThresholdPercent { get; set; } = DefaultMapRouteCapacityWarningThresholdPercent;
    public List<string> QuickAccessItems { get; set; } = new() { "action:export_route", string.Empty, string.Empty, string.Empty };
    public bool BackupsEnabled { get; set; }
    public string BackupDir { get; set; } = string.Empty;
    public string BackupModeDefault { get; set; } = "full";
    public int BackupRetentionDays { get; set; } = 30;
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupIntervalDays { get; set; } = 7;
    public string LastBackupIso { get; set; } = string.Empty;
    public string UpdateFeedUrl { get; set; } = DefaultUpdateFeedUrl;
    public bool ShowGpsTool { get; set; } = true;
    public string GpsToolUrl { get; set; } = DefaultGpsToolUrl;
    public bool ShowSpediteurTool { get; set; } = true;
    public string SpediteurToolUrl { get; set; } = DefaultSpediteurToolUrl;
    public string TourDefaultStartTime { get; set; } = DefaultTourStartTime;
    public string TomTomApiKey { get; set; } = "IkfQGXF6uvRllgzgL79SWuSzRQqJHYzH";
    public string TomTomMapStyle { get; set; } = DefaultTomTomMapStyle;
    public bool TomTomShowTrafficFlow { get; set; } = true;
    public int TomTomTrafficRefreshSeconds { get; set; } = DefaultTomTomTrafficRefreshSeconds;
    public int TomTomRouteRecalcDebounceMs { get; set; } = DefaultTomTomRouteRecalcDebounceMs;
    public string TomTomRoutingMode { get; set; } = DefaultTomTomRoutingMode;
    public double TomTomVehicleHeightMeters { get; set; } = DefaultTomTomVehicleHeightMeters;
    public bool TomTomEnableTileCache { get; set; } = true;
    public string CurrentUserName { get; set; } = string.Empty;
    public Dictionary<string, MapOverlayUserPreference> MapOverlayPreferencesByUser { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    // SQL Server Import Settings
    public SqlConnectionSettings SqlImportSettings { get; set; } = new();
    public DateTime? LastSqlImportDate { get; set; }
    public bool SqlImportEnabled { get; set; } = false;
}

public sealed class MapOverlayUserPreference
{
    public string Style { get; set; } = AppSettings.DefaultMapOverlayStyle;
    public bool ShowTrafficFlow { get; set; } = true;
    public bool ShowTrafficIncidents { get; set; }
    public bool ShowRoadLabels { get; set; } = true;
    public bool ShowPoi { get; set; } = true;
    public bool UseVehicleDimensions { get; set; }
    public bool UseVehicleWeightRestrictions { get; set; }
    public bool UseDepartAtTraffic { get; set; } = true;
}
