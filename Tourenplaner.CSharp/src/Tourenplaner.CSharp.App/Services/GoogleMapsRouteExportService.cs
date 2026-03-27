using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class GoogleMapsRouteExportService
{
    private const int MaxSupportedPoints = 25;

    public static bool TryBuildUrl(IReadOnlyList<GeoPoint>? points, out string url, out string error)
    {
        url = string.Empty;
        error = string.Empty;

        var validPoints = (points ?? Array.Empty<GeoPoint>()).ToList();
        if (validPoints.Count < 2)
        {
            error = "Für den Export werden mindestens zwei gültige Stopps mit Koordinaten benötigt.";
            return false;
        }

        if (validPoints.Count > MaxSupportedPoints)
        {
            error = $"Google Maps unterstützt höchstens {MaxSupportedPoints} Punkte pro Route. Die aktuelle Tour enthält {validPoints.Count} Punkte.";
            return false;
        }

        var origin = Format(validPoints[0]);
        var destination = Format(validPoints[^1]);
        url = $"https://www.google.com/maps/dir/?api=1&origin={Uri.EscapeDataString(origin)}&destination={Uri.EscapeDataString(destination)}&travelmode=driving";

        if (validPoints.Count > 2)
        {
            var waypoints = string.Join("|", validPoints.Skip(1).Take(validPoints.Count - 2).Select(Format));
            if (!string.IsNullOrWhiteSpace(waypoints))
            {
                url += $"&waypoints={Uri.EscapeDataString(waypoints)}";
            }
        }

        return true;
    }

    private static string Format(GeoPoint point)
    {
        return $"{point.Latitude.ToString(CultureInfo.InvariantCulture)},{point.Longitude.ToString(CultureInfo.InvariantCulture)}";
    }
}
