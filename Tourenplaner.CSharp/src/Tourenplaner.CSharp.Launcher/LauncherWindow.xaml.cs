using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Tourenplaner.CSharp.Launcher;

public partial class LauncherWindow : Window
{
    private bool _isLaunching;
    private readonly CancellationTokenSource _launchCancellation = new();

    public LauncherWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLaunching)
        {
            return;
        }

        _isLaunching = true;

        try
        {
            if (TryFocusExistingProcess())
            {
                Close();
                return;
            }

            var updateWasStarted = await TryStartUpdateAsync(_launchCancellation.Token);
            if (updateWasStarted)
            {
                return;
            }

            SetStatus("Installierte Anwendung wird gesucht...");
            await Task.Delay(200, _launchCancellation.Token);

            var appPath = await ResolveAppPathAsync();
            SetStatus("Tourenplaner wird geoeffnet...");

            var started = Process.Start(new ProcessStartInfo
            {
                FileName = appPath,
                WorkingDirectory = Path.GetDirectoryName(appPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });

            if (started is null)
            {
                throw new InvalidOperationException("Die Anwendung konnte nicht gestartet werden.");
            }

            await Task.Delay(900, _launchCancellation.Token);
            Close();
        }
        catch (OperationCanceledException)
        {
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Der Tourenplaner konnte nicht gestartet werden.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "GAWELA Tourenplaner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown(-1);
        }
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void SetDetails(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            DetailsTextBlock.Text = string.Empty;
            DetailsTextBlock.Visibility = Visibility.Collapsed;
            return;
        }

        DetailsTextBlock.Text = message;
        DetailsTextBlock.Visibility = Visibility.Visible;
    }

    private void SetProgress(double? percent, bool isIndeterminate, string? message = null)
    {
        ProgressBarControl.IsIndeterminate = isIndeterminate;
        ProgressBarControl.Value = percent ?? 0;

        if (string.IsNullOrWhiteSpace(message))
        {
            ProgressTextBlock.Text = string.Empty;
            ProgressTextBlock.Visibility = Visibility.Collapsed;
            return;
        }

        ProgressTextBlock.Text = message;
        ProgressTextBlock.Visibility = Visibility.Visible;
    }

    private async Task<bool> TryStartUpdateAsync(CancellationToken cancellationToken)
    {
        var configuration = await UpdateService.LoadConfigurationAsync(AppContext.BaseDirectory, cancellationToken);
        if (configuration is null)
        {
            return false;
        }

        SetStatus("Suche nach Updates...");
        SetProgress(null, isIndeterminate: true);

        try
        {
            var currentVersion = UpdateService.GetCurrentVersion();
            var manifest = await UpdateService.GetAvailableUpdateAsync(configuration, currentVersion, cancellationToken);
            if (manifest is null)
            {
                SetDetails(null);
                return false;
            }

            var versionText = manifest.ParsedVersion?.ToString() ?? manifest.Version;
            SetStatus($"Update {versionText} wird heruntergeladen...");
            SetDetails(GetReleaseDetails(manifest));

            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                var percentText = value.Percent is double percent
                    ? $"{percent:0}%"
                    : "wird geladen...";
                SetProgress(value.Percent, isIndeterminate: value.Percent is null, message: $"Download {percentText}");
            });

            var targetDirectory = Path.Combine(Path.GetTempPath(), "GAWELA-Tourenplaner", "downloads", versionText);
            var installerPath = await UpdateService.DownloadInstallerAsync(manifest, targetDirectory, progress, cancellationToken);

            SetStatus("Update ist bereit...");
            SetDetails("Windows wird jetzt das Setup mit Berechtigungsabfrage oeffnen.");
            SetProgress(null, isIndeterminate: true, message: "Setup wird gestartet");

            var launcherPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GAWELA.Tourenplaner.exe");
            var scriptPath = UpdateService.CreateInstallerScript(installerPath, launcherPath, Environment.ProcessId);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            await Task.Delay(700, cancellationToken);
            Application.Current.Shutdown();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            SetDetails("Die installierte Version wird gestartet, weil das Update gerade nicht geladen werden konnte.");
            SetProgress(null, isIndeterminate: true);
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    this,
                    $"Das automatische Update konnte nicht abgeschlossen werden.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    "GAWELA Tourenplaner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }, DispatcherPriority.Normal, cancellationToken);
            return false;
        }
    }

    private static string GetReleaseDetails(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            return "Eine neue Version wird automatisch installiert.";
        }

        var notes = manifest.ReleaseNotes.Trim();
        return notes.Length <= 180 ? notes : $"{notes[..177]}...";
    }

    private async Task<string> ResolveAppPathAsync()
    {
        var candidates = GetAppCandidates().ToArray();

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is not null)
        {
            SetStatus("Lokale Version wird im Hintergrund gebaut...");
            await BuildDevelopmentAppAsync(repositoryRoot);

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("Es wurde keine startbare Tourenplaner-App gefunden.");
    }

    private static IEnumerable<string> GetAppCandidates()
    {
        return
        [
            Path.Combine(AppContext.BaseDirectory, "Tourenplaner.CSharp.App.exe"),
            Path.Combine(AppContext.BaseDirectory, "app", "Tourenplaner.CSharp.App.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Tourenplaner.CSharp.App", "bin", "Debug", "net8.0-windows", "Tourenplaner.CSharp.App.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Tourenplaner.CSharp.App", "bin", "Release", "net8.0-windows", "Tourenplaner.CSharp.App.exe"))
        ];
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tourenplaner.CSharp.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static async Task BuildDevelopmentAppAsync(string repositoryRoot)
    {
        var solutionPath = Path.Combine(repositoryRoot, "Tourenplaner.CSharp.sln");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{solutionPath}\" -c Debug -v minimal -p:NuGetAudit=false",
            WorkingDirectory = repositoryRoot,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Der Hintergrund-Build konnte nicht gestartet werden.");
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var errorOutput = await process.StandardError.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(errorOutput))
            {
                errorOutput = await process.StandardOutput.ReadToEndAsync();
            }

            throw new InvalidOperationException(
                $"Der Hintergrund-Build ist fehlgeschlagen.{Environment.NewLine}{Environment.NewLine}{errorOutput.Trim()}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _launchCancellation.Cancel();
        _launchCancellation.Dispose();
    }

    private static bool TryFocusExistingProcess()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var running = Process.GetProcessesByName("Tourenplaner.CSharp.App")
            .Where(process => process.SessionId == sessionId)
            .ToList();

        foreach (var process in running)
        {
            try
            {
                process.Refresh();
                if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                NativeMethods.ShowWindowAsync(process.MainWindowHandle, 9);
                NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                return true;
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        return false;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
