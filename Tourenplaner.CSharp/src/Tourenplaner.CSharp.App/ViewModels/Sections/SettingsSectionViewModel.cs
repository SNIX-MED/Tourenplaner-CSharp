using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Reflection;
using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class SettingsSectionViewModel : SectionViewModelBase
{
    private const int MaxXmlImportPreviewItems = 100;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly IAppSettingsStore _repository;
    private readonly SettingsValidator _validator;
    private readonly BackupManager _backupManager;
    private readonly IOrderRepository? _orderRepository;
    private readonly ISettingsRepository? _settingsRepository;
    private readonly AppDataSyncService? _dataSyncService;
    private readonly string _dataRoot;
    private bool _isBackgroundGeocodingRunning;
    private SettingsCategoryNavigationItem? _selectedSettingsCategory;

    private string _statusText = string.Empty;
    
    // XML Import Settings
    private string _xmlImportFilePath = string.Empty;
    private bool _isImportingOrders = false;
    private string _importStatusMessage = "";
    private bool _isPreviewingXmlImport;
    private string _xmlImportPreviewSummary = string.Empty;
    private string _xmlImportPinIssueSummary = string.Empty;
    private int _xmlImportPreviewHiddenItemCount;
    private bool _hasPendingXmlImportPreview;
    private readonly List<SqlOrderImportData> _previewedXmlOrders = new();
    private DateTime _xmlImportPreviewLastWriteUtc;
    private long _xmlImportPreviewFileLength;
    
    private string _avisoEmailSubjectTemplate = AppSettings.DefaultAvisoEmailSubjectTemplate;
    private string _companyName = "Firma";
    private string _companyStreet = string.Empty;
    private string _companyPostalCode = string.Empty;
    private string _companyCity = string.Empty;
    private string _statusColorNotSpecified = AppSettings.DefaultStatusColorNotSpecified;
    private string _statusColorOrdered = AppSettings.DefaultStatusColorOrdered;
    private string _statusColorOnTheWay = AppSettings.DefaultStatusColorOnTheWay;
    private string _statusColorPendingPreparation = AppSettings.DefaultStatusColorPendingPreparation;
    private string _statusColorInStock = AppSettings.DefaultStatusColorInStock;
    private string _statusColorPlanned = AppSettings.DefaultStatusColorPlanned;
    private string _calendarLoadWarningColor = AppSettings.DefaultCalendarLoadWarningColor;
    private string _calendarLoadCriticalColor = AppSettings.DefaultCalendarLoadCriticalColor;
    private bool _mapUseDistinctPlannedTourColors = true;
    private int _calendarLoadWarningPeopleThreshold = 1;
    private int _calendarLoadCriticalPeopleThreshold = 2;
    private bool _mapAutoOpenDetailsOnPinSelection = true;
    private bool _mapSearchDimNonMatchingPins;
    private bool _mapPinInfoCardShowName = true;
    private bool _mapPinInfoCardShowOrderNumber = true;
    private bool _mapPinInfoCardShowStreet = true;
    private bool _mapPinInfoCardShowPostalCodeCity = true;
    private bool _mapPinInfoCardShowNotes = true;
    private bool _mapPinInfoCardShowProducts = true;
    private bool _mapPinInfoCardShowTotalWeight = true;
    private double _pinInfoCardZoomBehaviorStrength = AppSettings.DefaultPinInfoCardZoomBehaviorStrength;
    private int _mapRouteCapacityWarningThresholdPercent = AppSettings.DefaultMapRouteCapacityWarningThresholdPercent;
    private string _tomTomApiKey = "IkfQGXF6uvRllgzgL79SWuSzRQqJHYzH";
    private int _tomTomTrafficRefreshSeconds = AppSettings.DefaultTomTomTrafficRefreshSeconds;
    private int _tomTomRouteRecalcDebounceMs = AppSettings.DefaultTomTomRouteRecalcDebounceMs;
    private int _tomTomVehicleOnlyMaxSpeedKmh = AppSettings.DefaultTomTomVehicleOnlyMaxSpeedKmh;
    private int _tomTomVehicleWithTrailerMaxSpeedKmh = AppSettings.DefaultTomTomVehicleWithTrailerMaxSpeedKmh;
    private int _trafficBufferPercentFrom0500To0730 = AppSettings.DefaultTrafficBufferPercentFrom0500To0730;
    private int _trafficBufferPercentFrom0730To0900 = AppSettings.DefaultTrafficBufferPercentFrom0730To0900;
    private int _trafficBufferPercentFrom0900To1530 = AppSettings.DefaultTrafficBufferPercentFrom0900To1530;
    private int _trafficBufferPercentFrom1530To1830 = AppSettings.DefaultTrafficBufferPercentFrom1530To1830;
    private string _tomTomTrafficSeverityMode = AppSettings.DefaultTomTomTrafficSeverityMode;
    private bool _tomTomEnableTileCache = true;
    private bool _backupsEnabled;
    private string _backupDir = string.Empty;
    private string _backupModeDefault = "full";
    private int _backupRetentionDays = 30;
    private bool _autoBackupEnabled;
    private int _autoBackupIntervalDays = 7;
    private string _lastBackupIso = string.Empty;
    private string _applicationVersion = string.Empty;
    private string _runtimeVersion = string.Empty;
    private string _updateStatusText = "Noch nicht geprüft.";
    private string _availableUpdateVersion = "Noch nicht geprüft";
    private string _lastUpdateCheckText = "Noch nicht geprüft";
    private string _updatePublishedAtText = "-";
    private string _updateConfigPath = "Nicht gefunden";
    private string _updateActionUrl = string.Empty;
    private bool _isUpdateAvailable;
    private string _startupStatusText = "Noch keine Startdiagnose vorhanden.";
    private string _startupDetailText = "-";
    private string _startupRecordedAtText = "Noch keine Startdiagnose vorhanden.";
    private string _startupLastStepText = "-";
    private string _diagnosticsFilePath = string.Empty;
    private string _crashLogPath = string.Empty;
    private string _syncStatusText = "Lokaler Dateibetrieb ohne Live-Sync.";
    private string _syncModeText = "Lokaler Dateibetrieb";
    private string _lastSyncStatusChangeText = "Noch keine Statusaenderung";
    private string _lastRemoteSyncText = "Noch keine externe Aenderung";
    private string _lastLocalSyncText = "Noch keine lokale Aenderung";
    private string _lastSyncActivityText = "Noch keine Synchronisationsaktivitaet";
    private string _syncErrorText = "-";
    private string _latestBackupFile = "n/a";
    private string _latestBackupModifiedText = "n/a";
    private int _availableBackupsCount;
    private bool _showGpsTool = true;
    private string _gpsToolUrl = AppSettings.DefaultGpsToolUrl;
    private bool _showSpediteurTool = true;
    private string _spediteurToolUrl = AppSettings.DefaultSpediteurToolUrl;
    private string _tourDefaultStartTime = AppSettings.DefaultTourStartTime;
    private string _validationSummary = string.Empty;
    private bool _suppressAutoSave;
    private CancellationTokenSource? _autoSaveCts;
    private bool _autoSaveInProgress;
    private CancellationTokenSource? _pinInfoCardZoomBehaviorSaveCts;
    private string _currentUserName = string.Empty;
    private Dictionary<string, MapOverlayUserPreference> _mapOverlayPreferencesByUser = new(StringComparer.OrdinalIgnoreCase);
    private AppStorageMode _storageMode = AppStorageMode.JsonFiles;
    private string _postgreSqlHost = "localhost";
    private int _postgreSqlPort = 5432;
    private string _postgreSqlDatabase = "tourenplaner";
    private string _postgreSqlSchema = "app";
    private string _postgreSqlUsername = "postgres";
    private string _postgreSqlPassword = string.Empty;
    private bool _postgreSqlUseSsl;
    private int _postgreSqlTimeoutSeconds = 10;
    private AppStorageMode _activeStorageMode = AppStorageMode.JsonFiles;

    public SettingsSectionViewModel(
        IAppSettingsStore settingsStore,
        string dataRoot,
        IOrderRepository? orderRepository = null,
        ISettingsRepository? settingsRepository = null,
        AppDataSyncService? dataSyncService = null,
        AppStorageMode activeStorageMode = AppStorageMode.JsonFiles)
        : base("Settings", "Appearance, backup policy and restore operations.")
    {
        _repository = settingsStore;
        _validator = new SettingsValidator();
        _backupManager = new BackupManager();
        _orderRepository = orderRepository;
        _settingsRepository = settingsRepository;
        _dataSyncService = dataSyncService;
        _dataRoot = dataRoot;
        _activeStorageMode = activeStorageMode;

        BackupModes =
        [
            "full",
            "incremental"
        ];

        StorageModeOptions =
        [
            new StorageModeOption(AppStorageMode.JsonFiles, "Lokale JSON-Dateien"),
            new StorageModeOption(AppStorageMode.PostgreSql, "PostgreSQL Mehrbenutzer")
        ];

        TomTomTrafficSeverityOptions =
        [
            new TomTomTrafficSeverityOption("standard", "TomTom Standard"),
            new TomTomTrafficSeverityOption("slightly_stricter", "Etwas strenger"),
            new TomTomTrafficSeverityOption("strict", "Streng")
        ];

        SettingsCategories =
        [
            new SettingsCategoryNavigationItem("general", "Allgemein", "E-Mail, Startzeit, Standardverhalten und Firmendaten.", "\uE713"),
            new SettingsCategoryNavigationItem("storage", "Datenspeicher", "Lokale JSON-Dateien oder PostgreSQL-Mehrbenutzerbetrieb.", "\uE8D4"),
            new SettingsCategoryNavigationItem("map", "Karte & Kalender", "Farben, Kalenderwarnungen und Karteninfos.", "\uE787"),
            new SettingsCategoryNavigationItem("tomtom", "TomTom Karte & Traffic", "Routing, Overlay und Verkehrslogik.", "\uE81E"),
            new SettingsCategoryNavigationItem("tools", "Tools", "GPS- und Spediteur-Links und Sichtbarkeit.", "\uE90F"),
            new SettingsCategoryNavigationItem("backup", "Backup & Restore", "Sicherungen, Aufbewahrung und Wiederherstellung.", "\uE72C"),
            new SettingsCategoryNavigationItem("xml-import", "XML Import", "Import prüfen, Vorschau ansehen und sicher übernehmen.", "\uE9F9"),
            new SettingsCategoryNavigationItem("updates", "Updates & Validierung", "Versionen, Prüfungen und Konfigurationsstatus.", "\uE895")
        ];
        _selectedSettingsCategory = SettingsCategories.FirstOrDefault();

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync);
        ValidateCommand = new DelegateCommand(ValidateCurrentSettings);
        ResetStatusColorsCommand = new DelegateCommand(ResetStatusColors);
        PickStatusColorNotSpecifiedCommand = CreateColorPickerCommand(
            "Nicht festgelegt",
            "Statusfarbe für Aufträge ohne Lieferstatus.",
            () => StatusColorNotSpecified,
            AppSettings.DefaultStatusColorNotSpecified,
            value => StatusColorNotSpecified = value);
        PickStatusColorOrderedCommand = CreateColorPickerCommand(
            "Bestellt",
            "Statusfarbe für bestellte Aufträge.",
            () => StatusColorOrdered,
            AppSettings.DefaultStatusColorOrdered,
            value => StatusColorOrdered = value);
        PickStatusColorOnTheWayCommand = CreateColorPickerCommand(
            "Unterwegs / Teilweise bereit",
            "Statusfarbe für laufende oder teilweise bereite Aufträge.",
            () => StatusColorOnTheWay,
            AppSettings.DefaultStatusColorOnTheWay,
            value => StatusColorOnTheWay = value);
        PickStatusColorPendingPreparationCommand = CreateColorPickerCommand(
            "Zu richten / Teilweise zu richten",
            "Statusfarbe für zu richtende oder teilweise zu richtende Aufträge.",
            () => StatusColorPendingPreparation,
            AppSettings.DefaultStatusColorPendingPreparation,
            value => StatusColorPendingPreparation = value);
        PickStatusColorInStockCommand = CreateColorPickerCommand(
            "Lieferbereit",
            "Statusfarbe für voll lieferbereite Aufträge.",
            () => StatusColorInStock,
            AppSettings.DefaultStatusColorInStock,
            value => StatusColorInStock = value);
        PickStatusColorPlannedCommand = CreateColorPickerCommand(
            "Eingeplant",
            "Statusfarbe für bereits eingeplante Aufträge und Tourhinweise.",
            () => StatusColorPlanned,
            AppSettings.DefaultStatusColorPlanned,
            value => StatusColorPlanned = value);
        PickCalendarLoadWarningColorCommand = CreateColorPickerCommand(
            "Kalender-Warnung",
            "Akzentfarbe für stark ausgelastete Kalendertage.",
            () => CalendarLoadWarningColor,
            AppSettings.DefaultCalendarLoadWarningColor,
            value => CalendarLoadWarningColor = value);
        PickCalendarLoadCriticalColorCommand = CreateColorPickerCommand(
            "Kalender-Kritisch",
            "Akzentfarbe für kritische Kalendertage.",
            () => CalendarLoadCriticalColor,
            AppSettings.DefaultCalendarLoadCriticalColor,
            value => CalendarLoadCriticalColor = value);
        CreateBackupCommand = new AsyncCommand(CreateBackupAsync);
        RestoreLatestBackupCommand = new AsyncCommand(RestoreLatestBackupAsync);
        CleanupBackupsCommand = new DelegateCommand(CleanupBackups);
        CheckForUpdatesCommand = new AsyncCommand(CheckForUpdatesAsync);
        DownloadAvailableUpdateCommand = new DelegateCommand(DownloadAvailableUpdate, () => CanDownloadAvailableUpdate);
        TestPostgreSqlConnectionCommand = new AsyncCommand(
            TestPostgreSqlConnectionAsync,
            () => IsPostgreSqlStorageMode);
        ActivatePostgreSqlAndRestartCommand = new AsyncCommand(
            ActivatePostgreSqlAndRestartAsync,
            () => IsPostgreSqlStorageMode);
        
        // XML Import Commands
        BrowseXmlImportFileCommand = new DelegateCommand(BrowseXmlImportFile);
        DownloadXmlTemplateCommand = new DelegateCommand(DownloadXmlTemplateFile);
        PreviewXmlImportCommand = new AsyncCommand(
            PreviewXmlImportAsync,
            canExecute: () => !string.IsNullOrWhiteSpace(XmlImportFilePath) && !IsXmlImportBusy);
        ImportOrdersCommand = new AsyncCommand(
            ImportOrdersAsync,
            canExecute: CanImportOrders);

        XmlImportPreviewItems = [];
        XmlImportPreviewErrors = [];
        XmlImportPinIssueItems = [];
        XmlImportStructureFields = CreateXmlImportStructureFields();
        XmlImportAddressFields = CreateXmlImportAddressFields();
        XmlImportOrderFields = CreateXmlImportOrderFields();
        XmlImportProductFields = CreateXmlImportProductFields();

        AttachXmlImportFieldHandlers(XmlImportStructureFields);
        AttachXmlImportFieldHandlers(XmlImportAddressFields);
        AttachXmlImportFieldHandlers(XmlImportOrderFields);
        AttachXmlImportFieldHandlers(XmlImportProductFields);
        if (_dataSyncService is not null)
        {
            _dataSyncService.StatusChanged += OnSyncStatusChanged;
        }
        ApplySyncDiagnostics(_dataSyncService?.GetDiagnosticsSnapshot());

        PropertyChanged += OnSelfPropertyChanged;

        _ = RefreshAsync();
    }

    public ObservableCollection<string> BackupModes { get; }
    public ObservableCollection<StorageModeOption> StorageModeOptions { get; }
    public ObservableCollection<TomTomTrafficSeverityOption> TomTomTrafficSeverityOptions { get; }
    public ObservableCollection<SettingsCategoryNavigationItem> SettingsCategories { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ValidateCommand { get; }

    public ICommand ResetStatusColorsCommand { get; }

    public ICommand PickStatusColorNotSpecifiedCommand { get; }

    public ICommand PickStatusColorOrderedCommand { get; }

    public ICommand PickStatusColorOnTheWayCommand { get; }

    public ICommand PickStatusColorPendingPreparationCommand { get; }

    public ICommand PickStatusColorInStockCommand { get; }

    public ICommand PickStatusColorPlannedCommand { get; }

    public ICommand PickCalendarLoadWarningColorCommand { get; }

    public ICommand PickCalendarLoadCriticalColorCommand { get; }

    public ICommand CreateBackupCommand { get; }

    public ICommand RestoreLatestBackupCommand { get; }

    public ICommand CleanupBackupsCommand { get; }

    public AsyncCommand CheckForUpdatesCommand { get; }

    public DelegateCommand DownloadAvailableUpdateCommand { get; }

    public AsyncCommand TestPostgreSqlConnectionCommand { get; }

    public AsyncCommand ActivatePostgreSqlAndRestartCommand { get; }

    public ICommand BrowseXmlImportFileCommand { get; }

    public ICommand DownloadXmlTemplateCommand { get; }

    public AsyncCommand PreviewXmlImportCommand { get; }

    public AsyncCommand ImportOrdersCommand { get; }

    public ObservableCollection<XmlImportPreviewListItemViewModel> XmlImportPreviewItems { get; }

    public ObservableCollection<string> XmlImportPreviewErrors { get; }

    public ObservableCollection<XmlImportPinIssueListItemViewModel> XmlImportPinIssueItems { get; }

    public ObservableCollection<XmlImportMappingFieldViewModel> XmlImportStructureFields { get; }

    public ObservableCollection<XmlImportMappingFieldViewModel> XmlImportAddressFields { get; }

    public ObservableCollection<XmlImportMappingFieldViewModel> XmlImportOrderFields { get; }

    public ObservableCollection<XmlImportMappingFieldViewModel> XmlImportProductFields { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public SettingsCategoryNavigationItem? SelectedSettingsCategory
    {
        get => _selectedSettingsCategory;
        set
        {
            var target = value ?? SettingsCategories.FirstOrDefault();
            if (SetProperty(ref _selectedSettingsCategory, target))
            {
                OnPropertyChanged(nameof(SelectedSettingsCategoryKey));
                OnPropertyChanged(nameof(SelectedSettingsCategoryTitle));
                OnPropertyChanged(nameof(SelectedSettingsCategoryDescription));
            }
        }
    }

    public string SelectedSettingsCategoryKey => SelectedSettingsCategory?.Key ?? string.Empty;

    public string SelectedSettingsCategoryTitle => SelectedSettingsCategory?.Title ?? "Allgemein";

    public string SelectedSettingsCategoryDescription => SelectedSettingsCategory?.Description ?? "Verwalte die wichtigsten Standardwerte für den Tourenplaner.";

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public string AvisoEmailSubjectTemplate
    {
        get => _avisoEmailSubjectTemplate;
        set => SetProperty(ref _avisoEmailSubjectTemplate, value);
    }

    public string CompanyName
    {
        get => _companyName;
        set => SetProperty(ref _companyName, value);
    }

    public string CompanyStreet
    {
        get => _companyStreet;
        set => SetProperty(ref _companyStreet, value);
    }

    public string CompanyPostalCode
    {
        get => _companyPostalCode;
        set => SetProperty(ref _companyPostalCode, value);
    }

    public string CompanyCity
    {
        get => _companyCity;
        set => SetProperty(ref _companyCity, value);
    }

    public AppStorageMode StorageMode
    {
        get => _storageMode;
        set
        {
            if (SetProperty(ref _storageMode, value))
            {
                OnPropertyChanged(nameof(SelectedStorageModeOption));
                OnPropertyChanged(nameof(IsPostgreSqlStorageMode));
                OnPropertyChanged(nameof(StorageModeChangeHintText));
                TestPostgreSqlConnectionCommand.RaiseCanExecuteChanged();
                ActivatePostgreSqlAndRestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public StorageModeOption? SelectedStorageModeOption
    {
        get => StorageModeOptions.FirstOrDefault(x => x.Value == StorageMode);
        set
        {
            if (value is not null)
            {
                StorageMode = value.Value;
            }
        }
    }

    public bool IsPostgreSqlStorageMode => StorageMode == AppStorageMode.PostgreSql;

    public string ActiveStorageModeDisplayName => _activeStorageMode == AppStorageMode.PostgreSql
        ? "PostgreSQL Mehrbenutzer"
        : "Lokale JSON-Dateien";

    public string ActiveStorageModeDetailText => _activeStorageMode == AppStorageMode.PostgreSql
        ? "Mehrbenutzerbetrieb mit zentraler Datenbank aktiv"
        : "Lokaler Dateibetrieb aktiv";

    public string StorageModeChangeHintText => IsPostgreSqlStorageMode
        ? "PostgreSQL ist aktiv. Die Umstellung wird nach dem Speichern beim nächsten App-Start verwendet."
        : "JSON-Dateien sind aktiv. Die Umstellung wird nach dem Speichern beim nächsten App-Start verwendet.";

    public string PostgreSqlHost
    {
        get => _postgreSqlHost;
        set => SetProperty(ref _postgreSqlHost, value);
    }

    public int PostgreSqlPort
    {
        get => _postgreSqlPort;
        set => SetProperty(ref _postgreSqlPort, value);
    }

    public string PostgreSqlDatabase
    {
        get => _postgreSqlDatabase;
        set => SetProperty(ref _postgreSqlDatabase, value);
    }

    public string PostgreSqlSchema
    {
        get => _postgreSqlSchema;
        set => SetProperty(ref _postgreSqlSchema, value);
    }

    public string PostgreSqlUsername
    {
        get => _postgreSqlUsername;
        set => SetProperty(ref _postgreSqlUsername, value);
    }

    public string PostgreSqlPassword
    {
        get => _postgreSqlPassword;
        set => SetProperty(ref _postgreSqlPassword, value);
    }

    public bool PostgreSqlUseSsl
    {
        get => _postgreSqlUseSsl;
        set => SetProperty(ref _postgreSqlUseSsl, value);
    }

    public int PostgreSqlTimeoutSeconds
    {
        get => _postgreSqlTimeoutSeconds;
        set => SetProperty(ref _postgreSqlTimeoutSeconds, value);
    }

    public string StatusColorNotSpecified
    {
        get => _statusColorNotSpecified;
        set => SetColorProperty(ref _statusColorNotSpecified, value, AppSettings.DefaultStatusColorNotSpecified);
    }

    public string StatusColorOrdered
    {
        get => _statusColorOrdered;
        set => SetColorProperty(ref _statusColorOrdered, value, AppSettings.DefaultStatusColorOrdered);
    }

    public string StatusColorOnTheWay
    {
        get => _statusColorOnTheWay;
        set => SetColorProperty(ref _statusColorOnTheWay, value, AppSettings.DefaultStatusColorOnTheWay);
    }

    public string StatusColorPendingPreparation
    {
        get => _statusColorPendingPreparation;
        set => SetColorProperty(ref _statusColorPendingPreparation, value, AppSettings.DefaultStatusColorPendingPreparation);
    }

    public string StatusColorInStock
    {
        get => _statusColorInStock;
        set => SetColorProperty(ref _statusColorInStock, value, AppSettings.DefaultStatusColorInStock);
    }

    public string StatusColorPlanned
    {
        get => _statusColorPlanned;
        set => SetColorProperty(ref _statusColorPlanned, value, AppSettings.DefaultStatusColorPlanned);
    }

    public string CalendarLoadWarningColor
    {
        get => _calendarLoadWarningColor;
        set => SetColorProperty(ref _calendarLoadWarningColor, value, AppSettings.DefaultCalendarLoadWarningColor);
    }

    public string CalendarLoadCriticalColor
    {
        get => _calendarLoadCriticalColor;
        set => SetColorProperty(ref _calendarLoadCriticalColor, value, AppSettings.DefaultCalendarLoadCriticalColor);
    }

    public bool MapUseDistinctPlannedTourColors
    {
        get => _mapUseDistinctPlannedTourColors;
        set => SetProperty(ref _mapUseDistinctPlannedTourColors, value);
    }

    public int CalendarLoadWarningPeopleThreshold
    {
        get => _calendarLoadWarningPeopleThreshold;
        set => SetProperty(ref _calendarLoadWarningPeopleThreshold, value);
    }

    public int CalendarLoadCriticalPeopleThreshold
    {
        get => _calendarLoadCriticalPeopleThreshold;
        set => SetProperty(ref _calendarLoadCriticalPeopleThreshold, value);
    }

    public bool MapAutoOpenDetailsOnPinSelection
    {
        get => _mapAutoOpenDetailsOnPinSelection;
        set => SetProperty(ref _mapAutoOpenDetailsOnPinSelection, value);
    }

    public bool MapSearchDimNonMatchingPins
    {
        get => _mapSearchDimNonMatchingPins;
        set => SetProperty(ref _mapSearchDimNonMatchingPins, value);
    }

    public bool MapPinInfoCardShowName
    {
        get => _mapPinInfoCardShowName;
        set => SetProperty(ref _mapPinInfoCardShowName, value);
    }

    public bool MapPinInfoCardShowOrderNumber
    {
        get => _mapPinInfoCardShowOrderNumber;
        set => SetProperty(ref _mapPinInfoCardShowOrderNumber, value);
    }

    public bool MapPinInfoCardShowStreet
    {
        get => _mapPinInfoCardShowStreet;
        set => SetProperty(ref _mapPinInfoCardShowStreet, value);
    }

    public bool MapPinInfoCardShowPostalCodeCity
    {
        get => _mapPinInfoCardShowPostalCodeCity;
        set => SetProperty(ref _mapPinInfoCardShowPostalCodeCity, value);
    }

    public bool MapPinInfoCardShowNotes
    {
        get => _mapPinInfoCardShowNotes;
        set => SetProperty(ref _mapPinInfoCardShowNotes, value);
    }

    public bool MapPinInfoCardShowProducts
    {
        get => _mapPinInfoCardShowProducts;
        set => SetProperty(ref _mapPinInfoCardShowProducts, value);
    }

    public bool MapPinInfoCardShowTotalWeight
    {
        get => _mapPinInfoCardShowTotalWeight;
        set => SetProperty(ref _mapPinInfoCardShowTotalWeight, value);
    }

    public double PinInfoCardZoomBehaviorStrength
    {
        get => _pinInfoCardZoomBehaviorStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.2d, 4.0d);
            if (SetProperty(ref _pinInfoCardZoomBehaviorStrength, clamped))
            {
                RequestPinInfoCardZoomBehaviorStrengthSave();
            }
        }
    }

    public int MapRouteCapacityWarningThresholdPercent
    {
        get => _mapRouteCapacityWarningThresholdPercent;
        set => SetProperty(ref _mapRouteCapacityWarningThresholdPercent, value);
    }

    public string TomTomApiKey
    {
        get => _tomTomApiKey;
        set => SetProperty(ref _tomTomApiKey, value);
    }

    public int TomTomTrafficRefreshSeconds
    {
        get => _tomTomTrafficRefreshSeconds;
        set => SetProperty(ref _tomTomTrafficRefreshSeconds, value);
    }

    public int TomTomRouteRecalcDebounceMs
    {
        get => _tomTomRouteRecalcDebounceMs;
        set => SetProperty(ref _tomTomRouteRecalcDebounceMs, value);
    }

    public int TomTomVehicleOnlyMaxSpeedKmh
    {
        get => _tomTomVehicleOnlyMaxSpeedKmh;
        set => SetProperty(ref _tomTomVehicleOnlyMaxSpeedKmh, value);
    }

    public int TomTomVehicleWithTrailerMaxSpeedKmh
    {
        get => _tomTomVehicleWithTrailerMaxSpeedKmh;
        set => SetProperty(ref _tomTomVehicleWithTrailerMaxSpeedKmh, value);
    }

    public int TrafficBufferPercentFrom0500To0730
    {
        get => _trafficBufferPercentFrom0500To0730;
        set => SetProperty(ref _trafficBufferPercentFrom0500To0730, value);
    }

    public int TrafficBufferPercentFrom0730To0900
    {
        get => _trafficBufferPercentFrom0730To0900;
        set => SetProperty(ref _trafficBufferPercentFrom0730To0900, value);
    }

    public int TrafficBufferPercentFrom0900To1530
    {
        get => _trafficBufferPercentFrom0900To1530;
        set => SetProperty(ref _trafficBufferPercentFrom0900To1530, value);
    }

    public int TrafficBufferPercentFrom1530To1830
    {
        get => _trafficBufferPercentFrom1530To1830;
        set => SetProperty(ref _trafficBufferPercentFrom1530To1830, value);
    }

    public TomTomTrafficSeverityOption? SelectedTomTomTrafficSeverityOption
    {
        get => TomTomTrafficSeverityOptions.FirstOrDefault(x => string.Equals(x.Key, _tomTomTrafficSeverityMode, StringComparison.OrdinalIgnoreCase))
               ?? TomTomTrafficSeverityOptions.FirstOrDefault();
        set
        {
            if (value is null)
            {
                return;
            }

            TomTomTrafficSeverityMode = value.Key;
        }
    }

    public string TomTomTrafficSeverityMode
    {
        get => _tomTomTrafficSeverityMode;
        set
        {
            var normalized = AppSettings.NormalizeTomTomTrafficSeverityMode(value);
            if (SetProperty(ref _tomTomTrafficSeverityMode, normalized))
            {
                OnPropertyChanged(nameof(SelectedTomTomTrafficSeverityOption));
            }
        }
    }

    public bool TomTomEnableTileCache
    {
        get => _tomTomEnableTileCache;
        set => SetProperty(ref _tomTomEnableTileCache, value);
    }

    public bool BackupsEnabled
    {
        get => _backupsEnabled;
        set => SetProperty(ref _backupsEnabled, value);
    }

    public string BackupDir
    {
        get => _backupDir;
        set => SetProperty(ref _backupDir, value);
    }

    public string BackupModeDefault
    {
        get => _backupModeDefault;
        set => SetProperty(ref _backupModeDefault, value);
    }

    public int BackupRetentionDays
    {
        get => _backupRetentionDays;
        set => SetProperty(ref _backupRetentionDays, value);
    }

    public bool AutoBackupEnabled
    {
        get => _autoBackupEnabled;
        set => SetProperty(ref _autoBackupEnabled, value);
    }

    public int AutoBackupIntervalDays
    {
        get => _autoBackupIntervalDays;
        set => SetProperty(ref _autoBackupIntervalDays, value);
    }

    public string LastBackupIso
    {
        get => _lastBackupIso;
        private set => SetProperty(ref _lastBackupIso, value);
    }

    public string ApplicationVersion
    {
        get => _applicationVersion;
        private set => SetProperty(ref _applicationVersion, value);
    }

    public string RuntimeVersion
    {
        get => _runtimeVersion;
        private set => SetProperty(ref _runtimeVersion, value);
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public string AvailableUpdateVersion
    {
        get => _availableUpdateVersion;
        private set => SetProperty(ref _availableUpdateVersion, value);
    }

    public string LastUpdateCheckText
    {
        get => _lastUpdateCheckText;
        private set => SetProperty(ref _lastUpdateCheckText, value);
    }

    public string UpdatePublishedAtText
    {
        get => _updatePublishedAtText;
        private set => SetProperty(ref _updatePublishedAtText, value);
    }

    public string UpdateConfigPath
    {
        get => _updateConfigPath;
        private set => SetProperty(ref _updateConfigPath, value);
    }

    public string StartupStatusText
    {
        get => _startupStatusText;
        private set => SetProperty(ref _startupStatusText, value);
    }

    public string StartupDetailText
    {
        get => _startupDetailText;
        private set => SetProperty(ref _startupDetailText, value);
    }

    public string StartupRecordedAtText
    {
        get => _startupRecordedAtText;
        private set => SetProperty(ref _startupRecordedAtText, value);
    }

    public string StartupLastStepText
    {
        get => _startupLastStepText;
        private set => SetProperty(ref _startupLastStepText, value);
    }

    public string DiagnosticsFilePath
    {
        get => _diagnosticsFilePath;
        private set => SetProperty(ref _diagnosticsFilePath, value);
    }

    public string CrashLogPath
    {
        get => _crashLogPath;
        private set => SetProperty(ref _crashLogPath, value);
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        private set => SetProperty(ref _syncStatusText, value);
    }

    public string SyncModeText
    {
        get => _syncModeText;
        private set => SetProperty(ref _syncModeText, value);
    }

    public string LastSyncStatusChangeText
    {
        get => _lastSyncStatusChangeText;
        private set => SetProperty(ref _lastSyncStatusChangeText, value);
    }

    public string LastRemoteSyncText
    {
        get => _lastRemoteSyncText;
        private set => SetProperty(ref _lastRemoteSyncText, value);
    }

    public string LastLocalSyncText
    {
        get => _lastLocalSyncText;
        private set => SetProperty(ref _lastLocalSyncText, value);
    }

    public string LastSyncActivityText
    {
        get => _lastSyncActivityText;
        private set => SetProperty(ref _lastSyncActivityText, value);
    }

    public string SyncErrorText
    {
        get => _syncErrorText;
        private set => SetProperty(ref _syncErrorText, value);
    }

    public bool CanDownloadAvailableUpdate => _isUpdateAvailable && !string.IsNullOrWhiteSpace(_updateActionUrl);

    public string LatestBackupFile
    {
        get => _latestBackupFile;
        private set
        {
            if (SetProperty(ref _latestBackupFile, value))
            {
                OnPropertyChanged(nameof(LatestBackupSummaryText));
            }
        }
    }

    public string LatestBackupModifiedText
    {
        get => _latestBackupModifiedText;
        private set
        {
            if (SetProperty(ref _latestBackupModifiedText, value))
            {
                OnPropertyChanged(nameof(LatestBackupSummaryText));
            }
        }
    }

    public int AvailableBackupsCount
    {
        get => _availableBackupsCount;
        private set => SetProperty(ref _availableBackupsCount, value);
    }

    public string LatestBackupSummaryText =>
        string.Equals(LatestBackupFile, "n/a", StringComparison.OrdinalIgnoreCase)
            ? "Keine Sicherung vorhanden"
            : $"{LatestBackupModifiedText} | {LatestBackupFile}";

    public bool ShowGpsTool
    {
        get => _showGpsTool;
        set => SetProperty(ref _showGpsTool, value);
    }

    public string GpsToolUrl
    {
        get => _gpsToolUrl;
        set => SetProperty(ref _gpsToolUrl, value);
    }

    public bool ShowSpediteurTool
    {
        get => _showSpediteurTool;
        set => SetProperty(ref _showSpediteurTool, value);
    }

    public string SpediteurToolUrl
    {
        get => _spediteurToolUrl;
        set => SetProperty(ref _spediteurToolUrl, value);
    }

    public string TourDefaultStartTime
    {
        get => _tourDefaultStartTime;
        set => SetProperty(ref _tourDefaultStartTime, value);
    }

    public string XmlImportFilePath
    {
        get => _xmlImportFilePath;
        set => SetProperty(ref _xmlImportFilePath, value);
    }

    public bool IsImportingOrders
    {
        get => _isImportingOrders;
        set
        {
            if (SetProperty(ref _isImportingOrders, value))
            {
                OnPropertyChanged(nameof(IsXmlImportBusy));
            }
        }
    }

    public bool IsPreviewingXmlImport
    {
        get => _isPreviewingXmlImport;
        private set
        {
            if (SetProperty(ref _isPreviewingXmlImport, value))
            {
                OnPropertyChanged(nameof(IsXmlImportBusy));
            }
        }
    }

    public string ImportStatusMessage
    {
        get => _importStatusMessage;
        set => SetProperty(ref _importStatusMessage, value);
    }

    public string XmlImportPreviewSummary
    {
        get => _xmlImportPreviewSummary;
        private set => SetProperty(ref _xmlImportPreviewSummary, value);
    }

    public string XmlImportPinIssueSummary
    {
        get => _xmlImportPinIssueSummary;
        private set => SetProperty(ref _xmlImportPinIssueSummary, value);
    }

    public bool IsXmlImportBusy => IsImportingOrders || IsPreviewingXmlImport;

    public bool HasXmlImportPreview => !string.IsNullOrWhiteSpace(XmlImportPreviewSummary);

    public bool HasXmlImportPreviewItems => XmlImportPreviewItems.Count > 0;

    public bool HasXmlImportPreviewErrors => XmlImportPreviewErrors.Count > 0;

    public bool HasXmlImportPinIssues => XmlImportPinIssueItems.Count > 0;

    public bool HasXmlImportPinIssueSummary => !string.IsNullOrWhiteSpace(XmlImportPinIssueSummary);

    public bool HasXmlImportPreviewHiddenItems => _xmlImportPreviewHiddenItemCount > 0;

    public string XmlImportPreviewHiddenItemsText => HasXmlImportPreviewHiddenItems
        ? $"{_xmlImportPreviewHiddenItemCount} weitere Position(en) sind aus Performance-Gründen ausgeblendet."
        : string.Empty;

    public async Task RefreshAsync()
    {
        _suppressAutoSave = true;
        try
        {
            var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            ApplicationVersion =
                entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? entryAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? entryAssembly.GetName().Version?.ToString()
                ?? "0.0.0";
            RuntimeVersion = Environment.Version.ToString();
            ResetUpdateStatus();
            UpdateConfigPath = AppRuntimeDiagnosticsService.FindUpdateConfigPath(AppContext.BaseDirectory) ?? "Nicht gefunden";
            CrashLogPath = AppRuntimeDiagnosticsService.GetCrashLogPath(_dataRoot);
            DiagnosticsFilePath = AppRuntimeDiagnosticsService.GetStartupDiagnosticsPath(_dataRoot);
            ApplyStartupDiagnostics(AppRuntimeDiagnosticsService.LoadStartupDiagnostics(_dataRoot));
            ApplySyncDiagnostics(_dataSyncService?.GetDiagnosticsSnapshot());

            var settings = await _repository.LoadAsync();
            _activeStorageMode = settings.StorageMode;
            ApplyModel(settings);
            OnPropertyChanged(nameof(ActiveStorageModeDisplayName));
            OnPropertyChanged(nameof(ActiveStorageModeDetailText));
            UpdateBackupStatus(settings.BackupDir);
            ValidationSummary = string.Empty;
            StatusText = string.Empty;
            await CheckForUpdatesCoreAsync(showToastWhenUpToDate: false);
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    public async Task SaveAsync()
    {
        await SaveCoreAsync(showToast: true);
    }

    private async Task SaveCoreAsync(bool showToast)
    {
        var existingSettings = await _repository.LoadAsync();
        var model = BuildModel(existingSettings);
        var validation = _validator.Validate(model);
        if (!validation.IsValid)
        {
            ValidationSummary = string.Join(Environment.NewLine, validation.Errors);
            StatusText = showToast
                ? "Validation failed. Settings were not saved."
                : "Auto-save paused: validation failed.";
            return;
        }

        await _repository.SaveAsync(model);
        ValidationSummary = string.Empty;
        UpdateBackupStatus(model.BackupDir);
        _dataSyncService?.PublishSettings(_instanceId);
        StatusText = showToast ? "Settings saved." : string.Empty;
        if (showToast)
        {
            ToastNotificationService.ShowInfo("Einstellungen gespeichert.");
        }
    }

    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(XmlImportFilePath))
        {
            ClearXmlImportPreview(clearStatus: false);
            RaiseXmlImportCommandStates();
        }
        else if (e.PropertyName == nameof(IsImportingOrders) || e.PropertyName == nameof(IsPreviewingXmlImport))
        {
            RaiseXmlImportCommandStates();
        }

        if (_suppressAutoSave || _autoSaveInProgress)
        {
            return;
        }

        if (!ShouldAutoSaveProperty(e.PropertyName))
        {
            return;
        }

        ScheduleAutoSave();
    }

    private async void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        try
        {
            await Task.Delay(700, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _autoSaveInProgress = true;
            await SaveCoreAsync(showToast: false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _autoSaveInProgress = false;
        }
    }

    private static bool ShouldAutoSaveProperty(string? propertyName)
    {
        return propertyName is
            nameof(AvisoEmailSubjectTemplate) or
            nameof(CompanyName) or
            nameof(CompanyStreet) or
            nameof(CompanyPostalCode) or
            nameof(CompanyCity) or
            nameof(StorageMode) or
            nameof(PostgreSqlHost) or
            nameof(PostgreSqlPort) or
            nameof(PostgreSqlDatabase) or
            nameof(PostgreSqlSchema) or
            nameof(PostgreSqlUsername) or
            nameof(PostgreSqlPassword) or
            nameof(PostgreSqlUseSsl) or
            nameof(PostgreSqlTimeoutSeconds) or
            nameof(StatusColorNotSpecified) or
            nameof(StatusColorOrdered) or
            nameof(StatusColorOnTheWay) or
            nameof(StatusColorPendingPreparation) or
            nameof(StatusColorInStock) or
            nameof(StatusColorPlanned) or
            nameof(CalendarLoadWarningColor) or
            nameof(CalendarLoadCriticalColor) or
            nameof(MapUseDistinctPlannedTourColors) or
            nameof(CalendarLoadWarningPeopleThreshold) or
            nameof(CalendarLoadCriticalPeopleThreshold) or
            nameof(MapAutoOpenDetailsOnPinSelection) or
            nameof(MapSearchDimNonMatchingPins) or
            nameof(MapPinInfoCardShowName) or
            nameof(MapPinInfoCardShowOrderNumber) or
            nameof(MapPinInfoCardShowStreet) or
            nameof(MapPinInfoCardShowPostalCodeCity) or
            nameof(MapPinInfoCardShowNotes) or
            nameof(MapPinInfoCardShowProducts) or
            nameof(MapPinInfoCardShowTotalWeight) or
            nameof(PinInfoCardZoomBehaviorStrength) or
            nameof(MapRouteCapacityWarningThresholdPercent) or
            nameof(TomTomApiKey) or
            nameof(TomTomTrafficRefreshSeconds) or
            nameof(TomTomRouteRecalcDebounceMs) or
            nameof(TomTomVehicleOnlyMaxSpeedKmh) or
            nameof(TomTomVehicleWithTrailerMaxSpeedKmh) or
            nameof(TrafficBufferPercentFrom0500To0730) or
            nameof(TrafficBufferPercentFrom0730To0900) or
            nameof(TrafficBufferPercentFrom0900To1530) or
            nameof(TrafficBufferPercentFrom1530To1830) or
            nameof(TomTomTrafficSeverityMode) or
            nameof(TomTomEnableTileCache) or
            nameof(BackupsEnabled) or
            nameof(BackupDir) or
            nameof(BackupModeDefault) or
            nameof(BackupRetentionDays) or
            nameof(AutoBackupEnabled) or
            nameof(AutoBackupIntervalDays) or
            nameof(ShowGpsTool) or
            nameof(GpsToolUrl) or
            nameof(ShowSpediteurTool) or
            nameof(SpediteurToolUrl) or
            nameof(TourDefaultStartTime) or
            nameof(XmlImportFilePath);
    }

    public async Task CreateBackupAsync()
    {
        var model = BuildModel(await _repository.LoadAsync());
        var validation = _validator.Validate(model);
        if (!validation.IsValid)
        {
            ValidationSummary = string.Join(Environment.NewLine, validation.Errors);
            StatusText = "Backup cancelled because settings are invalid.";
            return;
        }

        var backupPath = await _backupManager.CreateBackupAsync(
            "GAWELA_Tourenplaner",
            _dataRoot,
            _dataRoot,
            model.BackupDir,
            model.BackupModeDefault);

        model.LastBackupIso = DateTimeOffset.Now.ToString("O");
        LastBackupIso = model.LastBackupIso;
        await _repository.SaveAsync(model);
        UpdateBackupStatus(model.BackupDir);

        StatusText = $"Backup created: {Path.GetFileName(backupPath)}";
        ValidationSummary = string.Empty;
    }

    public async Task RestoreLatestBackupAsync()
    {
        var model = BuildModel(await _repository.LoadAsync());
        if (string.IsNullOrWhiteSpace(model.BackupDir) || !Directory.Exists(model.BackupDir))
        {
            StatusText = "Restore failed: backup directory not found.";
            return;
        }

        var latestBackup = Directory.GetFiles(model.BackupDir, "*.bak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latestBackup is null)
        {
            StatusText = "Restore skipped: no backup file found.";
            return;
        }

        await _backupManager.RestoreBackupAsync(
            latestBackup,
            _dataRoot,
            _dataRoot,
            selectedGroups: ["all"]);

        StatusText = $"Restore completed from {Path.GetFileName(latestBackup)}.";
        await RefreshAsync();
    }

    public void CleanupBackups()
    {
        var model = BuildModel();
        _backupManager.CleanupOldBackups(model.BackupDir, model.BackupRetentionDays);
        UpdateBackupStatus(model.BackupDir);
        StatusText = "Backup cleanup executed.";
    }

    private async Task CheckForUpdatesAsync()
    {
        await CheckForUpdatesCoreAsync(showToastWhenUpToDate: true);
    }

    private async Task TestPostgreSqlConnectionAsync()
    {
        try
        {
            var databaseName = await VerifyPostgreSqlConnectionAsync();

            ValidationSummary = string.Empty;
            StatusText = $"PostgreSQL-Verbindung erfolgreich. Datenbank: {databaseName ?? PostgreSqlDatabase}, Schema: {BuildPostgreSqlStorageSettings().Schema}.";
            ToastNotificationService.ShowInfo("PostgreSQL-Verbindung erfolgreich getestet.");
        }
        catch (Exception ex)
        {
            StatusText = $"PostgreSQL-Verbindung fehlgeschlagen: {ex.Message}";
        }
    }

    private async Task ActivatePostgreSqlAndRestartAsync()
    {
        try
        {
            await VerifyPostgreSqlConnectionAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"PostgreSQL-Aktivierung abgebrochen: {ex.Message}";
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"Die PostgreSQL-Verbindung konnte nicht geprüft werden.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "PostgreSQL aktivieren",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var model = BuildModel();
        var validation = _validator.Validate(model);
        if (!validation.IsValid)
        {
            ValidationSummary = string.Join(Environment.NewLine, validation.Errors);
            StatusText = "PostgreSQL-Aktivierung abgebrochen: Einstellungen sind noch ungueltig.";
            return;
        }

        await _repository.SaveAsync(model);
        ValidationSummary = string.Empty;
        StatusText = "PostgreSQL wird aktiviert. Die App startet neu...";

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            StatusText = "Neustart fehlgeschlagen: Programmdatei wurde nicht gefunden.";
            return;
        }

        Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
        });

        System.Windows.Application.Current.Shutdown();
    }

    private async Task CheckForUpdatesCoreAsync(bool showToastWhenUpToDate)
    {
        UpdateStatusText = "Suche nach Updates...";
        UpdateConfigPath = AppRuntimeDiagnosticsService.FindUpdateConfigPath(AppContext.BaseDirectory) ?? "Nicht gefunden";

        try
        {
            var result = await AppUpdateStatusService.CheckAsync();
            ApplicationVersion = result.CurrentVersion;
            UpdateStatusText = result.StatusText;
            AvailableUpdateVersion = result.IsUpdateAvailable
                ? result.AvailableVersion ?? "-"
                : "Kein Update verfügbar";
            LastUpdateCheckText = result.CheckedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            UpdatePublishedAtText = TryFormatPublishedAt(result.PublishedAtUtc);
            _updateActionUrl = result.InstallerUrl?.Trim() ?? string.Empty;
            _isUpdateAvailable = result.IsUpdateAvailable;
            DownloadAvailableUpdateCommand.RaiseCanExecuteChanged();

            if (result.IsUpdateAvailable)
            {
                UpdateStatusText = $"Update {result.AvailableVersion} wird vorbereitet...";

                var progress = new Progress<string>(message => UpdateStatusText = message);
                var installResult = await InstalledAppUpdateService.TryApplyUpdateAsync(progress);
                if (installResult.UpdateWasStarted)
                {
                    System.Windows.Application.Current.Shutdown();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(installResult.ErrorMessage))
                {
                    UpdateStatusText = $"Update konnte nicht gestartet werden: {installResult.ErrorMessage}";
                    ToastNotificationService.ShowInfo("Das Update konnte nicht gestartet werden.");
                    return;
                }

                UpdateStatusText = $"Update {result.AvailableVersion} ist verfügbar.";
            }
            else if (showToastWhenUpToDate)
            {
                ToastNotificationService.ShowInfo("Es ist bereits die neueste Version installiert.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Update-Prüfung fehlgeschlagen: {ex.Message}";
            AvailableUpdateVersion = "Nicht verfügbar";
            LastUpdateCheckText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            UpdatePublishedAtText = "-";
            _updateActionUrl = string.Empty;
            _isUpdateAvailable = false;
            DownloadAvailableUpdateCommand.RaiseCanExecuteChanged();
            ToastNotificationService.ShowInfo("Die Update-Prüfung konnte nicht abgeschlossen werden.");
        }
    }

    private void DownloadAvailableUpdate()
    {
        if (string.IsNullOrWhiteSpace(_updateActionUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(_updateActionUrl)
        {
            UseShellExecute = true
        });
    }

    private void ValidateCurrentSettings()
    {
        var validation = _validator.Validate(BuildModel());
        ValidationSummary = validation.IsValid
            ? "Settings validation successful."
            : string.Join(Environment.NewLine, validation.Errors);
        StatusText = validation.IsValid ? "Validation OK." : "Validation reported issues.";
    }

    private void ResetStatusColors()
    {
        StatusColorNotSpecified = AppSettings.DefaultStatusColorNotSpecified;
        StatusColorOrdered = AppSettings.DefaultStatusColorOrdered;
        StatusColorOnTheWay = AppSettings.DefaultStatusColorOnTheWay;
        StatusColorPendingPreparation = AppSettings.DefaultStatusColorPendingPreparation;
        StatusColorInStock = AppSettings.DefaultStatusColorInStock;
        StatusColorPlanned = AppSettings.DefaultStatusColorPlanned;
        CalendarLoadWarningColor = AppSettings.DefaultCalendarLoadWarningColor;
        CalendarLoadCriticalColor = AppSettings.DefaultCalendarLoadCriticalColor;
        MapUseDistinctPlannedTourColors = true;
        StatusText = "Farben auf Standard zurückgesetzt.";
    }

    private void ResetUpdateStatus()
    {
        UpdateStatusText = "Noch nicht geprüft.";
        AvailableUpdateVersion = "Noch nicht geprüft";
        LastUpdateCheckText = "Noch nicht geprüft";
        UpdatePublishedAtText = "-";
        UpdateConfigPath = AppRuntimeDiagnosticsService.FindUpdateConfigPath(AppContext.BaseDirectory) ?? "Nicht gefunden";
        _updateActionUrl = string.Empty;
        _isUpdateAvailable = false;
        DownloadAvailableUpdateCommand.RaiseCanExecuteChanged();
    }

    private void OnSyncStatusChanged(object? sender, AppDataSyncDiagnosticsSnapshot snapshot)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnSyncStatusChanged(sender, snapshot));
            return;
        }

        ApplySyncDiagnostics(snapshot);
    }

    private void ApplyStartupDiagnostics(AppStartupDiagnosticsSnapshot snapshot)
    {
        StartupStatusText = snapshot.Summary;
        StartupDetailText = string.IsNullOrWhiteSpace(snapshot.Detail) ? "-" : snapshot.Detail!;
        StartupLastStepText = string.IsNullOrWhiteSpace(snapshot.LastStep) ? "-" : snapshot.LastStep!;
        StartupRecordedAtText = snapshot.RecordedAtUtc == DateTime.MinValue
            ? "Noch keine Startdiagnose vorhanden."
            : snapshot.RecordedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        DiagnosticsFilePath = string.IsNullOrWhiteSpace(snapshot.DiagnosticsFilePath)
            ? AppRuntimeDiagnosticsService.GetStartupDiagnosticsPath(_dataRoot)
            : snapshot.DiagnosticsFilePath;
        CrashLogPath = string.IsNullOrWhiteSpace(snapshot.CrashLogPath)
            ? AppRuntimeDiagnosticsService.GetCrashLogPath(_dataRoot)
            : snapshot.CrashLogPath;
    }

    private void ApplySyncDiagnostics(AppDataSyncDiagnosticsSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            SyncModeText = "Lokaler Dateibetrieb";
            SyncStatusText = "Lokaler Dateibetrieb ohne Live-Sync.";
            LastSyncStatusChangeText = "Noch keine Statusaenderung";
            LastRemoteSyncText = "Noch keine externe Aenderung";
            LastLocalSyncText = "Noch keine lokale Aenderung";
            LastSyncActivityText = "Noch keine Synchronisationsaktivitaet";
            SyncErrorText = "-";
            return;
        }

        SyncModeText = snapshot.IsRemoteSyncEnabled
            ? (snapshot.IsRemoteSyncConnected ? "PostgreSQL Mehrbenutzer verbunden" : "PostgreSQL Mehrbenutzer getrennt")
            : "Lokaler Dateibetrieb";
        SyncStatusText = snapshot.StatusText;
        LastSyncStatusChangeText = FormatOptionalDateTime(snapshot.LastStatusChangeUtc, "Noch keine Statusaenderung");
        LastRemoteSyncText = FormatOptionalDateTime(snapshot.LastRemoteChangeUtc, "Noch keine externe Aenderung");
        LastLocalSyncText = FormatOptionalDateTime(snapshot.LastLocalPublishUtc, "Noch keine lokale Aenderung");
        LastSyncActivityText = FormatOptionalDateTime(snapshot.LastAnyChangeUtc, "Noch keine Synchronisationsaktivitaet");
        SyncErrorText = string.IsNullOrWhiteSpace(snapshot.LastErrorMessage) ? "-" : snapshot.LastErrorMessage!;
    }

    private AppSettings BuildModel(AppSettings? existingSettings = null)
    {
        var model = existingSettings ?? new AppSettings();
        var currentUserName = AppSettings.NormalizeUserName(_currentUserName);
        var userPreference = model.ResolveUserPreference(currentUserName);

        userPreference.AppearanceMode = "Light";
        userPreference.AvisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(AvisoEmailSubjectTemplate)
            ? AppSettings.DefaultAvisoEmailSubjectTemplate
            : AvisoEmailSubjectTemplate.Trim();
        userPreference.StatusColorNotSpecified = NormalizeHexColor(StatusColorNotSpecified, AppSettings.DefaultStatusColorNotSpecified);
        userPreference.StatusColorOrdered = NormalizeHexColor(StatusColorOrdered, AppSettings.DefaultStatusColorOrdered);
        userPreference.StatusColorOnTheWay = NormalizeHexColor(StatusColorOnTheWay, AppSettings.DefaultStatusColorOnTheWay);
        userPreference.StatusColorPendingPreparation = NormalizeHexColor(StatusColorPendingPreparation, AppSettings.DefaultStatusColorPendingPreparation);
        userPreference.StatusColorInStock = NormalizeHexColor(StatusColorInStock, AppSettings.DefaultStatusColorInStock);
        userPreference.StatusColorPlanned = NormalizeHexColor(StatusColorPlanned, AppSettings.DefaultStatusColorPlanned);
        userPreference.CalendarLoadWarningColor = NormalizeHexColor(CalendarLoadWarningColor, AppSettings.DefaultCalendarLoadWarningColor);
        userPreference.CalendarLoadCriticalColor = NormalizeHexColor(CalendarLoadCriticalColor, AppSettings.DefaultCalendarLoadCriticalColor);
        userPreference.MapUseDistinctPlannedTourColors = MapUseDistinctPlannedTourColors;
        userPreference.CalendarLoadWarningPeopleThreshold = CalendarLoadWarningPeopleThreshold;
        userPreference.CalendarLoadCriticalPeopleThreshold = CalendarLoadCriticalPeopleThreshold;
        userPreference.MapAutoOpenDetailsOnPinSelection = MapAutoOpenDetailsOnPinSelection;
        userPreference.MapSearchDimNonMatchingPins = MapSearchDimNonMatchingPins;
        userPreference.MapPinInfoCardShowName = MapPinInfoCardShowName;
        userPreference.MapPinInfoCardShowOrderNumber = MapPinInfoCardShowOrderNumber;
        userPreference.MapPinInfoCardShowStreet = MapPinInfoCardShowStreet;
        userPreference.MapPinInfoCardShowPostalCodeCity = MapPinInfoCardShowPostalCodeCity;
        userPreference.MapPinInfoCardShowNotes = MapPinInfoCardShowNotes;
        userPreference.MapPinInfoCardShowProducts = MapPinInfoCardShowProducts;
        userPreference.MapPinInfoCardShowTotalWeight = MapPinInfoCardShowTotalWeight;
        userPreference.PinInfoCardZoomBehaviorStrength = Math.Clamp(PinInfoCardZoomBehaviorStrength, 0.2d, 4.0d);
        userPreference.MapRouteCapacityWarningThresholdPercent = Math.Clamp(MapRouteCapacityWarningThresholdPercent, 0, 100);
        userPreference.ShowGpsTool = ShowGpsTool;
        userPreference.GpsToolUrl = string.IsNullOrWhiteSpace(GpsToolUrl) ? AppSettings.DefaultGpsToolUrl : GpsToolUrl.Trim();
        userPreference.ShowSpediteurTool = ShowSpediteurTool;
        userPreference.SpediteurToolUrl = string.IsNullOrWhiteSpace(SpediteurToolUrl) ? AppSettings.DefaultSpediteurToolUrl : SpediteurToolUrl.Trim();
        userPreference.TourDefaultStartTime = NormalizeTourDefaultStartTime(TourDefaultStartTime);
        userPreference.TomTomTrafficRefreshSeconds = Math.Max(15, TomTomTrafficRefreshSeconds);
        userPreference.TomTomRouteRecalcDebounceMs = Math.Clamp(TomTomRouteRecalcDebounceMs, 100, 10000);
        userPreference.TomTomVehicleOnlyMaxSpeedKmh = Math.Clamp(TomTomVehicleOnlyMaxSpeedKmh, 1, 250);
        userPreference.TomTomVehicleWithTrailerMaxSpeedKmh = Math.Clamp(TomTomVehicleWithTrailerMaxSpeedKmh, 1, 250);
        userPreference.TrafficBufferPercentPerThirtyMinutes = Math.Clamp(TrafficBufferPercentFrom0500To0730, 0, 100);
        userPreference.TrafficBufferPercentFrom0500To0730 = Math.Clamp(TrafficBufferPercentFrom0500To0730, 0, 100);
        userPreference.TrafficBufferPercentFrom0730To0900 = Math.Clamp(TrafficBufferPercentFrom0730To0900, 0, 100);
        userPreference.TrafficBufferPercentFrom0900To1530 = Math.Clamp(TrafficBufferPercentFrom0900To1530, 0, 100);
        userPreference.TrafficBufferPercentFrom1530To1830 = Math.Clamp(TrafficBufferPercentFrom1530To1830, 0, 100);
        userPreference.TomTomTrafficSeverityMode = AppSettings.NormalizeTomTomTrafficSeverityMode(TomTomTrafficSeverityMode);
        userPreference.TomTomEnableTileCache = TomTomEnableTileCache;

        model.AppearanceMode = "Light";
        model.CompanyName = string.IsNullOrWhiteSpace(CompanyName) ? "Firma" : CompanyName.Trim();
        model.CompanyStreet = (CompanyStreet ?? string.Empty).Trim();
        model.CompanyPostalCode = (CompanyPostalCode ?? string.Empty).Trim();
        model.CompanyCity = (CompanyCity ?? string.Empty).Trim();
        model.StorageMode = StorageMode;
        model.PostgreSqlStorage = BuildPostgreSqlStorageSettings();
        model.TomTomApiKey = (TomTomApiKey ?? string.Empty).Trim();
        model.TomTomTrafficRefreshSeconds = Math.Max(15, TomTomTrafficRefreshSeconds);
        model.TomTomRouteRecalcDebounceMs = Math.Clamp(TomTomRouteRecalcDebounceMs, 100, 10000);
        model.TomTomVehicleOnlyMaxSpeedKmh = Math.Clamp(TomTomVehicleOnlyMaxSpeedKmh, 1, 250);
        model.TomTomVehicleWithTrailerMaxSpeedKmh = Math.Clamp(TomTomVehicleWithTrailerMaxSpeedKmh, 1, 250);
        model.TrafficBufferPercentPerThirtyMinutes = Math.Clamp(TrafficBufferPercentFrom0500To0730, 0, 100);
        model.TrafficBufferPercentFrom0500To0730 = Math.Clamp(TrafficBufferPercentFrom0500To0730, 0, 100);
        model.TrafficBufferPercentFrom0730To0900 = Math.Clamp(TrafficBufferPercentFrom0730To0900, 0, 100);
        model.TrafficBufferPercentFrom0900To1530 = Math.Clamp(TrafficBufferPercentFrom0900To1530, 0, 100);
        model.TrafficBufferPercentFrom1530To1830 = Math.Clamp(TrafficBufferPercentFrom1530To1830, 0, 100);
        model.TomTomTrafficSeverityMode = AppSettings.NormalizeTomTomTrafficSeverityMode(TomTomTrafficSeverityMode);
        model.CurrentUserName = currentUserName;
        model.MapOverlayPreferencesByUser = new Dictionary<string, MapOverlayUserPreference>(
            model.MapOverlayPreferencesByUser ?? _mapOverlayPreferencesByUser ?? new Dictionary<string, MapOverlayUserPreference>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        model.BackupsEnabled = BackupsEnabled;
        model.BackupDir = (BackupDir ?? string.Empty).Trim();
        model.BackupModeDefault = (BackupModeDefault ?? string.Empty).Trim();
        model.BackupRetentionDays = BackupRetentionDays;
        model.AutoBackupEnabled = AutoBackupEnabled;
        model.AutoBackupIntervalDays = AutoBackupIntervalDays;
        model.LastBackupIso = LastBackupIso;
        model.XmlImportFilePath = (XmlImportFilePath ?? string.Empty).Trim();
        model.XmlImportMapping = BuildXmlImportMapping().WithDefaults();
        model.QuickAccessItems = new List<string>();
        model.SetUserPreference(currentUserName, userPreference);

        return model;
    }

    private void ApplyModel(AppSettings settings)
    {
        var currentUserName = AppSettings.NormalizeUserName(LocalUserSessionService.CurrentUserName);
        var userPreference = settings.ResolveUserPreference(currentUserName);

        AvisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(userPreference.AvisoEmailSubjectTemplate)
            ? AppSettings.DefaultAvisoEmailSubjectTemplate
            : userPreference.AvisoEmailSubjectTemplate;
        CompanyName = string.IsNullOrWhiteSpace(settings.CompanyName) ? "Firma" : settings.CompanyName;
        CompanyStreet = settings.CompanyStreet ?? string.Empty;
        CompanyPostalCode = settings.CompanyPostalCode ?? string.Empty;
        CompanyCity = settings.CompanyCity ?? string.Empty;
        StorageMode = settings.StorageMode;
        PostgreSqlHost = string.IsNullOrWhiteSpace(settings.PostgreSqlStorage?.Host) ? "localhost" : settings.PostgreSqlStorage.Host;
        PostgreSqlPort = settings.PostgreSqlStorage?.Port > 0 ? settings.PostgreSqlStorage.Port : 5432;
        PostgreSqlDatabase = string.IsNullOrWhiteSpace(settings.PostgreSqlStorage?.Database) ? "tourenplaner" : settings.PostgreSqlStorage.Database;
        PostgreSqlSchema = string.IsNullOrWhiteSpace(settings.PostgreSqlStorage?.Schema) ? "app" : settings.PostgreSqlStorage.Schema;
        PostgreSqlUsername = string.IsNullOrWhiteSpace(settings.PostgreSqlStorage?.Username) ? "postgres" : settings.PostgreSqlStorage.Username;
        PostgreSqlPassword = settings.PostgreSqlStorage?.Password ?? string.Empty;
        PostgreSqlUseSsl = settings.PostgreSqlStorage?.UseSsl ?? false;
        PostgreSqlTimeoutSeconds = settings.PostgreSqlStorage?.TimeoutSeconds > 0 ? settings.PostgreSqlStorage.TimeoutSeconds : 10;
        StatusColorNotSpecified = NormalizeHexColor(userPreference.StatusColorNotSpecified, AppSettings.DefaultStatusColorNotSpecified);
        StatusColorOrdered = NormalizeHexColor(userPreference.StatusColorOrdered, AppSettings.DefaultStatusColorOrdered);
        StatusColorOnTheWay = NormalizeHexColor(userPreference.StatusColorOnTheWay, AppSettings.DefaultStatusColorOnTheWay);
        StatusColorPendingPreparation = NormalizeHexColor(userPreference.StatusColorPendingPreparation, AppSettings.DefaultStatusColorPendingPreparation);
        StatusColorInStock = NormalizeHexColor(userPreference.StatusColorInStock, AppSettings.DefaultStatusColorInStock);
        StatusColorPlanned = NormalizeHexColor(userPreference.StatusColorPlanned, AppSettings.DefaultStatusColorPlanned);
        CalendarLoadWarningColor = NormalizeHexColor(userPreference.CalendarLoadWarningColor, AppSettings.DefaultCalendarLoadWarningColor);
        CalendarLoadCriticalColor = NormalizeHexColor(userPreference.CalendarLoadCriticalColor, AppSettings.DefaultCalendarLoadCriticalColor);
        MapUseDistinctPlannedTourColors = userPreference.MapUseDistinctPlannedTourColors;
        CalendarLoadWarningPeopleThreshold = userPreference.CalendarLoadWarningPeopleThreshold < 1 ? 1 : userPreference.CalendarLoadWarningPeopleThreshold;
        CalendarLoadCriticalPeopleThreshold = userPreference.CalendarLoadCriticalPeopleThreshold < 1 ? 2 : userPreference.CalendarLoadCriticalPeopleThreshold;
        MapAutoOpenDetailsOnPinSelection = userPreference.MapAutoOpenDetailsOnPinSelection;
        MapSearchDimNonMatchingPins = userPreference.MapSearchDimNonMatchingPins;
        MapPinInfoCardShowName = userPreference.MapPinInfoCardShowName;
        MapPinInfoCardShowOrderNumber = userPreference.MapPinInfoCardShowOrderNumber;
        MapPinInfoCardShowStreet = userPreference.MapPinInfoCardShowStreet;
        MapPinInfoCardShowPostalCodeCity = userPreference.MapPinInfoCardShowPostalCodeCity;
        MapPinInfoCardShowNotes = userPreference.MapPinInfoCardShowNotes;
        MapPinInfoCardShowProducts = userPreference.MapPinInfoCardShowProducts;
        MapPinInfoCardShowTotalWeight = userPreference.MapPinInfoCardShowTotalWeight;
        PinInfoCardZoomBehaviorStrength = userPreference.PinInfoCardZoomBehaviorStrength is >= 0.2d and <= 4.0d
            ? userPreference.PinInfoCardZoomBehaviorStrength
            : AppSettings.DefaultPinInfoCardZoomBehaviorStrength;
        MapRouteCapacityWarningThresholdPercent = userPreference.MapRouteCapacityWarningThresholdPercent is < 0 or > 100
            ? AppSettings.DefaultMapRouteCapacityWarningThresholdPercent
            : userPreference.MapRouteCapacityWarningThresholdPercent;
        TomTomApiKey = settings.TomTomApiKey ?? string.Empty;
        TomTomTrafficRefreshSeconds = userPreference.TomTomTrafficRefreshSeconds < 15 ? AppSettings.DefaultTomTomTrafficRefreshSeconds : userPreference.TomTomTrafficRefreshSeconds;
        TomTomRouteRecalcDebounceMs = userPreference.TomTomRouteRecalcDebounceMs is < 100 or > 10000 ? AppSettings.DefaultTomTomRouteRecalcDebounceMs : userPreference.TomTomRouteRecalcDebounceMs;
        TomTomVehicleOnlyMaxSpeedKmh = userPreference.TomTomVehicleOnlyMaxSpeedKmh is < 1 or > 250 ? AppSettings.DefaultTomTomVehicleOnlyMaxSpeedKmh : userPreference.TomTomVehicleOnlyMaxSpeedKmh;
        TomTomVehicleWithTrailerMaxSpeedKmh = userPreference.TomTomVehicleWithTrailerMaxSpeedKmh is < 1 or > 250 ? AppSettings.DefaultTomTomVehicleWithTrailerMaxSpeedKmh : userPreference.TomTomVehicleWithTrailerMaxSpeedKmh;
        TrafficBufferPercentFrom0500To0730 = ResolveTrafficBufferPercent(
            userPreference.TrafficBufferPercentFrom0500To0730,
            userPreference.TrafficBufferPercentPerThirtyMinutes,
            AppSettings.DefaultTrafficBufferPercentFrom0500To0730);
        TrafficBufferPercentFrom0730To0900 = ResolveTrafficBufferPercent(
            userPreference.TrafficBufferPercentFrom0730To0900,
            userPreference.TrafficBufferPercentPerThirtyMinutes,
            AppSettings.DefaultTrafficBufferPercentFrom0730To0900);
        TrafficBufferPercentFrom0900To1530 = ResolveTrafficBufferPercent(
            userPreference.TrafficBufferPercentFrom0900To1530,
            userPreference.TrafficBufferPercentPerThirtyMinutes,
            AppSettings.DefaultTrafficBufferPercentFrom0900To1530);
        TrafficBufferPercentFrom1530To1830 = ResolveTrafficBufferPercent(
            userPreference.TrafficBufferPercentFrom1530To1830,
            userPreference.TrafficBufferPercentPerThirtyMinutes,
            AppSettings.DefaultTrafficBufferPercentFrom1530To1830);
        TomTomTrafficSeverityMode = AppSettings.NormalizeTomTomTrafficSeverityMode(userPreference.TomTomTrafficSeverityMode);
        TomTomEnableTileCache = userPreference.TomTomEnableTileCache;
        _currentUserName = currentUserName;
        _mapOverlayPreferencesByUser = new Dictionary<string, MapOverlayUserPreference>(
            settings.MapOverlayPreferencesByUser ?? new Dictionary<string, MapOverlayUserPreference>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        BackupsEnabled = settings.BackupsEnabled;
        BackupDir = settings.BackupDir;
        BackupModeDefault = settings.BackupModeDefault;
        BackupRetentionDays = settings.BackupRetentionDays;
        AutoBackupEnabled = settings.AutoBackupEnabled;
        AutoBackupIntervalDays = settings.AutoBackupIntervalDays;
        LastBackupIso = settings.LastBackupIso;
        ShowGpsTool = userPreference.ShowGpsTool;
        GpsToolUrl = string.IsNullOrWhiteSpace(userPreference.GpsToolUrl) ? AppSettings.DefaultGpsToolUrl : userPreference.GpsToolUrl;
        ShowSpediteurTool = userPreference.ShowSpediteurTool;
        SpediteurToolUrl = string.IsNullOrWhiteSpace(userPreference.SpediteurToolUrl) ? AppSettings.DefaultSpediteurToolUrl : userPreference.SpediteurToolUrl;
        TourDefaultStartTime = NormalizeTourDefaultStartTime(userPreference.TourDefaultStartTime);
        
        XmlImportFilePath = settings.XmlImportFilePath ?? string.Empty;
        ApplyXmlImportMapping(settings.XmlImportMapping);
    }

    private PostgreSqlStorageSettings BuildPostgreSqlStorageSettings()
    {
        return new PostgreSqlStorageSettings
        {
            Host = (PostgreSqlHost ?? string.Empty).Trim(),
            Port = PostgreSqlPort,
            Database = (PostgreSqlDatabase ?? string.Empty).Trim(),
            Schema = string.IsNullOrWhiteSpace(PostgreSqlSchema) ? "app" : PostgreSqlSchema.Trim(),
            Username = (PostgreSqlUsername ?? string.Empty).Trim(),
            Password = PostgreSqlPassword ?? string.Empty,
            UseSsl = PostgreSqlUseSsl,
            TimeoutSeconds = Math.Max(1, PostgreSqlTimeoutSeconds)
        };
    }

    private async Task<string?> VerifyPostgreSqlConnectionAsync()
    {
        var settings = BuildPostgreSqlStorageSettings();
        var connectionFactory = new PostgreSqlConnectionFactory();
        var schemaInitializer = new PostgreSqlSchemaInitializer();

        await using var connection = connectionFactory.CreateConnection(settings);
        await connection.OpenAsync();
        await schemaInitializer.EnsureSchemaAsync(connection, settings);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database();";
        return (await command.ExecuteScalarAsync()) as string;
    }

    private ObservableCollection<XmlImportMappingFieldViewModel> CreateXmlImportStructureFields()
    {
        return
        [
            new XmlImportMappingFieldViewModel("Adress-Datensatz", XmlImportMappingSettings.DefaultAddressRecordElement, XmlImportMappingSettings.DefaultAddressRecordElement),
            new XmlImportMappingFieldViewModel("Auftrags-Datensatz", XmlImportMappingSettings.DefaultOrderRecordElement, XmlImportMappingSettings.DefaultOrderRecordElement),
            new XmlImportMappingFieldViewModel("Produkt-Datensatz", XmlImportMappingSettings.DefaultProductRecordElement, XmlImportMappingSettings.DefaultProductRecordElement)
        ];
    }

    private ObservableCollection<XmlImportMappingFieldViewModel> CreateXmlImportAddressFields()
    {
        return
        [
            new XmlImportMappingFieldViewModel("Adress-ID", XmlImportMappingSettings.DefaultAddressId, XmlImportMappingSettings.DefaultAddressId),
            new XmlImportMappingFieldViewModel("Firma", XmlImportMappingSettings.DefaultAddressCompany, XmlImportMappingSettings.DefaultAddressCompany),
            new XmlImportMappingFieldViewModel("Nachname", XmlImportMappingSettings.DefaultAddressLastName, XmlImportMappingSettings.DefaultAddressLastName),
            new XmlImportMappingFieldViewModel("Vorname", XmlImportMappingSettings.DefaultAddressFirstName, XmlImportMappingSettings.DefaultAddressFirstName),
            new XmlImportMappingFieldViewModel("Strasse", XmlImportMappingSettings.DefaultAddressStreet, XmlImportMappingSettings.DefaultAddressStreet),
            new XmlImportMappingFieldViewModel("PLZ", XmlImportMappingSettings.DefaultAddressPostalCode, XmlImportMappingSettings.DefaultAddressPostalCode),
            new XmlImportMappingFieldViewModel("Ort", XmlImportMappingSettings.DefaultAddressCity, XmlImportMappingSettings.DefaultAddressCity),
            new XmlImportMappingFieldViewModel("Land", XmlImportMappingSettings.DefaultAddressCountry, XmlImportMappingSettings.DefaultAddressCountry),
            new XmlImportMappingFieldViewModel("E-Mail", XmlImportMappingSettings.DefaultAddressEmail, XmlImportMappingSettings.DefaultAddressEmail),
            new XmlImportMappingFieldViewModel("Telefon", XmlImportMappingSettings.DefaultAddressPhone, XmlImportMappingSettings.DefaultAddressPhone),
            new XmlImportMappingFieldViewModel("Kontaktperson", XmlImportMappingSettings.DefaultAddressContactPerson, XmlImportMappingSettings.DefaultAddressContactPerson)
        ];
    }

    private ObservableCollection<XmlImportMappingFieldViewModel> CreateXmlImportOrderFields()
    {
        return
        [
            new XmlImportMappingFieldViewModel("Auftragsnummer", XmlImportMappingSettings.DefaultOrderNumber, XmlImportMappingSettings.DefaultOrderNumber),
            new XmlImportMappingFieldViewModel("Typ", XmlImportMappingSettings.DefaultOrderType, XmlImportMappingSettings.DefaultOrderType),
            new XmlImportMappingFieldViewModel("Auftragsdatum", XmlImportMappingSettings.DefaultOrderDate, XmlImportMappingSettings.DefaultOrderDate),
            new XmlImportMappingFieldViewModel("Kunden-Adress-ID", XmlImportMappingSettings.DefaultOrderAddressId, XmlImportMappingSettings.DefaultOrderAddressId),
            new XmlImportMappingFieldViewModel("Liefer-Adress-ID", XmlImportMappingSettings.DefaultOrderDeliveryAddressId, XmlImportMappingSettings.DefaultOrderDeliveryAddressId),
            new XmlImportMappingFieldViewModel("Lieferbedingung", XmlImportMappingSettings.DefaultOrderDeliveryCondition, XmlImportMappingSettings.DefaultOrderDeliveryCondition),
            new XmlImportMappingFieldViewModel("Lieferdatum", XmlImportMappingSettings.DefaultOrderDeliveryDate, XmlImportMappingSettings.DefaultOrderDeliveryDate),
            new XmlImportMappingFieldViewModel("Archiviert", XmlImportMappingSettings.DefaultOrderArchived, XmlImportMappingSettings.DefaultOrderArchived),
            new XmlImportMappingFieldViewModel("Gesperrt", "(kein Standardwert)", XmlImportMappingSettings.DefaultOrderLocked),
            new XmlImportMappingFieldViewModel("Notiz", XmlImportMappingSettings.DefaultOrderNote, XmlImportMappingSettings.DefaultOrderNote)
        ];
    }

    private ObservableCollection<XmlImportMappingFieldViewModel> CreateXmlImportProductFields()
    {
        return
        [
            new XmlImportMappingFieldViewModel("Produkt-Auftrags-ID", XmlImportMappingSettings.DefaultProductOrderId, XmlImportMappingSettings.DefaultProductOrderId),
            new XmlImportMappingFieldViewModel("Artikelnummer", XmlImportMappingSettings.DefaultProductArticleNumber, XmlImportMappingSettings.DefaultProductArticleNumber),
            new XmlImportMappingFieldViewModel("Bezeichnung", XmlImportMappingSettings.DefaultProductDescription, XmlImportMappingSettings.DefaultProductDescription),
            new XmlImportMappingFieldViewModel("Menge", XmlImportMappingSettings.DefaultProductQuantity, XmlImportMappingSettings.DefaultProductQuantity),
            new XmlImportMappingFieldViewModel("Gewicht", XmlImportMappingSettings.DefaultProductWeight, XmlImportMappingSettings.DefaultProductWeight)
        ];
    }

    private void AttachXmlImportFieldHandlers(IEnumerable<XmlImportMappingFieldViewModel> fields)
    {
        foreach (var field in fields)
        {
            field.PropertyChanged += OnXmlImportFieldPropertyChanged;
        }
    }

    private void OnXmlImportFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(XmlImportMappingFieldViewModel.XmlName))
        {
            return;
        }

        ClearXmlImportPreview(clearStatus: false);

        if (_suppressAutoSave || _autoSaveInProgress)
        {
            return;
        }

        ScheduleAutoSave();
    }

    private XmlImportMappingSettings BuildXmlImportMapping()
    {
        return new XmlImportMappingSettings
        {
            AddressRecordElement = XmlImportStructureFields[0].XmlName,
            OrderRecordElement = XmlImportStructureFields[1].XmlName,
            ProductRecordElement = XmlImportStructureFields[2].XmlName,
            AddressId = XmlImportAddressFields[0].XmlName,
            AddressCompany = XmlImportAddressFields[1].XmlName,
            AddressLastName = XmlImportAddressFields[2].XmlName,
            AddressFirstName = XmlImportAddressFields[3].XmlName,
            AddressStreet = XmlImportAddressFields[4].XmlName,
            AddressHouseNumber = string.Empty,
            AddressPostalCode = XmlImportAddressFields[5].XmlName,
            AddressCity = XmlImportAddressFields[6].XmlName,
            AddressCountry = XmlImportAddressFields[7].XmlName,
            AddressEmail = XmlImportAddressFields[8].XmlName,
            AddressPhone = XmlImportAddressFields[9].XmlName,
            AddressContactPerson = XmlImportAddressFields[10].XmlName,
            OrderId = XmlImportMappingSettings.DefaultOrderId,
            OrderNumber = XmlImportOrderFields[0].XmlName,
            OrderType = XmlImportOrderFields[1].XmlName,
            OrderDate = XmlImportOrderFields[2].XmlName,
            OrderAddressId = XmlImportOrderFields[3].XmlName,
            OrderDeliveryAddressId = XmlImportOrderFields[4].XmlName,
            OrderDeliveryCondition = XmlImportOrderFields[5].XmlName,
            OrderDeliveryDate = XmlImportOrderFields[6].XmlName,
            OrderArchived = XmlImportOrderFields[7].XmlName,
            OrderLocked = XmlImportOrderFields[8].XmlName,
            OrderNote = XmlImportOrderFields[9].XmlName,
            ProductOrderId = XmlImportProductFields[0].XmlName,
            ProductArticleNumber = XmlImportProductFields[1].XmlName,
            ProductDescription = XmlImportProductFields[2].XmlName,
            ProductQuantity = XmlImportProductFields[3].XmlName,
            ProductWeight = XmlImportProductFields[4].XmlName
        };
    }

    private void ApplyXmlImportMapping(XmlImportMappingSettings? mapping)
    {
        var effective = (mapping ?? XmlImportMappingSettings.CreateDefault()).WithDefaults();

        _suppressAutoSave = true;
        try
        {
            XmlImportStructureFields[0].XmlName = effective.AddressRecordElement;
            XmlImportStructureFields[1].XmlName = effective.OrderRecordElement;
            XmlImportStructureFields[2].XmlName = effective.ProductRecordElement;

            XmlImportAddressFields[0].XmlName = effective.AddressId;
            XmlImportAddressFields[1].XmlName = effective.AddressCompany;
            XmlImportAddressFields[2].XmlName = effective.AddressLastName;
            XmlImportAddressFields[3].XmlName = effective.AddressFirstName;
            XmlImportAddressFields[4].XmlName = effective.AddressStreet;
            XmlImportAddressFields[5].XmlName = effective.AddressPostalCode;
            XmlImportAddressFields[6].XmlName = effective.AddressCity;
            XmlImportAddressFields[7].XmlName = effective.AddressCountry;
            XmlImportAddressFields[8].XmlName = effective.AddressEmail;
            XmlImportAddressFields[9].XmlName = effective.AddressPhone;
            XmlImportAddressFields[10].XmlName = effective.AddressContactPerson;

            XmlImportOrderFields[0].XmlName = effective.OrderNumber;
            XmlImportOrderFields[1].XmlName = effective.OrderType;
            XmlImportOrderFields[2].XmlName = effective.OrderDate;
            XmlImportOrderFields[3].XmlName = effective.OrderAddressId;
            XmlImportOrderFields[4].XmlName = effective.OrderDeliveryAddressId;
            XmlImportOrderFields[5].XmlName = effective.OrderDeliveryCondition;
            XmlImportOrderFields[6].XmlName = effective.OrderDeliveryDate;
            XmlImportOrderFields[7].XmlName = effective.OrderArchived;
            XmlImportOrderFields[8].XmlName = effective.OrderLocked;
            XmlImportOrderFields[9].XmlName = effective.OrderNote;

            XmlImportProductFields[0].XmlName = effective.ProductOrderId;
            XmlImportProductFields[1].XmlName = effective.ProductArticleNumber;
            XmlImportProductFields[2].XmlName = effective.ProductDescription;
            XmlImportProductFields[3].XmlName = effective.ProductQuantity;
            XmlImportProductFields[4].XmlName = effective.ProductWeight;
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    private static string NormalizeTourDefaultStartTime(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (TimeOnly.TryParseExact(normalized, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute) &&
            hour is >= 0 and <= 23 &&
            minute is >= 0 and <= 59)
        {
            return $"{hour:00}:{minute:00}";
        }

        return AppSettings.DefaultTourStartTime;
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

    private static string TryFormatPublishedAt(string? publishedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(publishedAtUtc))
        {
            return "-";
        }

        return DateTime.TryParse(publishedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : publishedAtUtc;
    }

    private static string FormatOptionalDateTime(DateTime? value, string fallback)
    {
        return value.HasValue
            ? value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
            : fallback;
    }

    private void UpdateBackupStatus(string? backupDir)
    {
        if (!string.IsNullOrWhiteSpace(backupDir) && Directory.Exists(backupDir))
        {
            var files = Directory.GetFiles(backupDir, "*.bak", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            AvailableBackupsCount = files.Count;
            if (files.FirstOrDefault() is { } latest)
            {
                LatestBackupFile = Path.GetFileName(latest);
                LatestBackupModifiedText = File.GetLastWriteTime(latest).ToString("dd.MM.yyyy HH:mm");
            }
            else
            {
                LatestBackupFile = "n/a";
                LatestBackupModifiedText = "n/a";
            }
            return;
        }

        AvailableBackupsCount = 0;
        LatestBackupFile = "n/a";
        LatestBackupModifiedText = "n/a";
    }

    private void BrowseXmlImportFile()
    {
            var dialog = new OpenFileDialog
        {
            Filter = "XML-Dateien (*.xml)|*.xml|Alle Dateien (*.*)|*.*",
            Title = "XML-Importdatei auswählen",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            XmlImportFilePath = dialog.FileName;
            ImportStatusMessage = $"Ausgewählte XML-Datei: {XmlImportFilePath}";
            ImportOrdersCommand.RaiseCanExecuteChanged();
        }
    }

    private void RequestPinInfoCardZoomBehaviorStrengthSave()
    {
        if (_suppressAutoSave)
        {
            return;
        }

        _pinInfoCardZoomBehaviorSaveCts?.Cancel();
        _pinInfoCardZoomBehaviorSaveCts?.Dispose();
        _pinInfoCardZoomBehaviorSaveCts = new CancellationTokenSource();
        var token = _pinInfoCardZoomBehaviorSaveCts.Token;
        _ = SavePinInfoCardZoomBehaviorStrengthAsync(token);
    }

    private async Task SavePinInfoCardZoomBehaviorStrengthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            var settings = await _repository.LoadAsync(cancellationToken);
            var currentUserName = AppSettings.NormalizeUserName(LocalUserSessionService.CurrentUserName);
            var userPreference = settings.ResolveUserPreference(currentUserName);
            var clamped = Math.Clamp(_pinInfoCardZoomBehaviorStrength, 0.2d, 4.0d);
            if (Math.Abs(userPreference.PinInfoCardZoomBehaviorStrength - clamped) < 0.0001d)
            {
                return;
            }

            userPreference.PinInfoCardZoomBehaviorStrength = clamped;
            settings.SetUserPreference(currentUserName, userPreference);
            await _repository.SaveAsync(settings, cancellationToken);
            _dataSyncService?.PublishSettings(_instanceId);
        }
        catch (OperationCanceledException)
        {
            // Keep only the latest slider value.
        }
        catch
        {
            // Ignore transient write failures; normal save flow can still persist values.
        }
    }

    private void DownloadXmlTemplateFile()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "XML-Dateien (*.xml)|*.xml",
            Title = "XML-Musterdatei speichern",
            FileName = "Auftragsimport-Muster.xml",
            DefaultExt = ".xml",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var xmlService = new XmlOrderImportService();
        File.WriteAllText(dialog.FileName, xmlService.CreateTemplateXml());
        ImportStatusMessage = $"Musterdatei gespeichert: {dialog.FileName}";
    }

    private async Task PreviewXmlImportAsync()
    {
        if (_orderRepository == null)
        {
            ImportStatusMessage = "Fehler: Auftragsrepository ist nicht initialisiert.";
            return;
        }

        IsPreviewingXmlImport = true;
        ImportStatusMessage = "XML-Datei wird geprüft...";

        try
        {
            if (string.IsNullOrWhiteSpace(XmlImportFilePath) || !File.Exists(XmlImportFilePath))
            {
                throw new FileNotFoundException("Bitte zuerst eine gültige XML-Datei auswählen.");
            }

            var fileInfo = new FileInfo(XmlImportFilePath);
            var xmlService = new XmlOrderImportService();
            var loadResult = xmlService.LoadOrdersFromFileDetailed(XmlImportFilePath, BuildXmlImportMapping());
            var importService = new SqlOrderImportService();
            var preview = await importService.PreviewImportAsync(loadResult.Orders, _orderRepository);

            var previewErrors = loadResult.Errors
                .Concat(preview.Errors)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            ApplyXmlImportPreview(loadResult.Orders, preview, previewErrors, fileInfo);

            var invalidCount = previewErrors.Count;
            ImportStatusMessage = BuildXmlImportPreviewStatusMessage(preview, invalidCount);
            StatusText = preview.ValidOrders > 0
                ? "XML Import Vorschau erstellt."
                : "XML Import Vorschau: keine gültigen Aufträge gefunden.";
        }
        catch (Exception ex)
        {
            ClearXmlImportPreview(clearStatus: false);
            ImportStatusMessage = $"Importvorschau fehlgeschlagen: {ex.Message}";
            StatusText = $"XML Import Vorschau fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsPreviewingXmlImport = false;
            RaiseXmlImportCommandStates();
        }
    }

    private async Task ImportOrdersAsync()
    {
        if (_orderRepository == null || _settingsRepository == null)
        {
            ImportStatusMessage = "Fehler: Repositories sind nicht initialisiert.";
            return;
        }

        if (!CanImportOrders())
        {
            ImportStatusMessage = "Bitte zuerst die XML-Datei prüfen. Wenn die Datei geändert wurde, erneut prüfen.";
            return;
        }

        IsImportingOrders = true;
        ImportStatusMessage = "Importiere geprüfte Aufträge aus XML...";

        try
        {
            if (!IsCurrentPreviewFile())
            {
                throw new InvalidOperationException("Die XML-Datei wurde nach der Vorschau geändert. Bitte erneut prüfen.");
            }

            if (_previewedXmlOrders.Count == 0)
            {
                throw new InvalidOperationException("Es liegt keine gültige Importvorschau vor.");
            }

            var importService = new SqlOrderImportService();
            var result = await importService.ImportOrdersAsync(
                _previewedXmlOrders.ToList(),
                _orderRepository,
                _settingsRepository);

            var parserErrorCount = XmlImportPreviewErrors.Count;
            if (result.Errors.Any())
            {
                foreach (var error in result.Errors)
                {
                    if (!XmlImportPreviewErrors.Any(existing => string.Equals(existing, error, StringComparison.Ordinal)))
                    {
                        XmlImportPreviewErrors.Add(error);
                    }
                }

                RaiseXmlImportPreviewStateChanged();
            }

            if (result.CreatedOrders > 0 || result.UpdatedOrders > 0)
            {
                var pinIssues = await EvaluateImportedPinAssignmentsAsync(result);
                ApplyXmlImportPinIssues(pinIssues);
                _dataSyncService?.PublishOrders(_instanceId);
                StartBackgroundPinGeocoding();

                if (pinIssues.Count > 0)
                {
                    AppMessageBox.Show(
                        $"XML-Import abgeschlossen.{Environment.NewLine}{Environment.NewLine}" +
                        $"{pinIssues.Count} importierte Karten-Auftraege konnten nicht exakt zugeordnet werden.{Environment.NewLine}" +
                        "Die betroffenen Auftraege sind unten im Bereich \"Problematische Pin-Zuordnungen\" aufgelistet.",
                        "Pins pruefen",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                ClearXmlImportPinIssues();
            }

            var appSettings = await _settingsRepository.GetAsync();
            appSettings.XmlImportFilePath = XmlImportFilePath;
            appSettings.LastXmlImportDate = DateTime.Now;
            appSettings.XmlImportMapping = BuildXmlImportMapping().WithDefaults();
            await _settingsRepository.SaveAsync(appSettings);

            _hasPendingXmlImportPreview = false;
            RaiseXmlImportPreviewStateChanged();

            var totalErrorCount = parserErrorCount + result.Errors.Count;
            ImportStatusMessage = BuildXmlImportCompletionMessage(result, totalErrorCount, XmlImportPinIssueItems.Count);
            StatusText = $"XML Import abgeschlossen: {result.CreatedOrders} neu, {result.UpdatedOrders} aktualisiert, {result.UnchangedOrders} unverändert.";
        }
        catch (Exception ex)
        {
            ImportStatusMessage = $"Importfehler: {ex.Message}";
            StatusText = $"XML Import fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsImportingOrders = false;
            RaiseXmlImportCommandStates();
        }
    }

    private bool CanImportOrders()
    {
        return !IsXmlImportBusy &&
               _hasPendingXmlImportPreview &&
               _previewedXmlOrders.Count > 0 &&
               IsCurrentPreviewFile();
    }

    private void ApplyXmlImportPreview(
        IReadOnlyList<SqlOrderImportData> previewOrders,
        ImportPreviewResult preview,
        IReadOnlyList<string> previewErrors,
        FileInfo fileInfo)
    {
        _previewedXmlOrders.Clear();
        _previewedXmlOrders.AddRange(previewOrders ?? []);
        _xmlImportPreviewLastWriteUtc = fileInfo.LastWriteTimeUtc;
        _xmlImportPreviewFileLength = fileInfo.Length;
        _hasPendingXmlImportPreview = _previewedXmlOrders.Count > 0;
        _xmlImportPreviewHiddenItemCount = Math.Max(0, preview.Items.Count - MaxXmlImportPreviewItems);
        XmlImportPreviewSummary = BuildXmlImportPreviewSummary(preview, previewErrors.Count);

        XmlImportPreviewItems.Clear();
        foreach (var item in preview.Items.Take(MaxXmlImportPreviewItems))
        {
            XmlImportPreviewItems.Add(XmlImportPreviewListItemViewModel.FromPreviewItem(item));
        }

        XmlImportPreviewErrors.Clear();
        foreach (var error in previewErrors)
        {
            XmlImportPreviewErrors.Add(error);
        }

        RaiseXmlImportPreviewStateChanged();
    }

    private void ClearXmlImportPreview(bool clearStatus)
    {
        _previewedXmlOrders.Clear();
        _xmlImportPreviewLastWriteUtc = DateTime.MinValue;
        _xmlImportPreviewFileLength = 0;
        _xmlImportPreviewHiddenItemCount = 0;
        _hasPendingXmlImportPreview = false;
        XmlImportPreviewSummary = string.Empty;
        XmlImportPreviewItems.Clear();
        XmlImportPreviewErrors.Clear();
        XmlImportPinIssueSummary = string.Empty;
        XmlImportPinIssueItems.Clear();

        if (clearStatus)
        {
            ImportStatusMessage = string.Empty;
        }

        RaiseXmlImportPreviewStateChanged();
    }

    private async Task<IReadOnlyList<XmlImportPinIssueListItemViewModel>> EvaluateImportedPinAssignmentsAsync(ImportResult result)
    {
        if (_orderRepository is null)
        {
            return [];
        }

        var changedOrderIds = result.ChangedOrderIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (changedOrderIds.Count == 0)
        {
            return [];
        }

        var allOrders = (await _orderRepository.GetAllAsync()).ToList();
        var importedMapOrders = allOrders
            .Where(x => x.Type == OrderType.Map &&
                        changedOrderIds.Contains(x.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (importedMapOrders.Count == 0)
        {
            return [];
        }

        var issues = new List<XmlImportPinIssueListItemViewModel>();
        var cacheFilePath = Path.Combine(_dataRoot, "geocode-cache.json");
        var hasLocationUpdates = false;

        foreach (var order in importedMapOrders)
        {
            var geocodingResult = await AddressGeocodingService.TryResolveOrderAsync(order, TomTomApiKey, cacheFilePath);
            if (geocodingResult?.Location != order.Location)
            {
                order.Location = geocodingResult?.Location;
                hasLocationUpdates = true;
            }

            var addressLine = BuildXmlImportPinIssueAddress(order);
            if (geocodingResult is null)
            {
                issues.Add(XmlImportPinIssueListItemViewModel.CreateMissing(
                    (order.Id ?? string.Empty).Trim(),
                    (order.CustomerName ?? string.Empty).Trim(),
                    addressLine));
                continue;
            }

            if (!geocodingResult.IsPrecise)
            {
                issues.Add(XmlImportPinIssueListItemViewModel.CreateApproximate(
                    (order.Id ?? string.Empty).Trim(),
                    (order.CustomerName ?? string.Empty).Trim(),
                    addressLine,
                    (geocodingResult.MatchType ?? string.Empty).Trim(),
                    geocodingResult.EntityType));
            }
        }

        if (hasLocationUpdates)
        {
            await _orderRepository.SaveAllAsync(allOrders);
        }

        return issues;
    }

    private void ApplyXmlImportPinIssues(IReadOnlyList<XmlImportPinIssueListItemViewModel> issues)
    {
        XmlImportPinIssueItems.Clear();
        foreach (var issue in issues)
        {
            XmlImportPinIssueItems.Add(issue);
        }

        XmlImportPinIssueSummary = issues.Count == 0
            ? string.Empty
            : $"{issues.Count} importierte Karten-Auftraege muessen bei der Pin-Zuordnung manuell geprueft werden.";
        RaiseXmlImportPreviewStateChanged();
    }

    private void ClearXmlImportPinIssues()
    {
        XmlImportPinIssueSummary = string.Empty;
        XmlImportPinIssueItems.Clear();
        RaiseXmlImportPreviewStateChanged();
    }

    private void RaiseXmlImportPreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasXmlImportPreview));
        OnPropertyChanged(nameof(HasXmlImportPreviewItems));
        OnPropertyChanged(nameof(HasXmlImportPreviewErrors));
        OnPropertyChanged(nameof(HasXmlImportPinIssues));
        OnPropertyChanged(nameof(HasXmlImportPinIssueSummary));
        OnPropertyChanged(nameof(HasXmlImportPreviewHiddenItems));
        OnPropertyChanged(nameof(XmlImportPreviewHiddenItemsText));
        RaiseXmlImportCommandStates();
    }

    private void RaiseXmlImportCommandStates()
    {
        PreviewXmlImportCommand.RaiseCanExecuteChanged();
        ImportOrdersCommand.RaiseCanExecuteChanged();
    }

    private bool IsCurrentPreviewFile()
    {
        if (string.IsNullOrWhiteSpace(XmlImportFilePath) || !File.Exists(XmlImportFilePath))
        {
            return false;
        }

        if (_xmlImportPreviewLastWriteUtc == DateTime.MinValue)
        {
            return false;
        }

        var fileInfo = new FileInfo(XmlImportFilePath);
        return fileInfo.Length == _xmlImportPreviewFileLength &&
               fileInfo.LastWriteTimeUtc == _xmlImportPreviewLastWriteUtc;
    }

    private static string BuildXmlImportPreviewSummary(ImportPreviewResult preview, int invalidCount)
    {
        return $"{preview.ValidOrders} gültige Aufträge geprüft | " +
               $"{preview.CreatedOrders} neu | " +
               $"{preview.UpdatedOrders} mit Änderungen | " +
               $"{preview.UnchangedOrders} unverändert | " +
               $"{invalidCount} fehlerhaft";
    }

    private static string BuildXmlImportPreviewStatusMessage(ImportPreviewResult preview, int invalidCount)
    {
        if (preview.ValidOrders == 0)
        {
            return invalidCount > 0
                ? $"Keine gültigen Aufträge gefunden. {invalidCount} Eintrag/Einträge enthalten Fehler."
                : "Keine importierbaren Aufträge gefunden.";
        }

        var message = $"Vorschau erstellt: {preview.CreatedOrders} neue, {preview.UpdatedOrders} geänderte und {preview.UnchangedOrders} unveränderte Aufträge.";
        if (invalidCount > 0)
        {
            message += $" {invalidCount} Eintrag/Einträge werden wegen Fehlern übersprungen.";
        }

        return message;
    }

    private static string BuildXmlImportCompletionMessage(ImportResult result, int errorCount, int pinIssueCount)
    {
        var message = $"Import abgeschlossen: {result.CreatedOrders} neu, {result.UpdatedOrders} aktualisiert, {result.UnchangedOrders} unverändert.";
        if (result.CreatedOrders > 0 || result.UpdatedOrders > 0)
        {
            message += " Pins ohne Koordinaten werden im Hintergrund weiter geprüft.";
        }

        if (pinIssueCount > 0)
        {
            message += $" {pinIssueCount} importierte Karten-Auftraege sollten wegen unklarer Pin-Zuordnung manuell korrigiert werden.";
        }

        if (errorCount > 0)
        {
            message += $" {errorCount} Eintrag/Eintraege wurden wegen Fehlern nicht importiert.";
        }

        return message;
    }

    private static string BuildXmlImportPinIssueAddress(Order order)
    {
        var street = string.Join(" ", new[]
        {
            (order.DeliveryAddress?.Street ?? string.Empty).Trim(),
            (order.DeliveryAddress?.HouseNumber ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var postalCity = string.Join(" ", new[]
        {
            (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim(),
            (order.DeliveryAddress?.City ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var addressLine = string.Join(", ", new[] { street, postalCity }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(addressLine))
        {
            return addressLine;
        }

        return (order.Address ?? string.Empty).Trim();
    }

    private DelegateCommand CreateColorPickerCommand(
        string title,
        string description,
        Func<string> getCurrentValue,
        string fallbackColor,
        Action<string> applyColor)
    {
        return new DelegateCommand(() =>
            OpenColorPicker(title, description, getCurrentValue(), fallbackColor, applyColor));
    }

    private void OpenColorPicker(
        string title,
        string description,
        string currentColor,
        string fallbackColor,
        Action<string> applyColor)
    {
        var dialog = new ColorPickerDialogWindow(
            title,
            description,
            currentColor,
            fallbackColor)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            applyColor(dialog.SelectedColorHex);
        }
    }

    private bool SetColorProperty(
        ref string field,
        string? value,
        string fallback,
        [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, NormalizeHexColor(value, fallback), propertyName);
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 7 &&
            normalized.StartsWith('#') &&
            normalized.Skip(1).All(Uri.IsHexDigit))
        {
            return normalized.ToUpperInvariant();
        }

        return fallback;
    }

    private async Task<int> GeocodeMapOrdersAfterSqlImportAsync()
    {
        if (_orderRepository is null)
        {
            return 0;
        }

        var allOrders = (await _orderRepository.GetAllAsync()).ToList();
        var geocoded = 0;

        foreach (var order in allOrders.Where(x => x.Type == OrderType.Map))
        {
            var needsGeocoding = order.Location is null || AddressGeocodingService.IsLikelyCountryCentroid(order.Location);
            if (!needsGeocoding)
            {
                continue;
            }

            var location = await AddressGeocodingService.TryGeocodeOrderAsync(
                order,
                TomTomApiKey,
                Path.Combine(_dataRoot, "geocode-cache.json"));
            if (location is null)
            {
                continue;
            }

            order.Location = location;
            geocoded++;
            await _orderRepository.SaveAllAsync(allOrders);
            _dataSyncService?.PublishOrders(_instanceId, order.Id, order.Id);
        }

        return geocoded;
    }

    private void StartBackgroundPinGeocoding()
    {
        if (_isBackgroundGeocodingRunning)
        {
            return;
        }

        _isBackgroundGeocodingRunning = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var geocoded = await GeocodeMapOrdersAfterSqlImportAsync();
                if (geocoded > 0)
                {
                    _dataSyncService?.PublishOrders(_instanceId);
                }
            }
            finally
            {
                _isBackgroundGeocodingRunning = false;
            }
        });
    }

    public sealed record StorageModeOption(AppStorageMode Value, string DisplayName);
    public sealed record TomTomTrafficSeverityOption(string Key, string DisplayName);

}
