using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class SettingsValidator
{
    private static readonly HashSet<string> AllowedAppearanceModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Light",
        "Dark"
    };

    private static readonly HashSet<string> AllowedBackupModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "full",
        "incremental"
    };

    public ValidationResult Validate(AppSettings settings)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.SqlServerInstance))
        {
            errors.Add("SqlServerInstance is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlDataDir))
        {
            errors.Add("SqlDataDir is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlDatabase) || SqlDatabaseNameInference.InferDatabaseName($"Database={settings.SqlDatabase};") == "unknown_db")
        {
            errors.Add("SqlDatabase must be a valid database identifier.");
        }

        if (!AllowedAppearanceModes.Contains(settings.AppearanceMode ?? string.Empty))
        {
            errors.Add("AppearanceMode must be one of: System, Light, Dark.");
        }

        if (!AllowedBackupModes.Contains(settings.BackupModeDefault ?? string.Empty))
        {
            errors.Add("BackupModeDefault must be full or incremental.");
        }

        if (settings.BackupRetentionDays < 1)
        {
            errors.Add("BackupRetentionDays must be greater than zero.");
        }

        if (settings.AutoBackupEnabled && settings.AutoBackupIntervalDays < 1)
        {
            errors.Add("AutoBackupIntervalDays must be greater than zero when AutoBackupEnabled is true.");
        }

        if ((settings.BackupsEnabled || settings.AutoBackupEnabled) && string.IsNullOrWhiteSpace(settings.BackupDir))
        {
            errors.Add("BackupDir is required when backup is enabled.");
        }

        if (settings.QuickAccessItems is null)
        {
            errors.Add("QuickAccessItems must not be null.");
        }
        else if (settings.QuickAccessItems.Count > 8)
        {
            errors.Add("QuickAccessItems must contain at most 8 entries.");
        }

        return new ValidationResult(errors);
    }
}
