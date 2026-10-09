using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class ResourceAvailabilityService
{
    public static DateOnly? ParseDate(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var formats = new[] { "dd.MM.yyyy", "yyyy-MM-dd" };
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

    public static bool IsUnavailableOnDate(IEnumerable<ResourceUnavailabilityPeriod>? periods, DateOnly date)
    {
        foreach (var period in periods ?? [])
        {
            if (period is null)
            {
                continue;
            }

            var start = ParseDate(period.StartDate);
            var end = ParseDate(period.EndDate);
            if (!start.HasValue || !end.HasValue)
            {
                continue;
            }

            var from = start.Value <= end.Value ? start.Value : end.Value;
            var to = start.Value <= end.Value ? end.Value : start.Value;
            if (date >= from && date <= to)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUnavailableAt(IEnumerable<ResourceUnavailabilityPeriod>? periods, DateOnly date, string? time)
    {
        if (!TimeOnly.TryParseExact((time ?? string.Empty).Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selectedTime))
        {
            return IsUnavailableOnDate(periods, date);
        }

        foreach (var period in periods ?? [])
        {
            var start = ParseDate(period.StartDate);
            var end = ParseDate(period.EndDate);
            if (!start.HasValue || !end.HasValue) continue;
            var from = start.Value <= end.Value ? start.Value : end.Value;
            var to = start.Value <= end.Value ? end.Value : start.Value;
            if (date < from || date > to) continue;

            var hasStartTime = TimeOnly.TryParseExact((period.StartTime ?? string.Empty).Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime);
            var hasEndTime = TimeOnly.TryParseExact((period.EndTime ?? string.Empty).Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime);
            if (from == to)
            {
                if ((!hasStartTime || selectedTime >= startTime) && (!hasEndTime || selectedTime <= endTime)) return true;
                continue;
            }

            if (date > from && date < to) return true;
            if (date == from && (!hasStartTime || selectedTime >= startTime)) return true;
            if (date == to && (!hasEndTime || selectedTime <= endTime)) return true;
        }

        return false;
    }
}
