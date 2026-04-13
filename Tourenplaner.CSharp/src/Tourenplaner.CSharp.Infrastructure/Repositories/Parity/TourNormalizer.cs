using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

internal static class TourNormalizer
{
    private static readonly string[] AcceptedFormats =
    [
        "yyyy-MM-dd",
        "dd-MM-yyyy",
        "dd.MM.yyyy",
        "dd/MM/yyyy"
    ];

    public static TourRecord NormalizeTour(TourRecord source)
    {
        source.Stops = source.Stops
            .Where(x => x is not null)
            .Select((stop, index) => NormalizeStop(stop, index + 1))
            .ToList();

        source.Date = NormalizeDateString(source.Date);
        source.Name = (source.Name ?? string.Empty).Trim();
        source.StartTime = string.IsNullOrWhiteSpace(source.StartTime) ? "08:00" : source.StartTime.Trim();
        source.RouteMode = string.IsNullOrWhiteSpace(source.RouteMode) ? "car" : source.RouteMode.Trim();
        source.VehicleId = string.IsNullOrWhiteSpace(source.VehicleId) ? null : source.VehicleId.Trim();
        source.TrailerId = string.IsNullOrWhiteSpace(source.TrailerId) ? null : source.TrailerId.Trim();
        source.SecondaryVehicleId = string.IsNullOrWhiteSpace(source.SecondaryVehicleId) ? null : source.SecondaryVehicleId.Trim();
        source.SecondaryTrailerId = string.IsNullOrWhiteSpace(source.SecondaryTrailerId) ? null : source.SecondaryTrailerId.Trim();
        source.EmployeeIds = source.EmployeeIds
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .Take(2)
            .ToList();

        source.TravelTimeCache = source.TravelTimeCache
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value);

        return source;
    }

    public static TourStopRecord NormalizeStop(TourStopRecord source, int order)
    {
        source.Id = string.IsNullOrWhiteSpace(source.Id)
            ? StopIdFromLegacy(source)
            : source.Id.Trim();
        source.Name = (source.Name ?? string.Empty).Trim();
        source.Address = (source.Address ?? string.Empty).Trim();
        source.Order = source.Order > 0 ? source.Order : order;
        source.Lng ??= source.Lon;
        source.Lon ??= source.Lng;
        source.TimeWindowStart = (source.TimeWindowStart ?? string.Empty).Trim();
        source.TimeWindowEnd = (source.TimeWindowEnd ?? string.Empty).Trim();
        source.PlannedArrival = (source.PlannedArrival ?? string.Empty).Trim();
        source.PlannedDeparture = (source.PlannedDeparture ?? string.Empty).Trim();
        source.ScheduleConflictText = (source.ScheduleConflictText ?? string.Empty).Trim();
        source.Gewicht = (source.Gewicht ?? string.Empty).Trim();
        source.EmployeeInfoText = (source.EmployeeInfoText ?? string.Empty).Trim();

        if (source.ServiceMinutes < 0)
        {
            source.ServiceMinutes = 0;
        }

        if (source.WaitMinutes < 0)
        {
            source.WaitMinutes = 0;
        }

        return source;
    }

    public static string NormalizeDateString(string? value)
    {
        var parsed = ParseDate(value);
        return parsed?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static DateTime? ParseDate(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var format in AcceptedFormats)
        {
            if (DateTime.TryParseExact(raw, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.Date;
            }
        }

        return null;
    }

    private static string StopIdFromLegacy(TourStopRecord stop)
    {
        if (!string.IsNullOrWhiteSpace(stop.Auftragsnummer))
        {
            return $"auftrag:{stop.Auftragsnummer.Trim()}";
        }

        if (stop.Lat.HasValue && stop.Lng.HasValue)
        {
            return $"coord:{Math.Round(stop.Lat.Value, 6)}:{Math.Round(stop.Lng.Value, 6)}";
        }

        return Guid.NewGuid().ToString();
    }
}
