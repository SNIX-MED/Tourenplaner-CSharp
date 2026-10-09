using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

internal static class ResourceUnavailabilityNormalizer
{
    public static List<ResourceUnavailabilityPeriod> NormalizePeriods(IEnumerable<ResourceUnavailabilityPeriod>? source)
    {
        var normalized = new List<ResourceUnavailabilityPeriod>();
        foreach (var item in source ?? [])
        {
            if (item is null)
            {
                continue;
            }

            var normalizedStart = NormalizeDate(item.StartDate);
            var normalizedEnd = NormalizeDate(item.EndDate);
            if (normalizedStart is null || normalizedEnd is null)
            {
                continue;
            }

            if (normalizedEnd.Value < normalizedStart.Value)
            {
                (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
            }

            normalized.Add(new ResourceUnavailabilityPeriod
            {
                StartDate = normalizedStart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = normalizedEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StartTime = NormalizeTime(item.StartTime),
                EndTime = NormalizeTime(item.EndTime),
                Note = (item.Note ?? string.Empty).Trim()
            });
        }

        return normalized
            .GroupBy(x => $"{x.StartDate}|{x.StartTime}|{x.EndDate}|{x.EndTime}|{x.Note}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.StartDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.EndDate, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeTime(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        return TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static DateOnly? NormalizeDate(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy" };
        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback
            : null;
    }
}
