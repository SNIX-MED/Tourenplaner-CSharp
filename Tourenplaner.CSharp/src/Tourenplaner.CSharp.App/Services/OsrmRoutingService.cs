using System.Net.Http;
using System.Text.Json;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed class OsrmRoutingService
{
    private static readonly HttpClient Client = CreateClient();

    public async Task<IReadOnlyList<IReadOnlyList<int>>?> TryBuildDurationMatrixMinutesAsync(
        IReadOnlyList<GeoPoint> stops,
        CancellationToken cancellationToken = default)
    {
        if (stops is null || stops.Count < 2)
        {
            return null;
        }

        var coordinates = string.Join(
            ";",
            stops.Select(x =>
                $"{x.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{x.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        var url = $"https://router.project-osrm.org/table/v1/driving/{coordinates}?annotations=duration";

        try
        {
            using var response = await Client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("durations", out var durations))
            {
                return null;
            }

            var matrix = new List<IReadOnlyList<int>>();
            foreach (var row in durations.EnumerateArray())
            {
                var minuteRow = new List<int>();
                foreach (var value in row.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Number)
                    {
                        minuteRow.Add(int.MaxValue / 4);
                        continue;
                    }

                    var seconds = value.GetDouble();
                    var minutes = Math.Max(0, (int)Math.Round(seconds / 60d, MidpointRounding.AwayFromZero));
                    minuteRow.Add(minutes);
                }

                matrix.Add(minuteRow);
            }

            return matrix;
        }
        catch
        {
            return null;
        }
    }

    public async Task<OsrmRouteResult> TryBuildRouteWithLegsAsync(
        IReadOnlyList<GeoPoint> stops,
        CancellationToken cancellationToken = default)
    {
        if (stops is null || stops.Count < 2)
        {
            return OsrmRouteResult.Empty;
        }

        var points = new List<GeoPoint>();
        var legs = new List<OsrmRouteLeg>();

        for (var i = 0; i < stops.Count - 1; i++)
        {
            var a = stops[i];
            var b = stops[i + 1];
            var segment = await TryBuildSegmentAsync(a, b, cancellationToken);
            if (segment.Points.Count < 2)
            {
                return OsrmRouteResult.Empty;
            }

            for (var p = 0; p < segment.Points.Count; p++)
            {
                // Avoid duplicating the shared endpoint between segments.
                if (i > 0 && p == 0)
                {
                    continue;
                }

                points.Add(segment.Points[p]);
            }

            legs.Add(new OsrmRouteLeg(segment.DurationMinutes, segment.DistanceKm));
        }

        return new OsrmRouteResult(points, legs);
    }

    public async Task<IReadOnlyList<GeoPoint>> TryBuildRouteAsync(
        IReadOnlyList<GeoPoint> stops,
        CancellationToken cancellationToken = default)
    {
        var details = await TryBuildRouteWithLegsAsync(stops, cancellationToken);
        return details.GeometryPoints;
    }

    private static async Task<OsrmSegmentResult> TryBuildSegmentAsync(
        GeoPoint start,
        GeoPoint end,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://router.project-osrm.org/route/v1/driving/{start.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{start.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)};{end.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{end.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}?overview=full&geometries=geojson";

        try
        {
            using var response = await Client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return OsrmSegmentResult.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            {
                return OsrmSegmentResult.Empty;
            }

            var route = routes[0];
            if (!route.TryGetProperty("geometry", out var geometry))
            {
                return OsrmSegmentResult.Empty;
            }

            if (!geometry.TryGetProperty("coordinates", out var coordinates))
            {
                return OsrmSegmentResult.Empty;
            }

            var points = new List<GeoPoint>();
            foreach (var coordinate in coordinates.EnumerateArray())
            {
                if (coordinate.GetArrayLength() < 2)
                {
                    continue;
                }

                var lon = coordinate[0].GetDouble();
                var lat = coordinate[1].GetDouble();
                points.Add(new GeoPoint(lat, lon));
            }

            var durationSec = route.TryGetProperty("duration", out var durationElement) ? durationElement.GetDouble() : 0d;
            var distanceMeter = route.TryGetProperty("distance", out var distanceElement) ? distanceElement.GetDouble() : 0d;
            var durationMinutes = Math.Max(0, (int)Math.Round(durationSec / 60d, MidpointRounding.AwayFromZero));
            var distanceKm = Math.Max(0d, distanceMeter / 1000d);

            return new OsrmSegmentResult(points, durationMinutes, distanceKm);
        }
        catch
        {
            return OsrmSegmentResult.Empty;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GAWELA-Tourenplaner/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

public sealed record OsrmRouteLeg(int DurationMinutes, double DistanceKm);

public sealed record OsrmRouteResult(
    IReadOnlyList<GeoPoint> GeometryPoints,
    IReadOnlyList<OsrmRouteLeg> Legs)
{
    public static OsrmRouteResult Empty { get; } = new(Array.Empty<GeoPoint>(), Array.Empty<OsrmRouteLeg>());
}

internal sealed record OsrmSegmentResult(
    IReadOnlyList<GeoPoint> Points,
    int DurationMinutes,
    double DistanceKm)
{
    public static OsrmSegmentResult Empty { get; } = new(Array.Empty<GeoPoint>(), 0, 0d);
}
