using System.Globalization;

namespace Tourenplaner.CSharp.App.Services;

public static class TourNameFormatter
{
    public static string NormalizeNewTourName(string? name, string? dateText)
    {
        var normalized = name?.Trim() ?? string.Empty;
        var isLegacyDefault = DateTime.TryParseExact(dateText?.Trim(), "dd.MM.yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && string.Equals(normalized, "Tour " + date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        return normalized.Length == 0 || isLegacyDefault ? BuildDefault(dateText) : normalized;
    }

    public static string BuildDefault(string? dateText)
    {
        if (!DateTime.TryParseExact(dateText?.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            date = DateTime.Today;
        }

        return date.ToString("dddd dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH"));
    }
}
