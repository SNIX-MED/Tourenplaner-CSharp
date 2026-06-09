using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tourenplaner.CSharp.App.Services;

internal static class InstalledAppUpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<InstalledAppUpdateResult> TryApplyUpdateAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var configPath = FindUpdateConfigPath(AppContext.BaseDirectory);
        if (configPath is null)
        {
            return InstalledAppUpdateResult.NotConfigured;
        }

        progress?.Report("Suche nach Updates...");

        var configuration = await ReadJsonFileAsync<UpdateConfiguration>(configPath, cancellationToken);
        if (configuration is null || string.IsNullOrWhiteSpace(configuration.ManifestUrl))
        {
            return InstalledAppUpdateResult.Failed("Die Update-Konfiguration ist unvollständig.");
        }

        using var manifestResponse = await Client.GetAsync(configuration.ManifestUrl.Trim(), cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();
        await using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await ReadJsonAsync<UpdateManifest>(manifestStream, cancellationToken);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.InstallerUrl))
        {
            return InstalledAppUpdateResult.Failed("Das Online-Update-Manifest konnte nicht gelesen werden.");
        }

        var currentVersion = ParseVersion(GetCurrentVersion());
        var availableVersion = ParseVersion(manifest.Version);
        if (currentVersion is null || availableVersion is null || availableVersion <= currentVersion)
        {
            return InstalledAppUpdateResult.NoUpdateAvailable;
        }

        progress?.Report($"Update {manifest.Version} wird heruntergeladen...");
        var installerPath = await DownloadInstallerAsync(manifest, progress, cancellationToken);

        progress?.Report("Update ist bereit. Das Setup wird gestartet...");
        var launcherPath = ResolveLauncherPath(AppContext.BaseDirectory);
        var scriptPath = CreateInstallerScript(installerPath, launcherPath, Environment.ProcessId);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true
        });

        return InstalledAppUpdateResult.UpdateStarted;
    }

    private static async Task<string> DownloadInstallerAsync(
        UpdateManifest manifest,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.Combine(
            Path.GetTempPath(),
            "GAWELA-Tourenplaner",
            "downloads",
            manifest.Version.Trim(),
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(targetDirectory);

        var fileName = Path.GetFileName(new Uri(manifest.InstallerUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "GAWELA-Tourenplaner-Setup.exe";
        }

        var destinationPath = Path.Combine(targetDirectory, fileName);

        using (var response = await Client.GetAsync(manifest.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);

            var buffer = new byte[81920];
            long downloadedBytes = 0;
            while (true)
            {
                var read = await responseStream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;

                var progressText = totalBytes is > 0
                    ? $"Update wird heruntergeladen... {downloadedBytes * 100d / totalBytes.Value:0}%"
                    : "Update wird heruntergeladen...";
                progress?.Report(progressText);
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            var actualHash = ComputeSha256(destinationPath);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Die heruntergeladene Update-Datei ist beschädigt oder unvollständig.");
            }
        }

        return destinationPath;
    }

    private static string CreateInstallerScript(string installerPath, string? launcherPath, int currentProcessId)
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), "GAWELA-Tourenplaner");
        Directory.CreateDirectory(scriptDirectory);

        var scriptPath = Path.Combine(scriptDirectory, $"apply-update-from-app-{Guid.NewGuid():N}.ps1");
        var launcherLine = string.IsNullOrWhiteSpace(launcherPath)
            ? string.Empty
            : "$launcher = '" + EscapePowerShell(launcherPath) + "'" + Environment.NewLine +
              "if ($process.ExitCode -eq 0 -and (Test-Path $launcher)) {" + Environment.NewLine +
              "    Start-Process -FilePath $launcher" + Environment.NewLine +
              "}" + Environment.NewLine;

        var script = $$"""
        $ErrorActionPreference = 'Stop'
        while (Get-Process -Id {{currentProcessId}} -ErrorAction SilentlyContinue) {
            Start-Sleep -Milliseconds 400
        }

        $installer = '{{EscapePowerShell(installerPath)}}'
        $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CLOSEAPPLICATIONS', '/FORCECLOSEAPPLICATIONS')

        for ($i = 0; $i -lt 40; $i++) {
            try {
                $stream = [System.IO.File]::Open($installer, 'Open', 'Read', 'ReadWrite')
                $stream.Dispose()
                break
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }

        $process = Start-Process -FilePath $installer -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        {{launcherLine}}
        """;

        File.WriteAllText(scriptPath, script, Encoding.ASCII);
        return scriptPath;
    }

    private static string? FindUpdateConfigPath(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "update-config.json"),
            Path.Combine(baseDirectory, "..", "update-config.json"),
            Path.Combine(baseDirectory, "..", "..", "update-config.json")
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static string? ResolveLauncherPath(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "GAWELA.Tourenplaner.exe"),
            Path.Combine(baseDirectory, "..", "GAWELA.Tourenplaner.exe"),
            Path.Combine(baseDirectory, "..", "..", "GAWELA.Tourenplaner.exe")
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static string GetCurrentVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entryAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? entryAssembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    private static Version? ParseVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        var normalized = versionText.Trim().TrimStart('\uFEFF');
        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static async Task<T?> ReadJsonFileAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await ReadJsonAsync<T>(stream, cancellationToken);
    }

    private static async Task<T?> ReadJsonAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var normalized = content.Trim().TrimStart('\uFEFF');
        return string.IsNullOrWhiteSpace(normalized)
            ? default
            : JsonSerializer.Deserialize<T>(normalized, JsonOptions);
    }

    private static string EscapePowerShell(string path)
    {
        return path.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GAWELA-Tourenplaner-AppUpdater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private sealed class UpdateConfiguration
    {
        public string ManifestUrl { get; set; } = string.Empty;
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;

        public string InstallerUrl { get; set; } = string.Empty;

        public string? Sha256 { get; set; }
    }
}

internal sealed record InstalledAppUpdateResult(bool IsConfigured, bool IsUpdateAvailable, bool UpdateWasStarted, string? ErrorMessage)
{
    public static InstalledAppUpdateResult NotConfigured { get; } = new(false, false, false, null);

    public static InstalledAppUpdateResult NoUpdateAvailable { get; } = new(true, false, false, null);

    public static InstalledAppUpdateResult UpdateStarted { get; } = new(true, true, true, null);

    public static InstalledAppUpdateResult Failed(string message) => new(true, true, false, message);
}
