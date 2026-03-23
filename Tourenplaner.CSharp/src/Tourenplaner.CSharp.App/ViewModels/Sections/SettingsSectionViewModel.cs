using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class SettingsSectionViewModel : SectionViewModelBase
{
    private readonly JsonAppSettingsRepository _repository;
    private readonly SettingsValidator _validator;
    private readonly BackupManager _backupManager;
    private readonly string _dataRoot;

    private string _statusText = "Loading settings...";
    private string _appearanceMode = "System";
    private string _googleMapsApiKey = string.Empty;
    private string _avisoEmailSubjectTemplate = AppSettings.DefaultAvisoEmailSubjectTemplate;
    private string _companyName = "Firma";
    private string _companyStreet = string.Empty;
    private string _companyPostalCode = string.Empty;
    private string _companyCity = string.Empty;
    private bool _backupsEnabled;
    private string _backupDir = string.Empty;
    private string _backupModeDefault = "full";
    private int _backupRetentionDays = 30;
    private bool _autoBackupEnabled;
    private int _autoBackupIntervalDays = 7;
    private string _lastBackupIso = string.Empty;
    private string _validationSummary = string.Empty;

    public SettingsSectionViewModel(string settingsJsonPath, string dataRoot)
        : base("Settings", "Appearance, backup policy and restore operations.")
    {
        _repository = new JsonAppSettingsRepository(settingsJsonPath);
        _validator = new SettingsValidator();
        _backupManager = new BackupManager();
        _dataRoot = dataRoot;

        AppearanceModes =
        [
            "System",
            "Light",
            "Dark"
        ];

        BackupModes =
        [
            "full",
            "incremental"
        ];

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync);
        ValidateCommand = new DelegateCommand(ValidateCurrentSettings);
        CreateBackupCommand = new AsyncCommand(CreateBackupAsync);
        RestoreLatestBackupCommand = new AsyncCommand(RestoreLatestBackupAsync);
        CleanupBackupsCommand = new DelegateCommand(CleanupBackups);

        _ = RefreshAsync();
    }

    public ObservableCollection<string> AppearanceModes { get; }

    public ObservableCollection<string> BackupModes { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ValidateCommand { get; }

    public ICommand CreateBackupCommand { get; }

    public ICommand RestoreLatestBackupCommand { get; }

    public ICommand CleanupBackupsCommand { get; }

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

    public string AppearanceMode
    {
        get => _appearanceMode;
        set => SetProperty(ref _appearanceMode, value);
    }

    public string GoogleMapsApiKey
    {
        get => _googleMapsApiKey;
        set => SetProperty(ref _googleMapsApiKey, value);
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

    public async Task RefreshAsync()
    {
        var settings = await _repository.LoadAsync();
        ApplyModel(settings);
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
        ValidationSummary = "Settings are valid.";
        StatusText = "Settings saved.";
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

        StatusText = $"Backup created: {Path.GetFileName(backupPath)}";
        ValidationSummary = "Backup completed successfully.";
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

    private AppSettings BuildModel()
    {
        return new AppSettings
        {
            AppearanceMode = (AppearanceMode ?? string.Empty).Trim(),
            GoogleMapsApiKey = (GoogleMapsApiKey ?? string.Empty).Trim(),
            AvisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(AvisoEmailSubjectTemplate)
                ? AppSettings.DefaultAvisoEmailSubjectTemplate
                : AvisoEmailSubjectTemplate.Trim(),
            CompanyName = string.IsNullOrWhiteSpace(CompanyName) ? "Firma" : CompanyName.Trim(),
            CompanyStreet = (CompanyStreet ?? string.Empty).Trim(),
            CompanyPostalCode = (CompanyPostalCode ?? string.Empty).Trim(),
            CompanyCity = (CompanyCity ?? string.Empty).Trim(),
            BackupsEnabled = BackupsEnabled,
            BackupDir = (BackupDir ?? string.Empty).Trim(),
            BackupModeDefault = (BackupModeDefault ?? string.Empty).Trim(),
            BackupRetentionDays = BackupRetentionDays,
            AutoBackupEnabled = AutoBackupEnabled,
            AutoBackupIntervalDays = AutoBackupIntervalDays,
            LastBackupIso = LastBackupIso,
            QuickAccessItems = new List<string>()
        };
    }

    private void ApplyModel(AppSettings settings)
    {
        AppearanceMode = settings.AppearanceMode;
        GoogleMapsApiKey = settings.GoogleMapsApiKey;
        AvisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(settings.AvisoEmailSubjectTemplate)
            ? AppSettings.DefaultAvisoEmailSubjectTemplate
            : settings.AvisoEmailSubjectTemplate;
        CompanyName = string.IsNullOrWhiteSpace(settings.CompanyName) ? "Firma" : settings.CompanyName;
        CompanyStreet = settings.CompanyStreet ?? string.Empty;
        CompanyPostalCode = settings.CompanyPostalCode ?? string.Empty;
        CompanyCity = settings.CompanyCity ?? string.Empty;
        BackupsEnabled = settings.BackupsEnabled;
        BackupDir = settings.BackupDir;
        BackupModeDefault = settings.BackupModeDefault;
        BackupRetentionDays = settings.BackupRetentionDays;
        AutoBackupEnabled = settings.AutoBackupEnabled;
        AutoBackupIntervalDays = settings.AutoBackupIntervalDays;
        LastBackupIso = settings.LastBackupIso;
    }
}
