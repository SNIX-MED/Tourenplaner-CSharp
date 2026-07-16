using System.IO;
using System.Text.Json;

namespace Tourenplaner.CSharp.App.Services;

internal static class LocalUserSessionService
{
    private static string _storagePath = string.Empty;

    public static string CurrentUserName { get; private set; } = string.Empty;

    public static void Initialize(string dataRootPath)
    {
        _storagePath = string.IsNullOrWhiteSpace(dataRootPath)
            ? string.Empty
            : Path.Combine(dataRootPath, "current-user.json");
    }

    public static async Task<string> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_storagePath) || !File.Exists(_storagePath))
        {
            CurrentUserName = string.Empty;
            return CurrentUserName;
        }

        await using var stream = File.OpenRead(_storagePath);
        var payload = await JsonSerializer.DeserializeAsync<LocalUserSessionPayload>(stream, cancellationToken: cancellationToken);
        CurrentUserName = (payload?.UserName ?? string.Empty).Trim();
        return CurrentUserName;
    }

    public static async Task SaveAsync(string? userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_storagePath))
        {
            CurrentUserName = (userName ?? string.Empty).Trim();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath) ?? string.Empty);
        CurrentUserName = (userName ?? string.Empty).Trim();
        await using var stream = File.Create(_storagePath);
        await JsonSerializer.SerializeAsync(stream, new LocalUserSessionPayload { UserName = CurrentUserName }, cancellationToken: cancellationToken);
    }

    private sealed class LocalUserSessionPayload
    {
        public string UserName { get; set; } = string.Empty;
    }
}
