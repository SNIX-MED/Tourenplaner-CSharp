using System.IO;
using System.Text.Json;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

/// <summary>
/// Stores WEBFLEET credentials locally per selected program user. These credentials must not
/// participate in the shared application-settings synchronization.
/// </summary>
internal static class WebfleetUserSettingsService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string _storagePath = string.Empty;

    public static void Initialize(string dataRootPath)
    {
        _storagePath = string.IsNullOrWhiteSpace(dataRootPath)
            ? string.Empty
            : Path.Combine(dataRootPath, "webfleet-user-settings.json");
    }

    public static async Task<WebfleetConnectionSettings?> LoadAsync(string? userName, CancellationToken cancellationToken = default)
    {
        var profiles = await LoadProfilesAsync(cancellationToken);
        return profiles.TryGetValue(AppSettings.NormalizeUserName(userName), out var profile)
            ? Clone(profile)
            : null;
    }

    public static async Task<WebfleetConnectionSettings?> LoadOrMigrateLegacyAsync(
        AppSettings settings,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        var profile = await LoadAsync(userName, cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        var legacy = settings?.Webfleet;
        if (legacy is null || !IsLegacyProfileOwnedByUser(legacy, userName))
        {
            return null;
        }

        profile = Clone(legacy);
        await SaveAsync(userName, profile, cancellationToken);
        return profile;
    }

    public static async Task SaveAsync(string? userName, WebfleetConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(_storagePath))
        {
            return;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await LoadProfilesCoreAsync(cancellationToken);
            profiles[AppSettings.NormalizeUserName(userName)] = Clone(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath) ?? string.Empty);
            await using var stream = File.Create(_storagePath);
            await JsonSerializer.SerializeAsync(stream, new WebfleetUserSettingsPayload { Profiles = profiles }, cancellationToken: cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<Dictionary<string, WebfleetConnectionSettings>> LoadProfilesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_storagePath))
        {
            return new Dictionary<string, WebfleetConnectionSettings>(StringComparer.OrdinalIgnoreCase);
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadProfilesCoreAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<Dictionary<string, WebfleetConnectionSettings>> LoadProfilesCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_storagePath) || !File.Exists(_storagePath))
        {
            return new Dictionary<string, WebfleetConnectionSettings>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(_storagePath);
        var payload = await JsonSerializer.DeserializeAsync<WebfleetUserSettingsPayload>(stream, cancellationToken: cancellationToken);
        return new Dictionary<string, WebfleetConnectionSettings>(payload?.Profiles ?? new Dictionary<string, WebfleetConnectionSettings>(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLegacyProfileOwnedByUser(WebfleetConnectionSettings legacy, string? userName) =>
        !string.IsNullOrWhiteSpace(legacy.AccountName) &&
        !string.IsNullOrWhiteSpace(legacy.UserName) &&
        string.Equals(AppSettings.NormalizeUserName(legacy.UserName), AppSettings.NormalizeUserName(userName), StringComparison.OrdinalIgnoreCase);

    private static WebfleetConnectionSettings Clone(WebfleetConnectionSettings source) => new()
    {
        IsEnabled = source.IsEnabled,
        AccountName = source.AccountName ?? string.Empty,
        UserName = source.UserName ?? string.Empty,
        ApiKey = source.ApiKey ?? string.Empty,
        Password = source.Password ?? string.Empty,
        PositionRefreshSeconds = source.PositionRefreshSeconds,
        CsvEndpoint = source.CsvEndpoint ?? WebfleetConnectionSettings.DefaultCsvEndpoint
    };

    private sealed class WebfleetUserSettingsPayload
    {
        public Dictionary<string, WebfleetConnectionSettings> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
