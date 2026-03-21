using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class UpdatesSectionViewModel : SectionViewModelBase
{
    private readonly JsonAppSettingsRepository _settingsRepository;

    private string _statusText = "Loading update status...";
    private string _applicationVersion = string.Empty;
    private string _runtimeVersion = string.Empty;
    private string _lastBackupIso = string.Empty;
    private string _latestBackupFile = "n/a";
    private int _availableBackupsCount;
    private string _updateFeedUrl = "https://github.com/";

    public UpdatesSectionViewModel(string settingsJsonPath)
        : base("Updates", "Version info, backup status and update channel links.")
    {
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        OpenUpdateFeedCommand = new DelegateCommand(OpenUpdateFeed);
        _ = RefreshAsync();
    }

    public ICommand RefreshCommand { get; }

    public ICommand OpenUpdateFeedCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
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

    public string LastBackupIso
    {
        get => _lastBackupIso;
        private set => SetProperty(ref _lastBackupIso, value);
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

    public async Task RefreshAsync()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        ApplicationVersion = entryAssembly.GetName().Version?.ToString() ?? "0.0.0";
        RuntimeVersion = Environment.Version.ToString();

        var settings = await _settingsRepository.LoadAsync();
        LastBackupIso = string.IsNullOrWhiteSpace(settings.LastBackupIso) ? "n/a" : settings.LastBackupIso;
        UpdateFeedUrl = $"https://github.com/search?q={Uri.EscapeDataString(settings.SqlDatabase)}";

        if (!string.IsNullOrWhiteSpace(settings.BackupDir) && Directory.Exists(settings.BackupDir))
        {
            var files = Directory.GetFiles(settings.BackupDir, "*.bak", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            AvailableBackupsCount = files.Count;
            LatestBackupFile = files.FirstOrDefault() is { } latest ? Path.GetFileName(latest) : "n/a";
        }
        else
        {
            AvailableBackupsCount = 0;
            LatestBackupFile = "n/a";
        }

        StatusText = "Update status refreshed.";
    }

    private void OpenUpdateFeed()
    {
        if (!Uri.TryCreate((UpdateFeedUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            StatusText = "Invalid update URL.";
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        StatusText = "Opened update feed in browser.";
    }
}
