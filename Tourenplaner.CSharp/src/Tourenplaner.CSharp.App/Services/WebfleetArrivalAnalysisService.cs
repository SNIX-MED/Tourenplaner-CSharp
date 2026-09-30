using System.Globalization;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed record WebfleetStopArrivalAnalysis(
    string StopId,
    string StopName,
    DateTimeOffset? ActualArrival,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd)
{
    public bool? ArrivedWithinWindow => ActualArrival.HasValue && WindowStart.HasValue && WindowEnd.HasValue
        ? ActualArrival >= WindowStart && ActualArrival <= WindowEnd
        : null;
}

public static class WebfleetArrivalAnalysisService
{
    public const double DefaultArrivalRadiusMeters = 150d;

    public static IReadOnlyList<WebfleetStopArrivalAnalysis> Analyze(
        TourRecord tour,
        IReadOnlyList<WebfleetTrackPoint> trackPoints,
        DateOnly tourDate,
        double arrivalRadiusMeters = DefaultArrivalRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(tour);
        ArgumentNullException.ThrowIfNull(trackPoints);
        if (arrivalRadiusMeters <= 0) throw new ArgumentOutOfRangeException(nameof(arrivalRadiusMeters));

        var points = trackPoints.OrderBy(point => point.PositionTime).ToList();
        var searchAfter = DateTimeOffset.MinValue;
        var results = new List<WebfleetStopArrivalAnalysis>();

        foreach (var stop in (tour.Stops ?? [])
                     .Where(IsAnalyzableStop)
                     .OrderBy(stop => stop.Order > 0 ? stop.Order : int.MaxValue))
        {
            var windowStart = ParseTourTime(tourDate, stop.PlannedArrivalOptimistic);
            var windowEnd = ParseTourTime(tourDate, stop.PlannedArrivalPessimistic);
            var candidateFrom = windowStart?.AddHours(-3) ?? new DateTimeOffset(tourDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
            var candidateTo = windowEnd?.AddHours(3) ?? candidateFrom.AddDays(1);
            var actualArrival = points.FirstOrDefault(point =>
                point.PositionTime > searchAfter &&
                point.PositionTime >= candidateFrom &&
                point.PositionTime <= candidateTo &&
                DistanceMeters(stop.Lat!.Value, stop.Lon ?? stop.Lng!.Value, point.Latitude, point.Longitude) <= arrivalRadiusMeters)?.PositionTime;

            if (actualArrival.HasValue)
            {
                searchAfter = actualArrival.Value;
            }

            results.Add(new WebfleetStopArrivalAnalysis(stop.Id, stop.Name, actualArrival, windowStart, windowEnd));
        }

        return results;
    }

    public static string BuildSummary(IReadOnlyList<WebfleetStopArrivalAnalysis> results)
    {
        if (results.Count == 0) return "Keine Kundenstopps mit Koordinaten für die Ankunftsanalyse gefunden.";

        var evaluated = results.Where(result => result.ArrivedWithinWindow.HasValue).ToList();
        var within = evaluated.Count(result => result.ArrivedWithinWindow == true);
        var details = results.Select(result =>
        {
            var name = string.IsNullOrWhiteSpace(result.StopName) ? result.StopId : result.StopName;
            if (!result.ActualArrival.HasValue) return $"{name}: keine Ankunft erkannt";
            if (!result.ArrivedWithinWindow.HasValue) return $"{name}: {result.ActualArrival:HH:mm}, kein Sollfenster";
            return $"{name}: {result.ActualArrival:HH:mm} {(result.ArrivedWithinWindow == true ? "innerhalb" : "ausserhalb")}";
        });

        return $"Ankunftsanalyse: {within}/{evaluated.Count} innerhalb des Zeitfensters. {string.Join(" · ", details)}";
    }

    private static bool IsAnalyzableStop(TourStopRecord stop)
    {
        var kind = (stop.StopKind ?? string.Empty).Trim();
        return !TourStopIdentity.IsCompanyStop(stop) &&
               !string.Equals(kind, "pause", StringComparison.OrdinalIgnoreCase) &&
               stop.Lat.HasValue && (stop.Lon.HasValue || stop.Lng.HasValue);
    }

    private static DateTimeOffset? ParseTourTime(DateOnly date, string? text)
    {
        return TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? new DateTimeOffset(date.ToDateTime(time, DateTimeKind.Local))
            : null;
    }

    private static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMeters = 6_371_000d;
        var dLat = DegreesToRadians(latitude2 - latitude1);
        var dLon = DegreesToRadians(longitude2 - longitude1);
        var lat1 = DegreesToRadians(latitude1);
        var lat2 = DegreesToRadians(latitude2);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
