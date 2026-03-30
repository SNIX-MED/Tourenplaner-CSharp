using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;
using System.Reflection;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class SettingsSectionViewModel : SectionViewModelBase
{
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly JsonAppSettingsRepository _repository;
    private readonly SettingsValidator _validator;
    private readonly BackupManager _backupManager;
    private readonly GitHubReleaseUpdateService _updateService;
    private readonly IOrderRepository? _orderRepository;
    private readonly ISettingsRepository? _settingsRepository;
    private readonly AppDataSyncService? _dataSyncService;
    private readonly string _dataRoot;
    private bool _isBackgroundGeocodingRunning;

    private string _statusText = "Loading settings...";
    
    // SQL Import Settings
    private string _sqlServer = SqlConnectionSettings.DefaultServer;
    private string _sqlDatabase = "Business11";
    private string _sqlDatabasePath = string.Empty;
    private bool _sqlUseWindowsAuth = true;
    private string _sqlUserId = string.Empty;
    private string _sqlPassword = string.Empty;
    private bool _sqlImportEnabled = false;
    private bool _isTestingConnection = false;
    private bool _isImportingOrders = false;
    private string _importStatusMessage = "";
    
    private string _avisoEmailSubjectTemplate = AppSettings.DefaultAvisoEmailSubjectTemplate;
    private string _companyName = "Firma";
    private string _companyStreet = string.Empty;
    private string _companyPostalCode = string.Empty;
    private string _companyCity = string.Empty;
    private string _statusColorNotSpecified = AppSettings.DefaultStatusColorNotSpecified;
    private string _statusColorOrdered = AppSettings.DefaultStatusColorOrdered;
    private string _statusColorOnTheWay = AppSettings.DefaultStatusColorOnTheWay;
    private string _statusColorInStock = AppSettings.DefaultStatusColorInStock;
    private string _statusColorPlanned = AppSettings.DefaultStatusColorPlanned;
    private string _calendarLoadWarningColor = AppSettings.DefaultCalendarLoadWarningColor;
    private string _calendarLoadCriticalColor = AppSettings.DefaultCalendarLoadCriticalColor;
    private int _calendarLoadWarningPeopleThreshold = 1;
    private int _calendarLoadCriticalPeopleThreshold = 2;
    private bool _mapDetailsPanelExpanded = true;
    private bool _backupsEnabled;
    private string _backupDir = string.Empty;
    private string _backupModeDefault = "full";
    private int _backupRetentionDays = 30;
    private bool _autoBackupEnabled;
    private int _autoBackupIntervalDays = 7;
    private string _lastBackupIso = string.Empty;
    private string _applicationVersion = string.Empty;
    private string _runtimeVersion = string.Empty;
    private string _latestBackupFile = "n/a";
    private int _availableBackupsCount;
    private string _updateFeedUrl = AppSettings.DefaultUpdateFeedUrl;
    private bool _showGpsTool = true;
    private string _gpsToolUrl = AppSettings.DefaultGpsToolUrl;
    private bool _showSpediteurTool = true;
    private string _spediteurToolUrl = AppSettings.DefaultSpediteurToolUrl;
    private string _latestReleaseVersion = "Noch nicht geprüft";
    private string _latestReleasePublishedAt = "n/a";
    private string _updateCheckResult = "Noch nicht geprüft.";
    private string _latestReleaseLink = string.Empty;
    private string _latestReleaseAsset = "n/a";
    private GitHubReleaseInfo? _latestRelease;
    private string _validationSummary = string.Empty;

    public SettingsSectionViewModel(
        string settingsJsonPath,
        string dataRoot,
        IOrderRepository? orderRepository = null,
        ISettingsRepository? settingsRepository = null,
        AppDataSyncService? dataSyncService = null)
        : base("Settings", "Appearance, backup policy and restore operations.")
    {
        _repository = new JsonAppSettingsRepository(settingsJsonPath);
        _validator = new SettingsValidator();
        _backupManager = new BackupManager();
        _updateService = new GitHubReleaseUpdateService();
        _orderRepository = orderRepository;
        _settingsRepository = settingsRepository;
        _dataSyncService = dataSyncService;
        _dataRoot = dataRoot;

        BackupModes =
        [
            "full",
            "incremental"
        ];

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync);
        ValidateCommand = new DelegateCommand(ValidateCurrentSettings);
        ResetStatusColorsCommand = new DelegateCommand(ResetStatusColors);
        CreateBackupCommand = new AsyncCommand(CreateBackupAsync);
        RestoreLatestBackupCommand = new AsyncCommand(RestoreLatestBackupAsync);
        CleanupBackupsCommand = new DelegateCommand(CleanupBackups);
        OpenUpdateFeedCommand = new DelegateCommand(OpenUpdateFeed);
        CheckForUpdatesCommand = new AsyncCommand(CheckForUpdatesAsync);
        DownloadLatestUpdateCommand = new AsyncCommand(DownloadLatestUpdateAsync);
        
        // SQL Import Commands
        TestSqlConnectionCommand = new AsyncCommand(
            TestConnectionAsync,
            canExecute: () => !IsTestingConnection && !IsImportingOrders);
        ImportOrdersCommand = new AsyncCommand(
            ImportOrdersAsync,
            canExecute: () => SqlImportEnabled && !IsTestingConnection && !IsImportingOrders);

        _ = RefreshAsync();
    }

    public ObservableCollection<string> BackupModes { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ValidateCommand { get; }

    public ICommand ResetStatusColorsCommand { get; }

    public ICommand CreateBackupCommand { get; }

    public ICommand RestoreLatestBackupCommand { get; }

    public ICommand CleanupBackupsCommand { get; }

    public ICommand OpenUpdateFeedCommand { get; }

    public ICommand CheckForUpdatesCommand { get; }

    public ICommand DownloadLatestUpdateCommand { get; }
    
    public AsyncCommand TestSqlConnectionCommand { get; }

    public AsyncCommand ImportOrdersCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

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

    public string StatusColorNotSpecified
    {
        get => _statusColorNotSpecified;
        set => SetProperty(ref _statusColorNotSpecified, value);
    }

    public string StatusColorOrdered
    {
        get => _statusColorOrdered;
        set => SetProperty(ref _statusColorOrdered, value);
    }

    public string StatusColorOnTheWay
    {
        get => _statusColorOnTheWay;
        set => SetProperty(ref _statusColorOnTheWay, value);
    }

    public string StatusColorInStock
    {
        get => _statusColorInStock;
        set => SetProperty(ref _statusColorInStock, value);
    }

    public string StatusColorPlanned
    {
        get => _statusColorPlanned;
        set => SetProperty(ref _statusColorPlanned, value);
    }

    public string CalendarLoadWarningColor
    {
        get => _calendarLoadWarningColor;
        set => SetProperty(ref _calendarLoadWarningColor, value);
    }

    public string CalendarLoadCriticalColor
    {
        get => _calendarLoadCriticalColor;
        set => SetProperty(ref _calendarLoadCriticalColor, value);
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

    public bool MapDetailsPanelExpanded
    {
        get => _mapDetailsPanelExpanded;
        set => SetProperty(ref _mapDetailsPanelExpanded, value);
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

    public string LatestBackupFile
    {
        get => _latestBackupFile;
        private set => SetProperty(ref _latestBackupFile, value);
    }

    public int AvailableBackupsCount
    {
        get => _availableBackupsCount;
        private set => SetProperty(ref _availableBackupsCount, value);
    }

    public string UpdateFeedUrl
    {
        get => _updateFeedUrl;
        set => SetProperty(ref _updateFeedUrl, value);
    }

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

    public string LatestReleaseVersion
    {
        get => _latestReleaseVersion;
        private set => SetProperty(ref _latestReleaseVersion, value);
    }

    public string LatestReleasePublishedAt
    {
        get => _latestReleasePublishedAt;
        private set => SetProperty(ref _latestReleasePublishedAt, value);
    }

    public string UpdateCheckResult
    {
        get => _updateCheckResult;
        private set => SetProperty(ref _updateCheckResult, value);
    }

    public string LatestReleaseAsset
    {
        get => _latestReleaseAsset;
        private set => SetProperty(ref _latestReleaseAsset, value);
    }

    public string SqlDatabasePath
    {
        get => _sqlDatabasePath;
        set => SetProperty(ref _sqlDatabasePath, value);
    }

    public string SqlServer
    {
        get => _sqlServer;
        set => SetProperty(ref _sqlServer, value);
    }

    public string SqlDatabase
    {
        get => _sqlDatabase;
        set => SetProperty(ref _sqlDatabase, value);
    }

    public bool SqlUseWindowsAuth
    {
        get => _sqlUseWindowsAuth;
        set => SetProperty(ref _sqlUseWindowsAuth, value);
    }

    public string SqlUserId
    {
        get => _sqlUserId;
        set => SetProperty(ref _sqlUserId, value);
    }

    public string SqlPassword
    {
        get => _sqlPassword;
        set => SetProperty(ref _sqlPassword, value);
    }

    public bool SqlImportEnabled
    {
        get => _sqlImportEnabled;
        set => SetProperty(ref _sqlImportEnabled, value);
    }

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        set => SetProperty(ref _isTestingConnection, value);
    }

    public bool IsImportingOrders
    {
        get => _isImportingOrders;
        set => SetProperty(ref _isImportingOrders, value);
    }

    public string ImportStatusMessage
    {
        get => _importStatusMessage;
        set => SetProperty(ref _importStatusMessage, value);
    }

    public async Task RefreshAsync()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        ApplicationVersion = entryAssembly.GetName().Version?.ToString() ?? "0.0.0";
        RuntimeVersion = Environment.Version.ToString();

        var settings = await _repository.LoadAsync();
        ApplyModel(settings);
        UpdateBackupStatus(settings.BackupDir);
        ResetUpdateState();
        ValidationSummary = string.Empty;
        StatusText = "Settings loaded.";
    }

    public async Task SaveAsync()
    {
        var model = BuildModel();
        var validation = _validator.Validate(model);
        if (!validation.IsValid)
        {
            ValidationSummary = string.Join(Environment.NewLine, validation.Errors);
            StatusText = "Validation failed. Settings were not saved.";
            return;
        }

        await _repository.SaveAsync(model);
        ValidationSummary = string.Empty;
        UpdateBackupStatus(model.BackupDir);
        StatusText = "Settings saved.";
        ToastNotificationService.ShowInfo("Einstellungen gespeichert.");
    }

    public async Task CreateBackupAsync()
    {
        var model = BuildModel();
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
        var model = BuildModel();
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
        StatusColorInStock = AppSettings.DefaultStatusColorInStock;
        StatusColorPlanned = AppSettings.DefaultStatusColorPlanned;
        StatusText = "Statusfarben auf Standard zurückgesetzt.";
    }

    private void OpenUpdateFeed()
    {
        var target = !string.IsNullOrWhiteSpace(_latestReleaseLink)
            ? _latestReleaseLink
            : (UpdateFeedUrl ?? string.Empty).Trim();

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            StatusText = "Ungültige Update-URL.";
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        StatusText = "Update-Seite im Browser geöffnet.";
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var release = await _updateService.GetLatestReleaseAsync(UpdateFeedUrl);
            _latestRelease = release;
            _latestReleaseLink = release.HtmlUrl;
            LatestReleaseVersion = string.IsNullOrWhiteSpace(release.ReleaseName)
                ? release.TagName
                : $"{release.ReleaseName} ({release.TagName})";
            LatestReleasePublishedAt = release.PublishedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "n/a";
            LatestReleaseAsset = _updateService.SelectPreferredAsset(release)?.Name ?? "Kein Asset im Release";

            var currentVersion = GitHubReleaseUpdateService.TryParseVersion(ApplicationVersion);
            var releaseVersion = GitHubReleaseUpdateService.TryParseVersion(release.TagName);
            var updateAvailable = releaseVersion is not null && currentVersion is not null
                ? releaseVersion > currentVersion
                : !string.Equals(release.TagName.Trim(), ApplicationVersion.Trim(), StringComparison.OrdinalIgnoreCase);

            UpdateCheckResult = updateAvailable
                ? "Neue Version verfügbar."
                : "Die installierte Version ist aktuell.";

            if (release.IsPrerelease)
            {
                UpdateCheckResult += " GitHub markiert dieses Release als Vorabversion.";
            }

            StatusText = updateAvailable
                ? $"Update gefunden: {release.TagName}"
                : "Keine neuere GitHub-Version gefunden.";
        }
        catch (Exception ex)
        {
            _latestRelease = null;
            _latestReleaseLink = string.Empty;
            LatestReleaseVersion = "Prüfung fehlgeschlagen";
            LatestReleasePublishedAt = "n/a";
            LatestReleaseAsset = "n/a";
            UpdateCheckResult = "Release-Prüfung fehlgeschlagen.";
            StatusText = $"Update-Prüfung fehlgeschlagen: {ex.Message}";
        }
    }

    private async Task DownloadLatestUpdateAsync()
    {
        try
        {
            if (_latestRelease is null)
            {
                await CheckForUpdatesAsync();
            }

            if (_latestRelease is null)
            {
                StatusText = "Es ist noch kein Release geladen.";
                return;
            }

            var asset = _updateService.SelectPreferredAsset(_latestRelease);
            if (asset is null)
            {
                OpenUpdateFeed();
                StatusText = "Kein Download-Asset gefunden. Die Release-Seite wurde geöffnet.";
                return;
            }

            var downloadsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");

            var targetPath = await _updateService.DownloadAssetAsync(asset, downloadsDirectory);
            LatestReleaseAsset = asset.Name;

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{targetPath}\"")
            {
                UseShellExecute = true
            });

            StatusText = $"Update heruntergeladen: {Path.GetFileName(targetPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Update-Download fehlgeschlagen: {ex.Message}";
        }
    }

    private AppSettings BuildModel()
    {
        return new AppSettings
        {
            AppearanceMode = "Light",
            AvisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(AvisoEmailSubjectTemplate)
                ? AppSettings.DefaultAvisoEmailSubjectTemplate
                : AvisoEmailSubjectTemplate.Trim(),
            CompanyName = string.IsNullOrWhiteSpace(CompanyName) ? "Firma" : CompanyName.Trim(),
            CompanyStreet = (CompanyStreet ?? string.Empty).Trim(),
            CompanyPostalCode = (CompanyPostalCode ?? string.Empty).Trim(),
            CompanyCity = (CompanyCity ?? string.Empty).Trim(),
            StatusColorNotSpecified = (StatusColorNotSpecified ?? string.Empty).Trim(),
            StatusColorOrdered = (StatusColorOrdered ?? string.Empty).Trim(),
            StatusColorOnTheWay = (StatusColorOnTheWay ?? string.Empty).Trim(),
            StatusColorInStock = (StatusColorInStock ?? string.Empty).Trim(),
            StatusColorPlanned = (StatusColorPlanned ?? string.Empty).Trim(),
            CalendarLoadWarningColor = (CalendarLoadWarningColor ?? string.Empty).Trim(),
            CalendarLoadCriticalColor = (CalendarLoadCriticalColor ?? string.Empty).Trim(),
            CalendarLoadWarningPeopleThreshold = CalendarLoadWarningPeopleThreshold,
            CalendarLoadCriticalPeopleThreshold = CalendarLoadCriticalPeopleThreshold,
            MapDetailsPanelExpanded = MapDetailsPanelExpanded,
            BackupsEnabled = BackupsEnabled,
            BackupDir = (BackupDir ?? string.Empty).Trim(),
            BackupModeDefault = (BackupModeDefault ?? string.Empty).Trim(),
            BackupRetentionDays = BackupRetentionDays,
            AutoBackupEnabled = AutoBackupEnabled,
            AutoBackupIntervalDays = AutoBackupIntervalDays,
            LastBackupIso = LastBackupIso,
            UpdateFeedUrl = string.IsNullOrWhiteSpace(UpdateFeedUrl) ? AppSettings.DefaultUpdateFeedUrl : UpdateFeedUrl.Trim(),
            ShowGpsTool = ShowGpsTool,
            GpsToolUrl = string.IsNullOrWhiteSpace(GpsToolUrl) ? AppSettings.DefaultGpsToolUrl : GpsToolUrl.Trim(),
            ShowSpediteurTool = ShowSpediteurTool,
            SpediteurToolUrl = string.IsNullOrWhiteSpace(SpediteurToolUrl) ? AppSettings.DefaultSpediteurToolUrl : SpediteurToolUrl.Trim(),
            SqlImportSettings = new SqlConnectionSettings
            {
                Server = string.IsNullOrWhiteSpace(SqlServer) ? SqlConnectionSettings.DefaultServer : SqlServer.Trim(),
                Database = string.IsNullOrWhiteSpace(SqlDatabase) ? "Business11" : SqlDatabase.Trim(),
                DatabasePath = SqlDatabasePath,
                UseWindowsAuthentication = SqlUseWindowsAuth,
                UserId = (SqlUserId ?? string.Empty).Trim(),
                Password = SqlPassword ?? string.Empty
            },
            SqlImportEnabled = SqlImportEnabled,
            QuickAccessItems = new List<string>()
        };
    }

    private void ApplyModel(AppSettings settings)
    {
        AvisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(settings.AvisoEmailSubjectTemplate)
            ? AppSettings.DefaultAvisoEmailSubjectTemplate
            : settings.AvisoEmailSubjectTemplate;
        CompanyName = string.IsNullOrWhiteSpace(settings.CompanyName) ? "Firma" : settings.CompanyName;
        CompanyStreet = settings.CompanyStreet ?? string.Empty;
        CompanyPostalCode = settings.CompanyPostalCode ?? string.Empty;
        CompanyCity = settings.CompanyCity ?? string.Empty;
        StatusColorNotSpecified = string.IsNullOrWhiteSpace(settings.StatusColorNotSpecified) ? AppSettings.DefaultStatusColorNotSpecified : settings.StatusColorNotSpecified;
        StatusColorOrdered = string.IsNullOrWhiteSpace(settings.StatusColorOrdered) ? AppSettings.DefaultStatusColorOrdered : settings.StatusColorOrdered;
        StatusColorOnTheWay = string.IsNullOrWhiteSpace(settings.StatusColorOnTheWay) ? AppSettings.DefaultStatusColorOnTheWay : settings.StatusColorOnTheWay;
        StatusColorInStock = string.IsNullOrWhiteSpace(settings.StatusColorInStock) ? AppSettings.DefaultStatusColorInStock : settings.StatusColorInStock;
        StatusColorPlanned = string.IsNullOrWhiteSpace(settings.StatusColorPlanned) ? AppSettings.DefaultStatusColorPlanned : settings.StatusColorPlanned;
        CalendarLoadWarningColor = string.IsNullOrWhiteSpace(settings.CalendarLoadWarningColor) ? AppSettings.DefaultCalendarLoadWarningColor : settings.CalendarLoadWarningColor;
        CalendarLoadCriticalColor = string.IsNullOrWhiteSpace(settings.CalendarLoadCriticalColor) ? AppSettings.DefaultCalendarLoadCriticalColor : settings.CalendarLoadCriticalColor;
        CalendarLoadWarningPeopleThreshold = settings.CalendarLoadWarningPeopleThreshold < 1 ? 1 : settings.CalendarLoadWarningPeopleThreshold;
        CalendarLoadCriticalPeopleThreshold = settings.CalendarLoadCriticalPeopleThreshold < 1 ? 2 : settings.CalendarLoadCriticalPeopleThreshold;
        MapDetailsPanelExpanded = settings.MapDetailsPanelExpanded;
        BackupsEnabled = settings.BackupsEnabled;
        BackupDir = settings.BackupDir;
        BackupModeDefault = settings.BackupModeDefault;
        BackupRetentionDays = settings.BackupRetentionDays;
        AutoBackupEnabled = settings.AutoBackupEnabled;
        AutoBackupIntervalDays = settings.AutoBackupIntervalDays;
        LastBackupIso = settings.LastBackupIso;
        UpdateFeedUrl = string.IsNullOrWhiteSpace(settings.UpdateFeedUrl) ? AppSettings.DefaultUpdateFeedUrl : settings.UpdateFeedUrl;
        ShowGpsTool = settings.ShowGpsTool;
        GpsToolUrl = string.IsNullOrWhiteSpace(settings.GpsToolUrl) ? AppSettings.DefaultGpsToolUrl : settings.GpsToolUrl;
        ShowSpediteurTool = settings.ShowSpediteurTool;
        SpediteurToolUrl = string.IsNullOrWhiteSpace(settings.SpediteurToolUrl) ? AppSettings.DefaultSpediteurToolUrl : settings.SpediteurToolUrl;
        
        // SQL Settings
        SqlServer = string.IsNullOrWhiteSpace(settings.SqlImportSettings?.Server)
            ? SqlConnectionSettings.DefaultServer
            : settings.SqlImportSettings.Server;
        SqlDatabase = string.IsNullOrWhiteSpace(settings.SqlImportSettings?.Database)
            ? "Business11"
            : settings.SqlImportSettings.Database;
        SqlDatabasePath = string.IsNullOrWhiteSpace(settings.SqlImportSettings?.DatabasePath)
            ? string.Empty
            : settings.SqlImportSettings.DatabasePath;
        SqlUseWindowsAuth = settings.SqlImportSettings?.UseWindowsAuthentication ?? true;
        SqlUserId = settings.SqlImportSettings?.UserId ?? string.Empty;
        SqlPassword = settings.SqlImportSettings?.Password ?? string.Empty;
        SqlImportEnabled = settings.SqlImportEnabled;
    }

    private void UpdateBackupStatus(string? backupDir)
    {
        if (!string.IsNullOrWhiteSpace(backupDir) && Directory.Exists(backupDir))
        {
            var files = Directory.GetFiles(backupDir, "*.bak", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            AvailableBackupsCount = files.Count;
            LatestBackupFile = files.FirstOrDefault() is { } latest ? Path.GetFileName(latest) : "n/a";
            return;
        }

        AvailableBackupsCount = 0;
        LatestBackupFile = "n/a";
    }

    private void ResetUpdateState()
    {
        _latestRelease = null;
        _latestReleaseLink = string.Empty;
        LatestReleaseVersion = "Noch nicht geprüft";
        LatestReleasePublishedAt = "n/a";
        LatestReleaseAsset = "n/a";
        UpdateCheckResult = "Noch nicht geprüft.";
    }

    private async Task TestConnectionAsync()
    {
        IsTestingConnection = true;
        ImportStatusMessage = "🔌 Verbindung wird getestet...";

        try
        {
            var settings = new SqlConnectionSettings
            {
                Server = string.IsNullOrWhiteSpace(SqlServer) ? SqlConnectionSettings.DefaultServer : SqlServer.Trim(),
                Database = string.IsNullOrWhiteSpace(SqlDatabase) ? "Business11" : SqlDatabase.Trim(),
                DatabasePath = SqlDatabasePath,
                UseWindowsAuthentication = SqlUseWindowsAuth,
                UserId = (SqlUserId ?? string.Empty).Trim(),
                Password = SqlPassword ?? string.Empty
            };

            var service = new SqlServerOrderService(settings);
            await service.TestConnectionAsync();

            ImportStatusMessage = "✓ Verbindung erfolgreich!";
            SqlImportEnabled = true;
            StatusText = "SQL Server Verbindung erfolgreich getestet.";
            
            // Aktualisiere Button-Status
            ImportOrdersCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            var hint = BuildSqlConnectionHint();
            ImportStatusMessage = string.IsNullOrWhiteSpace(hint)
                ? $"✗ Fehler: {ex.Message}"
                : $"✗ Fehler: {ex.Message}{Environment.NewLine}{hint}";
            SqlImportEnabled = false;
            StatusText = "SQL Server Verbindung fehlgeschlagen.";
        }
        finally
        {
            IsTestingConnection = false;
            TestSqlConnectionCommand.RaiseCanExecuteChanged();
            ImportOrdersCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task ImportOrdersAsync()
    {
        if (_orderRepository == null || _settingsRepository == null)
        {
            ImportStatusMessage = "✗ Fehler: Repositories nicht initialisiert.";
            return;
        }

        IsImportingOrders = true;
        ImportStatusMessage = "⏳ Importiere Aufträge aus SQL Server...";

        try
        {
            var appSettings = await _settingsRepository.GetAsync();
            appSettings.SqlImportSettings = new SqlConnectionSettings
            {
                Server = string.IsNullOrWhiteSpace(SqlServer) ? SqlConnectionSettings.DefaultServer : SqlServer.Trim(),
                Database = string.IsNullOrWhiteSpace(SqlDatabase) ? "Business11" : SqlDatabase.Trim(),
                DatabasePath = SqlDatabasePath,
                UseWindowsAuthentication = SqlUseWindowsAuth,
                UserId = (SqlUserId ?? string.Empty).Trim(),
                Password = SqlPassword ?? string.Empty
            };

            var sqlService = new SqlServerOrderService(appSettings.SqlImportSettings);
            // Always run a full sync here so delivery-type rule changes (ArticleID mapping)
            // are re-applied to existing orders as well.
            var sqlOrders = await sqlService.GetNewAndUpdatedOrdersAsync(null);

            if (sqlOrders.Count == 0)
            {
                ImportStatusMessage = "ℹ Keine importierbaren Aufträge gefunden.";
                StatusText = "SQL Import: Keine Daten gefunden.";
                return;
            }

            var importService = new SqlOrderImportService();
            var result = await importService.ImportOrdersAsync(
                sqlOrders,
                _orderRepository,
                _settingsRepository);
            _dataSyncService?.PublishOrders(_instanceId);
            StartBackgroundPinGeocoding();

            var message = $"✓ Import abgeschlossen:\n" +
                          $"  • Neue Aufträge: {result.CreatedOrders}\n" +
                          $"  • Aktualisiert: {result.UpdatedOrders}\n" +
                          $"  • Pin-Erstellung: läuft im Hintergrund";

            if (result.Errors.Any())
            {
                message += $"\n\n⚠ Fehler bei {result.Errors.Count} Aufträgen";
            }

            ImportStatusMessage = message;
            StatusText = $"SQL Import erfolgreich: {result.CreatedOrders} neu, {result.UpdatedOrders} aktualisiert.";
        }
        catch (Exception ex)
        {
            var hint = BuildSqlConnectionHint();
            ImportStatusMessage = string.IsNullOrWhiteSpace(hint)
                ? $"✗ Importfehler: {ex.Message}"
                : $"✗ Importfehler: {ex.Message}{Environment.NewLine}{hint}";
            StatusText = $"SQL Import fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsImportingOrders = false;
            TestSqlConnectionCommand.RaiseCanExecuteChanged();
            ImportOrdersCommand.RaiseCanExecuteChanged();
        }
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

            var location = await AddressGeocodingService.TryGeocodeOrderAsync(order);
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

    private string BuildSqlConnectionHint()
    {
        var trimmedPath = (SqlDatabasePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            return "Hinweis: Bitte SQL Server, Datenbankname und bei Bedarf den MDF-Pfad prüfen.";
        }

        if (trimmedPath.Length >= 2 && char.IsLetter(trimmedPath[0]) && trimmedPath[1] == ':')
        {
            try
            {
                var root = Path.GetPathRoot(trimmedPath);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory)
                    {
                        return "Hinweis: Der angegebene Pfad liegt auf einem Netzlaufwerk. SQL Server-Dienste können gemappte Laufwerke wie L: oft nicht verwenden. In diesem Fall direkt mit Server + Datenbankname verbinden oder einen lokalen/UNC-Pfad verwenden, auf den der SQL-Dienst Zugriff hat.";
                    }
                }
            }
            catch
            {
            }
        }

        return "Hinweis: Falls die Datenbank bereits an eine SQL-Instanz angehängt ist, reichen meist SQL Server und Datenbankname. Der MDF-Pfad ist dann optional.";
    }
}
