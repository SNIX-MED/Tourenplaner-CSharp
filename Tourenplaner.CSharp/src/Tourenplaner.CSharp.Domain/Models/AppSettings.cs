namespace Tourenplaner.CSharp.Domain.Models;

public sealed class AppSettings
{
    private const string LegacyDefaultStatusColorNotSpecified = "#A3A3A3";

    public const string TomTomApiKeyEnvironmentVariableName = "TOURENPLANER_TOMTOM_API_KEY";
    public const string DefaultAvisoEmailSubjectTemplate = "Lieferung von Auftrag X";
    public const string DefaultStatusColorNotSpecified = "#FFFFFF";
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
    public const int DefaultTomTomVehicleOnlyMaxSpeedKmh = 120;
    public const int DefaultTomTomVehicleWithTrailerMaxSpeedKmh = 100;
    private const int LegacyDefaultTrafficBufferPercent = 20;
    public const int DefaultTrafficBufferPercentFrom0500To0730 = 10;
    public const int DefaultTrafficBufferPercentFrom0730To0900 = 20;
    public const int DefaultTrafficBufferPercentFrom0900To1530 = 0;
    public const int DefaultTrafficBufferPercentFrom1530To1830 = 20;
    public const int DefaultStayMinutesFreiBordsteinkante = 10;
    public const int DefaultStayMinutesMitVerteilung = 20;
    public const int DefaultStayMinutesMitVerteilungMontage = 30;
    public const string DefaultTomTomTrafficSeverityMode = "slightly_stricter";
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
    public string TomTomApiKey { get; set; } = ResolveDefaultTomTomApiKey();
    public int TomTomTrafficRefreshSeconds { get; set; } = DefaultTomTomTrafficRefreshSeconds;
    public int TomTomRouteRecalcDebounceMs { get; set; } = DefaultTomTomRouteRecalcDebounceMs;
    public int TomTomVehicleOnlyMaxSpeedKmh { get; set; } = DefaultTomTomVehicleOnlyMaxSpeedKmh;
    public int TomTomVehicleWithTrailerMaxSpeedKmh { get; set; } = DefaultTomTomVehicleWithTrailerMaxSpeedKmh;
    public int TrafficBufferPercentPerThirtyMinutes { get; set; } = DefaultTrafficBufferPercentFrom0500To0730;
    public int TrafficBufferPercentFrom0500To0730 { get; set; } = -1;
    public int TrafficBufferPercentFrom0730To0900 { get; set; } = -1;
    public int TrafficBufferPercentFrom0900To1530 { get; set; } = -1;
    public int TrafficBufferPercentFrom1530To1830 { get; set; } = -1;
    public int StayMinutesFreiBordsteinkante { get; set; } = DefaultStayMinutesFreiBordsteinkante;
    public int StayMinutesMitVerteilung { get; set; } = DefaultStayMinutesMitVerteilung;
    public int StayMinutesMitVerteilungMontage { get; set; } = DefaultStayMinutesMitVerteilungMontage;
    public string TomTomTrafficSeverityMode { get; set; } = DefaultTomTomTrafficSeverityMode;
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
            return NormalizeUserPreference(existing.Clone());
        }

        return BuildLegacyUserPreference();
    }

    public void SetUserPreference(string? userName, UserAppPreference preference)
    {
        var normalizedUserName = NormalizeUserName(userName);
        UserPreferencesByUser[normalizedUserName] = NormalizeTrafficBufferProfile((preference ?? BuildLegacyUserPreference()).Clone());
    }

    public static string NormalizeUserName(string? userName)
    {
        var normalized = (userName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
    }

    private UserAppPreference BuildLegacyUserPreference()
    {
        return NormalizeUserPreference(new UserAppPreference
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
            TomTomVehicleOnlyMaxSpeedKmh = TomTomVehicleOnlyMaxSpeedKmh,
            TomTomVehicleWithTrailerMaxSpeedKmh = TomTomVehicleWithTrailerMaxSpeedKmh,
            TrafficBufferPercentPerThirtyMinutes = TrafficBufferPercentPerThirtyMinutes,
            TrafficBufferPercentFrom0500To0730 = ResolveTrafficBufferPercent(
                TrafficBufferPercentFrom0500To0730,
                TrafficBufferPercentPerThirtyMinutes,
                DefaultTrafficBufferPercentFrom0500To0730),
            TrafficBufferPercentFrom0730To0900 = ResolveTrafficBufferPercent(
                TrafficBufferPercentFrom0730To0900,
                TrafficBufferPercentPerThirtyMinutes,
                DefaultTrafficBufferPercentFrom0730To0900),
            TrafficBufferPercentFrom0900To1530 = ResolveTrafficBufferPercent(
                TrafficBufferPercentFrom0900To1530,
                TrafficBufferPercentPerThirtyMinutes,
                DefaultTrafficBufferPercentFrom0900To1530),
            TrafficBufferPercentFrom1530To1830 = ResolveTrafficBufferPercent(
                TrafficBufferPercentFrom1530To1830,
                TrafficBufferPercentPerThirtyMinutes,
                DefaultTrafficBufferPercentFrom1530To1830),
            TomTomTrafficSeverityMode = NormalizeTomTomTrafficSeverityMode(TomTomTrafficSeverityMode),
            TomTomEnableTileCache = TomTomEnableTileCache
        });
    }

    public static string NormalizeTomTomTrafficSeverityMode(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "standard" => "standard",
            "strict" => "strict",
            "slightly_stricter" => "slightly_stricter",
            _ => DefaultTomTomTrafficSeverityMode
        };
    }

    public static string ResolveDefaultTomTomApiKey()
    {
        var configuredValue = Environment.GetEnvironmentVariable(TomTomApiKeyEnvironmentVariableName);
        return string.IsNullOrWhiteSpace(configuredValue)
            ? "IkfQGXF6uvRllgzgL79SWuSzRQqJHYzH"
            : configuredValue.Trim();
    }

    public int ResolveMapOrderStayMinutes(string? deliveryType)
    {
        return DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(deliveryType) switch
        {
            DeliveryMethodExtensions.MitVerteilung => NormalizeStayMinutes(StayMinutesMitVerteilung, DefaultStayMinutesMitVerteilung),
            DeliveryMethodExtensions.MitVerteilungMontage => NormalizeStayMinutes(StayMinutesMitVerteilungMontage, DefaultStayMinutesMitVerteilungMontage),
            _ => NormalizeStayMinutes(StayMinutesFreiBordsteinkante, DefaultStayMinutesFreiBordsteinkante)
        };
    }

    public static int NormalizeStayMinutes(int value, int fallbackValue)
    {
        return value < 0
            ? Math.Max(0, fallbackValue)
            : Math.Clamp(value, 0, 1440);
    }

    private static int ResolveTrafficBufferPercent(int explicitValue, int legacyValue, int fallbackValue)
    {
        if (explicitValue is >= 0 and <= 100)
        {
            return explicitValue;
        }

        if (legacyValue is >= 0 and <= 100)
        {
            return legacyValue;
        }

        return fallbackValue;
    }

    private static UserAppPreference NormalizeUserPreference(UserAppPreference preference)
    {
        preference.StatusColorNotSpecified = NormalizeNotSpecifiedStatusColor(preference.StatusColorNotSpecified);
        return NormalizeTrafficBufferProfile(preference);
    }

    private static string NormalizeNotSpecifiedStatusColor(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, LegacyDefaultStatusColorNotSpecified, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultStatusColorNotSpecified;
        }

        return normalized;
    }

    private static UserAppPreference NormalizeTrafficBufferProfile(UserAppPreference preference)
    {
        if (!IsLegacyDefaultTrafficBufferProfile(preference))
        {
            return preference;
        }

        preference.TrafficBufferPercentPerThirtyMinutes = DefaultTrafficBufferPercentFrom0500To0730;
        preference.TrafficBufferPercentFrom0500To0730 = DefaultTrafficBufferPercentFrom0500To0730;
        preference.TrafficBufferPercentFrom0730To0900 = DefaultTrafficBufferPercentFrom0730To0900;
        preference.TrafficBufferPercentFrom0900To1530 = DefaultTrafficBufferPercentFrom0900To1530;
        preference.TrafficBufferPercentFrom1530To1830 = DefaultTrafficBufferPercentFrom1530To1830;
        return preference;
    }

    private static bool IsLegacyDefaultTrafficBufferProfile(UserAppPreference preference)
    {
        static bool IsLegacyDefaultOrUnspecified(int value)
            => value == LegacyDefaultTrafficBufferPercent || value == -1;

        return preference.TrafficBufferPercentPerThirtyMinutes == LegacyDefaultTrafficBufferPercent &&
               IsLegacyDefaultOrUnspecified(preference.TrafficBufferPercentFrom0500To0730) &&
               IsLegacyDefaultOrUnspecified(preference.TrafficBufferPercentFrom0730To0900) &&
               IsLegacyDefaultOrUnspecified(preference.TrafficBufferPercentFrom0900To1530) &&
               IsLegacyDefaultOrUnspecified(preference.TrafficBufferPercentFrom1530To1830);
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
    public int TomTomVehicleOnlyMaxSpeedKmh { get; set; } = AppSettings.DefaultTomTomVehicleOnlyMaxSpeedKmh;
    public int TomTomVehicleWithTrailerMaxSpeedKmh { get; set; } = AppSettings.DefaultTomTomVehicleWithTrailerMaxSpeedKmh;
    public int TrafficBufferPercentPerThirtyMinutes { get; set; } = AppSettings.DefaultTrafficBufferPercentFrom0500To0730;
    public int TrafficBufferPercentFrom0500To0730 { get; set; } = -1;
    public int TrafficBufferPercentFrom0730To0900 { get; set; } = -1;
    public int TrafficBufferPercentFrom0900To1530 { get; set; } = -1;
    public int TrafficBufferPercentFrom1530To1830 { get; set; } = -1;
    public string TomTomTrafficSeverityMode { get; set; } = AppSettings.DefaultTomTomTrafficSeverityMode;
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
            TomTomVehicleOnlyMaxSpeedKmh = TomTomVehicleOnlyMaxSpeedKmh,
            TomTomVehicleWithTrailerMaxSpeedKmh = TomTomVehicleWithTrailerMaxSpeedKmh,
            TrafficBufferPercentPerThirtyMinutes = TrafficBufferPercentPerThirtyMinutes,
            TrafficBufferPercentFrom0500To0730 = TrafficBufferPercentFrom0500To0730,
            TrafficBufferPercentFrom0730To0900 = TrafficBufferPercentFrom0730To0900,
            TrafficBufferPercentFrom0900To1530 = TrafficBufferPercentFrom0900To1530,
            TrafficBufferPercentFrom1530To1830 = TrafficBufferPercentFrom1530To1830,
            TomTomTrafficSeverityMode = TomTomTrafficSeverityMode,
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
