using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed class TomTomRoutingService
{
    private static readonly HttpClient Client = CreateClient();
    private readonly string _apiKey;
    private readonly TomTomRoutingProfile _profile;

    public TomTomRoutingService(string? apiKey, TomTomRoutingProfile? profile = null)
    {
        _apiKey = (apiKey ?? string.Empty).Trim();
        _profile = profile ?? TomTomRoutingProfile.Default;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<IReadOnlyList<int>>?> TryBuildDurationMatrixMinutesAsync(
        IReadOnlyList<GeoPoint> stops,
        DateTimeOffset? departAt = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || stops is null || stops.Count < 2)
        {
            return null;
        }

        var matrix = new List<IReadOnlyList<int>>(stops.Count);
        for (var i = 0; i < stops.Count; i++)
        {
            var row = new List<int>(stops.Count);
            for (var j = 0; j < stops.Count; j++)
            {
                if (i == j)
                {
                    row.Add(0);
                    continue;
                }

                var route = await TryBuildRouteWithLegsAsync([stops[i], stops[j]], departAt, cancellationToken);
                row.Add(route.Legs.Count == 1 ? route.Legs[0].DurationMinutes : int.MaxValue / 4);
            }

            matrix.Add(row);
        }

        return matrix;
    }

    public async Task<OsrmRouteResult> TryBuildRouteWithLegsAsync(
        IReadOnlyList<GeoPoint> stops,
        DateTimeOffset? departAt = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || stops is null || stops.Count < 2)
        {
            return OsrmRouteResult.Empty;
        }

        var routeQuery = string.Join(":", stops.Select(FormatPoint));
        var url =
            $"https://api.tomtom.com/routing/1/calculateRoute/{routeQuery}/json?key={Uri.EscapeDataString(_apiKey)}&traffic=true&travelMode=car&routeType=fastest&computeTravelTimeFor=all&sectionType=traffic";
        if (_profile.Mode == TomTomRoutingMode.HeightAware)
        {
            if (_profile.VehicleLengthMeters > 0d)
            {
                var length = _profile.VehicleLengthMeters.ToString("0.##", CultureInfo.InvariantCulture);
                url += $"&vehicleLength={Uri.EscapeDataString(length)}";
            }

            if (_profile.VehicleWidthMeters > 0d)
            {
                var width = _profile.VehicleWidthMeters.ToString("0.##", CultureInfo.InvariantCulture);
                url += $"&vehicleWidth={Uri.EscapeDataString(width)}";
            }

            if (_profile.VehicleHeightMeters > 0d)
            {
                var height = _profile.VehicleHeightMeters.ToString("0.##", CultureInfo.InvariantCulture);
                url += $"&vehicleHeight={Uri.EscapeDataString(height)}";
            }

            if (_profile.VehicleWeightKg > 0)
            {
                var weight = _profile.VehicleWeightKg.ToString(CultureInfo.InvariantCulture);
                url += $"&vehicleWeight={Uri.EscapeDataString(weight)}";
            }
        }

        if (departAt.HasValue)
        {
            var departAtIso = departAt.Value.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
            url += $"&departAt={Uri.EscapeDataString(departAtIso)}";
        }

        try
        {
            using var response = await Client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return OsrmRouteResult.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("routes", out var routes) ||
                routes.ValueKind != JsonValueKind.Array ||
                routes.GetArrayLength() == 0)
            {
                return OsrmRouteResult.Empty;
            }

            var route = routes[0];
            if (!route.TryGetProperty("legs", out var legsElement) || legsElement.ValueKind != JsonValueKind.Array)
            {
                return OsrmRouteResult.Empty;
            }

            var geometry = new List<GeoPoint>();
            var legs = new List<OsrmRouteLeg>();
            var trafficSegments = new List<OsrmRouteTrafficSegment>();
            var legIndex = 0;
            foreach (var leg in legsElement.EnumerateArray())
            {
                var durationMinutes = 0;
                var distanceKm = 0d;
                if (leg.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
                {
                    if (summary.TryGetProperty("travelTimeInSeconds", out var sec) && sec.ValueKind == JsonValueKind.Number)
                    {
                        durationMinutes = Math.Max(0, (int)Math.Round(sec.GetDouble() / 60d, MidpointRounding.AwayFromZero));
                    }

                    if (summary.TryGetProperty("lengthInMeters", out var meters) && meters.ValueKind == JsonValueKind.Number)
                    {
                        distanceKm = Math.Max(0d, meters.GetDouble() / 1000d);
                    }
                }

                legs.Add(new OsrmRouteLeg(durationMinutes, distanceKm));

                if (!leg.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
                {
                    legIndex++;
                    continue;
                }

                var pointIndex = 0;
                foreach (var point in points.EnumerateArray())
                {
                    if (!point.TryGetProperty("latitude", out var latElement) ||
                        !point.TryGetProperty("longitude", out var lonElement) ||
                        latElement.ValueKind != JsonValueKind.Number ||
                        lonElement.ValueKind != JsonValueKind.Number)
                    {
                        pointIndex++;
                        continue;
                    }

                    // Skip duplicated waypoint at leg boundary.
                    if (legIndex > 0 && pointIndex == 0)
                    {
                        pointIndex++;
                        continue;
                    }

                    geometry.Add(new GeoPoint(latElement.GetDouble(), lonElement.GetDouble()));
                    pointIndex++;
                }

                legIndex++;
            }

            if (route.TryGetProperty("sections", out var sectionsElement) && sectionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sectionsElement.EnumerateArray())
                {
                    if (!section.TryGetProperty("sectionType", out var sectionTypeElement) ||
                        sectionTypeElement.ValueKind != JsonValueKind.String ||
                        !string.Equals(sectionTypeElement.GetString(), "TRAFFIC", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!section.TryGetProperty("startPointIndex", out var startIndexElement) ||
                        !section.TryGetProperty("endPointIndex", out var endIndexElement) ||
                        startIndexElement.ValueKind != JsonValueKind.Number ||
                        endIndexElement.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    var startIndex = startIndexElement.GetInt32();
                    var endIndex = endIndexElement.GetInt32();
                    if (startIndex < 0 || endIndex <= startIndex || geometry.Count < 2)
                    {
                        continue;
                    }

                    var maxPointIndex = geometry.Count - 1;
                    var clampedStart = Math.Min(maxPointIndex - 1, Math.Max(0, startIndex));
                    var clampedEnd = Math.Min(maxPointIndex, Math.Max(clampedStart + 1, endIndex));
                    if (clampedEnd <= clampedStart)
                    {
                        continue;
                    }

                    var trafficLevel = "unknown";
                    if (section.TryGetProperty("simpleCategory", out var categoryElement) && categoryElement.ValueKind == JsonValueKind.String)
                    {
                        var rawCategory = (categoryElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                        var normalizedCategory = rawCategory
                            .Replace("_", string.Empty, StringComparison.Ordinal)
                            .Replace("-", string.Empty, StringComparison.Ordinal)
                            .Replace(" ", string.Empty, StringComparison.Ordinal);
                        trafficLevel = normalizedCategory switch
                        {
                            "roadclosure" => "blocked",
                            "closed" => "blocked",
                            "jam" => "heavy",
                            "jammed" => "heavy",
                            "stationary" => "heavy",
                            "heavilycongested" => "heavy",
                            "congested" => "moderate",
                            "stopandgo" => "moderate",
                            "slow" => "light",
                            "freeflow" => "freeFlow",
                            "open" => "freeFlow",
                            "none" => "unknown",
                            _ => "unknown"
                        };
                    }

                    if (string.Equals(trafficLevel, "unknown", StringComparison.OrdinalIgnoreCase) &&
                        section.TryGetProperty("delayInSeconds", out var delaySecondsElement) &&
                        delaySecondsElement.ValueKind == JsonValueKind.Number)
                    {
                        var delaySeconds = delaySecondsElement.GetDouble();
                        trafficLevel = delaySeconds switch
                        {
                            <= 10d => "freeFlow",
                            <= 60d => "light",
                            <= 180d => "moderate",
                            <= 480d => "heavy",
                            _ => "blocked"
                        };
                    }

                    if (string.Equals(trafficLevel, "unknown", StringComparison.OrdinalIgnoreCase) &&
                        section.TryGetProperty("magnitudeOfDelay", out var delayMagnitudeElement) &&
                        delayMagnitudeElement.ValueKind == JsonValueKind.Number)
                    {
                        var magnitude = delayMagnitudeElement.GetInt32();
                        trafficLevel = magnitude switch
                        {
                            <= 0 => "freeFlow",
                            1 => "light",
                            2 => "moderate",
                            3 => "heavy",
                            _ => "heavy"
                        };
                    }

                    if (string.Equals(trafficLevel, "unknown", StringComparison.OrdinalIgnoreCase) &&
                        section.TryGetProperty("effectiveSpeedInKmh", out var speedElement) && speedElement.ValueKind == JsonValueKind.Number)
                    {
                        // Last-resort fallback only: city routes can have low absolute speed without traffic jam.
                        var speed = speedElement.GetDouble();
                        trafficLevel = speed switch
                        {
                            >= 70 => "freeFlow",
                            >= 40 => "light",
                            >= 20 => "moderate",
                            _ => "heavy"
                        };
                    }

                    trafficSegments.Add(new OsrmRouteTrafficSegment(clampedStart, clampedEnd, trafficLevel));
                }
            }

            if (legs.Count != stops.Count - 1 || geometry.Count < 2)
            {
                return OsrmRouteResult.Empty;
            }

            return new OsrmRouteResult(geometry, legs, trafficSegments);
        }
        catch
        {
            return OsrmRouteResult.Empty;
        }
    }

    private static string FormatPoint(GeoPoint point)
    {
        return $"{point.Latitude.ToString(CultureInfo.InvariantCulture)},{point.Longitude.ToString(CultureInfo.InvariantCulture)}";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GAWELA-Tourenplaner/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

public enum TomTomRoutingMode
{
    Car = 0,
    HeightAware = 1
}

public sealed record TomTomRoutingProfile(
    TomTomRoutingMode Mode,
    double VehicleHeightMeters,
    double VehicleLengthMeters = 0d,
    double VehicleWidthMeters = 0d,
    int VehicleWeightKg = 0)
{
    public static TomTomRoutingProfile Default { get; } = new(TomTomRoutingMode.Car, 0d);
}
