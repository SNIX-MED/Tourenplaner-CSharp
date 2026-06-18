namespace Tourenplaner.CSharp.Domain.Models;

public sealed class AppSettings
{
    public const string DefaultAvisoEmailSubjectTemplate = "Lieferung von Auftrag X";
    public const string DefaultStatusColorNotSpecified = "#A3A3A3";
    public const string DefaultStatusColorOrdered = "#0EA5E9";
    public const string DefaultStatusColorOnTheWay = "#F59E0B";
    public const string DefaultStatusColorPendingPreparation = "#F97316";
    public const string DefaultStatusColorInStock = "#16A34A";
    public const string DefaultStatusColorPlanned = "#64748B";
    public const string DefaultCalendarLoadWarningColor = "#F59E0B";
    public const string DefaultCalendarLoadCriticalColor = "#DC2626";
    public const string DefaultGpsToolUrl = "https://map.ktrac.ch/";
    public const string DefaultSpediteurToolUrl = "https://portal.haslertransport.ch/";
    public const string DefaultTourStartTime = "07:30";
    public const int DefaultMapRouteCapacityWarningThresholdPercent = 5;
    public const double DefaultPinInfoCardScale = 1.0d;
    public const double DefaultPinInfoCardZoomBehaviorStrength = 1.0d;
    public const int DefaultTomTomTrafficRefreshSeconds = 60;
    public const int DefaultTomTomRouteRecalcDebounceMs = 900;
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
    public string StatusColorPendingPreparation { get; set; } = DefaultStatusColorPendingPreparation;
    public string StatusColorInStock { get; set; } = DefaultStatusColorInStock;
    public string StatusColorPlanned { get; set; } = DefaultStatusColorPlanned;
    public string CalendarLoadWarningColor { get; set; } = DefaultCalendarLoadWarningColor;
    public string CalendarLoadCriticalColor { get; set; } = DefaultCalendarLoadCriticalColor;
    public bool MapUseDistinctPlannedTourColors { get; set; } = true;
    public int CalendarLoadWarningPeopleThreshold { get; set; } = 1;
    public int CalendarLoadCriticalPeopleThreshold { get; set; } = 2;
    public bool MapDetailsPanelExpanded { get; set; } = true;
    public bool MapAutoOpenDetailsOnPinSelection { get; set; } = true;
    public bool MapSearchDimNonMatchingPins { get; set; } = true;
    public bool MapPinInfoCardShowName { get; set; } = true;
    public bool MapPinInfoCardShowOrderNumber { get; set; } = true;
    public bool MapPinInfoCardShowStreet { get; set; } = true;
    public bool MapPinInfoCardShowPostalCodeCity { get; set; } = true;
    public bool MapPinInfoCardShowNotes { get; set; } = true;
    public bool MapPinInfoCardShowProducts { get; set; } = true;
    public bool MapPinInfoCardShowTotalWeight { get; set; } = true;
    public double PinInfoCardScale { get; set; } = DefaultPinInfoCardScale;
    public double PinInfoCardZoomBehaviorStrength { get; set; } = DefaultPinInfoCardZoomBehaviorStrength;
    public int MapRouteCapacityWarningThresholdPercent { get; set; } = DefaultMapRouteCapacityWarningThresholdPercent;
    public List<string> QuickAccessItems { get; set; } = new() { "action:export_route", string.Empty, string.Empty, string.Empty };
    public bool BackupsEnabled { get; set; }
    public string BackupDir { get; set; } = string.Empty;
    public string BackupModeDefault { get; set; } = "full";
    public int BackupRetentionDays { get; set; } = 30;
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupIntervalDays { get; set; } = 7;
    public string LastBackupIso { get; set; } = string.Empty;
    public bool ShowGpsTool { get; set; } = true;
    public string GpsToolUrl { get; set; } = DefaultGpsToolUrl;
    public bool ShowSpediteurTool { get; set; } = true;
    public string SpediteurToolUrl { get; set; } = DefaultSpediteurToolUrl;
    public string TourDefaultStartTime { get; set; } = DefaultTourStartTime;
    public string TomTomApiKey { get; set; } = "IkfQGXF6uvRllgzgL79SWuSzRQqJHYzH";
    public int TomTomTrafficRefreshSeconds { get; set; } = DefaultTomTomTrafficRefreshSeconds;
    public int TomTomRouteRecalcDebounceMs { get; set; } = DefaultTomTomRouteRecalcDebounceMs;
    public bool TomTomEnableTileCache { get; set; } = true;
    public string CurrentUserName { get; set; } = string.Empty;
    public Dictionary<string, MapOverlayUserPreference> MapOverlayPreferencesByUser { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, UserAppPreference> UserPreferencesByUser { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AppStorageMode StorageMode { get; set; } = AppStorageMode.JsonFiles;
    public PostgreSqlStorageSettings PostgreSqlStorage { get; set; } = new();
    
    // SQL Server Import Settings
    public SqlConnectionSettings SqlImportSettings { get; set; } = new();
    public DateTime? LastSqlImportDate { get; set; }
    public bool SqlImportEnabled { get; set; } = false;
    public string XmlImportFilePath { get; set; } = string.Empty;
    public DateTime? LastXmlImportDate { get; set; }
    public XmlImportMappingSettings XmlImportMapping { get; set; } = XmlImportMappingSettings.CreateDefault();

    public UserAppPreference ResolveUserPreference(string? userName)
    {
        var normalizedUserName = NormalizeUserName(userName);
        if (UserPreferencesByUser.TryGetValue(normalizedUserName, out var existing) && existing is not null)
        {
            return existing.Clone();
        }

        return BuildLegacyUserPreference();
    }

    public void SetUserPreference(string? userName, UserAppPreference preference)
    {
        var normalizedUserName = NormalizeUserName(userName);
        UserPreferencesByUser[normalizedUserName] = (preference ?? BuildLegacyUserPreference()).Clone();
    }

    public static string NormalizeUserName(string? userName)
    {
        var normalized = (userName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
    }

    private UserAppPreference BuildLegacyUserPreference()
    {
        return new UserAppPreference
        {
            AppearanceMode = string.IsNullOrWhiteSpace(AppearanceMode) ? "Light" : AppearanceMode,
            AvisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(AvisoEmailSubjectTemplate) ? DefaultAvisoEmailSubjectTemplate : AvisoEmailSubjectTemplate,
            StatusColorNotSpecified = StatusColorNotSpecified,
            StatusColorOrdered = StatusColorOrdered,
            StatusColorOnTheWay = StatusColorOnTheWay,
            StatusColorPendingPreparation = StatusColorPendingPreparation,
            StatusColorInStock = StatusColorInStock,
            StatusColorPlanned = StatusColorPlanned,
            CalendarLoadWarningColor = CalendarLoadWarningColor,
            CalendarLoadCriticalColor = CalendarLoadCriticalColor,
            MapUseDistinctPlannedTourColors = MapUseDistinctPlannedTourColors,
            CalendarLoadWarningPeopleThreshold = CalendarLoadWarningPeopleThreshold,
            CalendarLoadCriticalPeopleThreshold = CalendarLoadCriticalPeopleThreshold,
            MapDetailsPanelExpanded = MapDetailsPanelExpanded,
            MapAutoOpenDetailsOnPinSelection = MapAutoOpenDetailsOnPinSelection,
            MapSearchDimNonMatchingPins = MapSearchDimNonMatchingPins,
            MapPinInfoCardShowName = MapPinInfoCardShowName,
            MapPinInfoCardShowOrderNumber = MapPinInfoCardShowOrderNumber,
            MapPinInfoCardShowStreet = MapPinInfoCardShowStreet,
            MapPinInfoCardShowPostalCodeCity = MapPinInfoCardShowPostalCodeCity,
            MapPinInfoCardShowNotes = MapPinInfoCardShowNotes,
            MapPinInfoCardShowProducts = MapPinInfoCardShowProducts,
            MapPinInfoCardShowTotalWeight = MapPinInfoCardShowTotalWeight,
            PinInfoCardScale = PinInfoCardScale,
            PinInfoCardZoomBehaviorStrength = PinInfoCardZoomBehaviorStrength,
            MapRouteCapacityWarningThresholdPercent = MapRouteCapacityWarningThresholdPercent,
            QuickAccessItems = new List<string>(QuickAccessItems ?? []),
            ShowGpsTool = ShowGpsTool,
            GpsToolUrl = string.IsNullOrWhiteSpace(GpsToolUrl) ? DefaultGpsToolUrl : GpsToolUrl,
            ShowSpediteurTool = ShowSpediteurTool,
            SpediteurToolUrl = string.IsNullOrWhiteSpace(SpediteurToolUrl) ? DefaultSpediteurToolUrl : SpediteurToolUrl,
            TourDefaultStartTime = string.IsNullOrWhiteSpace(TourDefaultStartTime) ? DefaultTourStartTime : TourDefaultStartTime,
            TomTomTrafficRefreshSeconds = TomTomTrafficRefreshSeconds,
            TomTomRouteRecalcDebounceMs = TomTomRouteRecalcDebounceMs,
            TomTomEnableTileCache = TomTomEnableTileCache
        };
    }
}

public sealed class UserAppPreference
{
    public string AppearanceMode { get; set; } = "Light";
    public string AvisoEmailSubjectTemplate { get; set; } = AppSettings.DefaultAvisoEmailSubjectTemplate;
    public string StatusColorNotSpecified { get; set; } = AppSettings.DefaultStatusColorNotSpecified;
    public string StatusColorOrdered { get; set; } = AppSettings.DefaultStatusColorOrdered;
    public string StatusColorOnTheWay { get; set; } = AppSettings.DefaultStatusColorOnTheWay;
    public string StatusColorPendingPreparation { get; set; } = AppSettings.DefaultStatusColorPendingPreparation;
    public string StatusColorInStock { get; set; } = AppSettings.DefaultStatusColorInStock;
    public string StatusColorPlanned { get; set; } = AppSettings.DefaultStatusColorPlanned;
    public string CalendarLoadWarningColor { get; set; } = AppSettings.DefaultCalendarLoadWarningColor;
    public string CalendarLoadCriticalColor { get; set; } = AppSettings.DefaultCalendarLoadCriticalColor;
    public bool MapUseDistinctPlannedTourColors { get; set; } = true;
    public int CalendarLoadWarningPeopleThreshold { get; set; } = 1;
    public int CalendarLoadCriticalPeopleThreshold { get; set; } = 2;
    public bool MapDetailsPanelExpanded { get; set; } = true;
    public bool MapAutoOpenDetailsOnPinSelection { get; set; } = true;
    public bool MapSearchDimNonMatchingPins { get; set; } = true;
    public bool MapPinInfoCardShowName { get; set; } = true;
    public bool MapPinInfoCardShowOrderNumber { get; set; } = true;
    public bool MapPinInfoCardShowStreet { get; set; } = true;
    public bool MapPinInfoCardShowPostalCodeCity { get; set; } = true;
    public bool MapPinInfoCardShowNotes { get; set; } = true;
    public bool MapPinInfoCardShowProducts { get; set; } = true;
    public bool MapPinInfoCardShowTotalWeight { get; set; } = true;
    public double PinInfoCardScale { get; set; } = AppSettings.DefaultPinInfoCardScale;
    public double PinInfoCardZoomBehaviorStrength { get; set; } = AppSettings.DefaultPinInfoCardZoomBehaviorStrength;
    public int MapRouteCapacityWarningThresholdPercent { get; set; } = AppSettings.DefaultMapRouteCapacityWarningThresholdPercent;
    public List<string> QuickAccessItems { get; set; } = new() { "action:export_route", string.Empty, string.Empty, string.Empty };
    public bool ShowGpsTool { get; set; } = true;
    public string GpsToolUrl { get; set; } = AppSettings.DefaultGpsToolUrl;
    public bool ShowSpediteurTool { get; set; } = true;
    public string SpediteurToolUrl { get; set; } = AppSettings.DefaultSpediteurToolUrl;
    public string TourDefaultStartTime { get; set; } = AppSettings.DefaultTourStartTime;
    public int TomTomTrafficRefreshSeconds { get; set; } = AppSettings.DefaultTomTomTrafficRefreshSeconds;
    public int TomTomRouteRecalcDebounceMs { get; set; } = AppSettings.DefaultTomTomRouteRecalcDebounceMs;
    public bool TomTomEnableTileCache { get; set; } = true;

    public UserAppPreference Clone()
    {
        return new UserAppPreference
        {
            AppearanceMode = AppearanceMode,
            AvisoEmailSubjectTemplate = AvisoEmailSubjectTemplate,
            StatusColorNotSpecified = StatusColorNotSpecified,
            StatusColorOrdered = StatusColorOrdered,
            StatusColorOnTheWay = StatusColorOnTheWay,
            StatusColorPendingPreparation = StatusColorPendingPreparation,
            StatusColorInStock = StatusColorInStock,
            StatusColorPlanned = StatusColorPlanned,
            CalendarLoadWarningColor = CalendarLoadWarningColor,
            CalendarLoadCriticalColor = CalendarLoadCriticalColor,
            MapUseDistinctPlannedTourColors = MapUseDistinctPlannedTourColors,
            CalendarLoadWarningPeopleThreshold = CalendarLoadWarningPeopleThreshold,
            CalendarLoadCriticalPeopleThreshold = CalendarLoadCriticalPeopleThreshold,
            MapDetailsPanelExpanded = MapDetailsPanelExpanded,
            MapAutoOpenDetailsOnPinSelection = MapAutoOpenDetailsOnPinSelection,
            MapSearchDimNonMatchingPins = MapSearchDimNonMatchingPins,
            MapPinInfoCardShowName = MapPinInfoCardShowName,
            MapPinInfoCardShowOrderNumber = MapPinInfoCardShowOrderNumber,
            MapPinInfoCardShowStreet = MapPinInfoCardShowStreet,
            MapPinInfoCardShowPostalCodeCity = MapPinInfoCardShowPostalCodeCity,
            MapPinInfoCardShowNotes = MapPinInfoCardShowNotes,
            MapPinInfoCardShowProducts = MapPinInfoCardShowProducts,
            MapPinInfoCardShowTotalWeight = MapPinInfoCardShowTotalWeight,
            PinInfoCardScale = PinInfoCardScale,
            PinInfoCardZoomBehaviorStrength = PinInfoCardZoomBehaviorStrength,
            MapRouteCapacityWarningThresholdPercent = MapRouteCapacityWarningThresholdPercent,
            QuickAccessItems = new List<string>(QuickAccessItems ?? []),
            ShowGpsTool = ShowGpsTool,
            GpsToolUrl = GpsToolUrl,
            ShowSpediteurTool = ShowSpediteurTool,
            SpediteurToolUrl = SpediteurToolUrl,
            TourDefaultStartTime = TourDefaultStartTime,
            TomTomTrafficRefreshSeconds = TomTomTrafficRefreshSeconds,
            TomTomRouteRecalcDebounceMs = TomTomRouteRecalcDebounceMs,
            TomTomEnableTileCache = TomTomEnableTileCache
        };
    }
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
