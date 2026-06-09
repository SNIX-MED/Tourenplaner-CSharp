using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Tourenplaner.CSharp.App.Services;

internal static class AppUpdateStatusService
{
    private static readonly HttpClient Client = CreateClient();

    public static string GetCurrentVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entryAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? entryAssembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    public static async Task<AppUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var currentVersionText = GetCurrentVersion();
        var currentVersion = ParseVersion(currentVersionText);
        var configPath = FindUpdateConfigPath(AppContext.BaseDirectory);
        if (configPath is null)
        {
            return new AppUpdateCheckResult(
                currentVersionText,
                "Update-Prüfung ist nur in der installierten Release-Version verfügbar.",
                null,
                null,
                null,
                false,
                false,
                DateTime.UtcNow);
        }

        await using var configStream = File.OpenRead(configPath);
        var configuration = await JsonSerializer.DeserializeAsync<AppUpdateConfiguration>(configStream, cancellationToken: cancellationToken);
        if (configuration is null || string.IsNullOrWhiteSpace(configuration.ManifestUrl))
        {
            return new AppUpdateCheckResult(
                currentVersionText,
                "Die Update-Konfiguration ist unvollständig.",
                null,
                null,
                null,
                false,
                false,
                DateTime.UtcNow);
        }

        using var response = await Client.GetAsync(configuration.ManifestUrl.Trim(), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var manifestStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<AppUpdateManifest>(manifestStream, cancellationToken: cancellationToken);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.InstallerUrl))
        {
            return new AppUpdateCheckResult(
                currentVersionText,
                "Das Online-Update-Manifest konnte nicht gelesen werden.",
                null,
                null,
                null,
                true,
                false,
                DateTime.UtcNow);
        }

        var availableVersion = ParseVersion(manifest.Version);
        var isUpdateAvailable = availableVersion is not null && currentVersion is not null && availableVersion > currentVersion;
        var statusText = isUpdateAvailable
            ? $"Update {manifest.Version} ist verfügbar."
            : "Es ist bereits die neueste Version installiert.";

        return new AppUpdateCheckResult(
            currentVersionText,
            statusText,
            manifest.Version,
            manifest.InstallerUrl,
            manifest.PublishedAtUtc,
            true,
            isUpdateAvailable,
            DateTime.UtcNow);
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

    private static Version? ParseVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        var normalized = versionText.Trim();
        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GAWELA-Tourenplaner-App/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private sealed class AppUpdateConfiguration
    {
        public string ManifestUrl { get; set; } = string.Empty;
    }

    private sealed class AppUpdateManifest
    {
        public string Version { get; set; } = string.Empty;

        public string InstallerUrl { get; set; } = string.Empty;

        public string? PublishedAtUtc { get; set; }
    }
}

internal sealed record AppUpdateCheckResult(
    string CurrentVersion,
    string StatusText,
    string? AvailableVersion,
    string? InstallerUrl,
    string? PublishedAtUtc,
    bool IsConfigured,
    bool IsUpdateAvailable,
    DateTime CheckedAtUtc);
