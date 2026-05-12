using System.Text.RegularExpressions;
using System.Globalization;
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

        if (!Uri.TryCreate((settings.GpsToolUrl ?? string.Empty).Trim(), UriKind.Absolute, out _))
        {
            errors.Add("GpsToolUrl must be a valid absolute URL.");
        }

        if (!Uri.TryCreate((settings.SpediteurToolUrl ?? string.Empty).Trim(), UriKind.Absolute, out _))
        {
            errors.Add("SpediteurToolUrl must be a valid absolute URL.");
        }

        var normalizedTourStartTime = (settings.TourDefaultStartTime ?? string.Empty).Trim();
        if (!TimeOnly.TryParseExact(normalizedTourStartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add("TourDefaultStartTime must be in the format HH:mm.");
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

        if (settings.MapRouteCapacityWarningThresholdPercent is < 0 or > 100)
        {
            errors.Add("MapRouteCapacityWarningThresholdPercent must be between 0 and 100.");
        }

        if (settings.PinInfoCardScale is < 0.7d or > 1.8d)
        {
            errors.Add("PinInfoCardScale must be between 0.7 and 1.8.");
        }

        if (settings.TomTomTrafficRefreshSeconds < 15)
        {
            errors.Add("TomTomTrafficRefreshSeconds must be at least 15.");
        }

        if (settings.TomTomRouteRecalcDebounceMs is < 100 or > 10000)
        {
            errors.Add("TomTomRouteRecalcDebounceMs must be between 100 and 10000.");
        }

        var tomTomRoutingMode = (settings.TomTomRoutingMode ?? string.Empty).Trim().ToLowerInvariant();
        if (tomTomRoutingMode is not ("car" or "heightaware"))
        {
            errors.Add("TomTomRoutingMode must be either 'car' or 'heightAware'.");
        }

        if (settings.TomTomVehicleHeightMeters is < 0d or > 20d)
        {
            errors.Add("TomTomVehicleHeightMeters must be between 0 and 20.");
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
