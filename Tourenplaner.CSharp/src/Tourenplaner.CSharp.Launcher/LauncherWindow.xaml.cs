using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Tourenplaner.CSharp.Launcher;

public partial class LauncherWindow : Window
{
    private bool _isLaunching;

    public LauncherWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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
            SetStatus("Installierte Anwendung wird gesucht...");
            await Task.Delay(200);

            var appPath = await ResolveAppPathAsync();
            SetStatus("Tourenplaner wird geoeffnet...");

            if (TryFocusExistingProcess())
            {
                Close();
                return;
            }

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

            await Task.Delay(900);
            Close();
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
