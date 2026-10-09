using System.IO;
using System.Text.Json;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

internal static class UpdateStorageModeGuard
{
    private const string StateFileName = "storage-mode-before-update.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task CaptureAsync(
        string dataRoot,
        string stateRoot,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(dataRoot, "settings.json");
        var settings = await new JsonAppSettingsRepository(settingsPath).LoadAsync(cancellationToken);
        var state = new UpdateStorageModeState
        {
            StorageMode = settings.StorageMode,
            PostgreSqlStorage = settings.StorageMode == AppStorageMode.PostgreSql
                ? settings.PostgreSqlStorage
                : null,
            TargetVersion = targetVersion,
            CapturedAtUtc = DateTimeOffset.UtcNow
        };

        Directory.CreateDirectory(stateRoot);
        var statePath = Path.Combine(stateRoot, StateFileName);
        var temporaryPath = statePath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(state, JsonOptions),
            cancellationToken);
        File.Move(temporaryPath, statePath, overwrite: true);
    }

    public static async Task<AppStorageMode?> RestoreAsync(
        string dataRoot,
        string stateRoot,
        CancellationToken cancellationToken = default)
    {
        var statePath = Path.Combine(stateRoot, StateFileName);
        if (!File.Exists(statePath))
        {
            return null;
        }

        var stateJson = await File.ReadAllTextAsync(statePath, cancellationToken);
        var state = JsonSerializer.Deserialize<UpdateStorageModeState>(stateJson);
        if (state is null || !Enum.IsDefined(state.StorageMode))
        {
            throw new InvalidDataException("Der vor dem Update gespeicherte Speichermodus ist ungültig.");
        }

        var settingsPath = Path.Combine(dataRoot, "settings.json");
        var repository = new JsonAppSettingsRepository(settingsPath);
        var settings = await repository.LoadAsync(cancellationToken);
        settings.StorageMode = state.StorageMode;

        if (state.StorageMode == AppStorageMode.PostgreSql && state.PostgreSqlStorage?.IsConfigured() == true)
        {
            settings.PostgreSqlStorage = state.PostgreSqlStorage;
        }

        await repository.SaveAsync(settings, cancellationToken);
        File.Delete(statePath);
        return state.StorageMode;
    }

    private sealed class UpdateStorageModeState
    {
        public AppStorageMode StorageMode { get; set; }

        public PostgreSqlStorageSettings? PostgreSqlStorage { get; set; }

        public string TargetVersion { get; set; } = string.Empty;

        public DateTimeOffset CapturedAtUtc { get; set; }
    }
}
