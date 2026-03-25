using System.Text.RegularExpressions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class SettingsValidator
{
    private static readonly Regex HexColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedBackupModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "full",
        "incremental"
    };

    public ValidationResult Validate(AppSettings settings)
    {
        var errors = new List<string>();

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

        if (string.IsNullOrWhiteSpace(settings.AvisoEmailSubjectTemplate))
        {
            errors.Add("AvisoEmailSubjectTemplate must not be empty.");
        }

        if (!Uri.TryCreate((settings.UpdateFeedUrl ?? string.Empty).Trim(), UriKind.Absolute, out var updateUri))
        {
            errors.Add("UpdateFeedUrl must be a valid absolute URL.");
        }
        else
        {
            var host = updateUri.Host.Trim().ToLowerInvariant();
            if (host != "github.com" && host != "www.github.com" && host != "api.github.com")
            {
                errors.Add("UpdateFeedUrl must point to GitHub.");
            }
        }

        var hasAnyCompanyAddressPart =
            !string.IsNullOrWhiteSpace(settings.CompanyStreet) ||
            !string.IsNullOrWhiteSpace(settings.CompanyPostalCode) ||
            !string.IsNullOrWhiteSpace(settings.CompanyCity);
        if (hasAnyCompanyAddressPart &&
            (string.IsNullOrWhiteSpace(settings.CompanyStreet) ||
             string.IsNullOrWhiteSpace(settings.CompanyPostalCode) ||
             string.IsNullOrWhiteSpace(settings.CompanyCity)))
        {
            errors.Add("Company address must include street, postal code and city when configured.");
        }

        if (settings.QuickAccessItems is null)
        {
            errors.Add("QuickAccessItems must not be null.");
        }
        else if (settings.QuickAccessItems.Count > 8)
        {
            errors.Add("QuickAccessItems must contain at most 8 entries.");
        }

        ValidateHexColor(settings.StatusColorNotSpecified, "StatusColorNotSpecified", errors);
        ValidateHexColor(settings.StatusColorOrdered, "StatusColorOrdered", errors);
        ValidateHexColor(settings.StatusColorOnTheWay, "StatusColorOnTheWay", errors);
        ValidateHexColor(settings.StatusColorInStock, "StatusColorInStock", errors);
        ValidateHexColor(settings.StatusColorPlanned, "StatusColorPlanned", errors);
        ValidateHexColor(settings.CalendarLoadWarningColor, "CalendarLoadWarningColor", errors);
        ValidateHexColor(settings.CalendarLoadCriticalColor, "CalendarLoadCriticalColor", errors);

        if (settings.CalendarLoadWarningPeopleThreshold < 1)
        {
            errors.Add("CalendarLoadWarningPeopleThreshold must be greater than zero.");
        }

        if (settings.CalendarLoadCriticalPeopleThreshold < 1)
        {
            errors.Add("CalendarLoadCriticalPeopleThreshold must be greater than zero.");
        }

        if (settings.CalendarLoadCriticalPeopleThreshold < settings.CalendarLoadWarningPeopleThreshold)
        {
            errors.Add("CalendarLoadCriticalPeopleThreshold must be greater than or equal to CalendarLoadWarningPeopleThreshold.");
        }

        return new ValidationResult(errors);
    }

    private static void ValidateHexColor(string? value, string propertyName, List<string> errors)
    {
        if (!HexColorRegex.IsMatch((value ?? string.Empty).Trim()))
        {
            errors.Add($"{propertyName} must be a hex color in the format #RRGGBB.");
        }
    }
}
