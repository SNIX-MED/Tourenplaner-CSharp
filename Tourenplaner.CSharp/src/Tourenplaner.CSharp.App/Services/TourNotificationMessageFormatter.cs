using System.Globalization;
using System.Text;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class TourNotificationMessageFormatter
{
    public static string GetTourDisplayName(TourRecord? tour, CultureInfo? culture = null)
    {
        var effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        var normalizedName = NormalizePlainText(tour?.Name);
        if (normalizedName.Length > 0)
        {
            return normalizedName;
        }

        if (TryResolveTourDate(tour?.Date, effectiveCulture, out var date))
        {
            return $"Tour vom {date.ToString("d", effectiveCulture)}";
        }

        return "Die neue Tour";
    }

    public static string FormatTourCreatedMessage(TourRecord? tour, CultureInfo? culture = null)
    {
        var displayName = GetTourDisplayName(tour, culture);
        return string.Equals(displayName, "Die neue Tour", StringComparison.Ordinal)
            ? "Die neue Tour wurde erstellt."
            : HasCustomName(tour)
                ? $"\u201e{displayName}\u201c wurde erstellt."
                : $"{displayName} wurde erstellt.";
    }

    public static string FormatTourUpdatedMessage(TourRecord? tour, CultureInfo? culture = null)
    {
        var displayName = GetTourDisplayName(tour, culture);
        if (string.Equals(displayName, "Die neue Tour", StringComparison.Ordinal))
        {
            return "Die Tour wurde aktualisiert.";
        }

        return HasCustomName(tour)
            ? $"\u201e{displayName}\u201c wurde aktualisiert."
            : $"{displayName} wurde aktualisiert.";
    }

    private static bool HasCustomName(TourRecord? tour) => NormalizePlainText(tour?.Name).Length > 0;

    private static string NormalizePlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = true;
        foreach (var character in value)
        {
            var isWhitespace = char.IsWhiteSpace(character) || char.IsControl(character);
            if (isWhitespace)
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool TryResolveTourDate(string? value, CultureInfo culture, out DateTime date)
    {
        var normalized = NormalizePlainText(value);
        return DateTime.TryParse(normalized, culture, DateTimeStyles.AllowWhiteSpaces, out date) ||
               DateTime.TryParseExact(
                   normalized,
                   ["dd.MM.yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"],
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out date);
    }
}
