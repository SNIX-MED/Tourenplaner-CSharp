using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Tourenplaner.CSharp.App.Services;

internal static class AppRuntimeDiagnosticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetCrashLogPath(string dataRoot)
        => Path.Combine(dataRoot, "app-crash.log");

    public static string GetStartupDiagnosticsPath(string dataRoot)
        => Path.Combine(dataRoot, "startup-diagnostics.json");

    public static AppStartupDiagnosticsSnapshot LoadStartupDiagnostics(string dataRoot)
    {
        var path = GetStartupDiagnosticsPath(dataRoot);
        if (!File.Exists(path))
        {
            return AppStartupDiagnosticsSnapshot.CreateMissing(path, dataRoot, GetCrashLogPath(dataRoot));
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8).Trim().TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(json))
            {
                return AppStartupDiagnosticsSnapshot.CreateMissing(path, dataRoot, GetCrashLogPath(dataRoot));
            }

            var snapshot = JsonSerializer.Deserialize<AppStartupDiagnosticsSnapshot>(json, JsonOptions);
            return snapshot ?? AppStartupDiagnosticsSnapshot.CreateMissing(path, dataRoot, GetCrashLogPath(dataRoot));
        }
        catch
        {
            return AppStartupDiagnosticsSnapshot.CreateMissing(path, dataRoot, GetCrashLogPath(dataRoot));
        }
    }

    public static async Task WriteStartupDiagnosticsAsync(AppStartupDiagnosticsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var path = GetStartupDiagnosticsPath(snapshot.DataRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? snapshot.DataRootPath);
        var json = JsonSerializer.Serialize(snapshot with { DiagnosticsFilePath = path }, JsonOptions);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
    }

    public static string? FindUpdateConfigPath(string baseDirectory)
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
}

internal sealed record AppStartupDiagnosticsSnapshot(
    string Status,
    string Summary,
    string? Detail,
    string? LastStep,
    DateTime RecordedAtUtc,
    string DataRootPath,
    string AppBaseDirectory,
    string CrashLogPath,
    string DiagnosticsFilePath,
    string? UpdateConfigPath,
    string? StorageMode,
    string? ExceptionType)
{
    public static AppStartupDiagnosticsSnapshot CreateMissing(string diagnosticsFilePath, string dataRootPath, string crashLogPath)
        => new(
            "Unknown",
            "Noch keine Startdiagnose vorhanden.",
            null,
            null,
            DateTime.MinValue,
            dataRootPath,
            AppContext.BaseDirectory,
            crashLogPath,
            diagnosticsFilePath,
            AppRuntimeDiagnosticsService.FindUpdateConfigPath(AppContext.BaseDirectory),
            null,
            null);
}
