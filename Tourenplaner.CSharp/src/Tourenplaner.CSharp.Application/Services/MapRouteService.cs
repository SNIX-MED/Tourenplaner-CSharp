using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class MapRouteService
{
    public int DetermineNextTourId(IEnumerable<TourRecord>? tours)
    {
        var maxId = (tours ?? Array.Empty<TourRecord>())
            .Select(t => t?.Id ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        return maxId < 1 ? 1 : maxId + 1;
    }

    public IReadOnlyList<MapRouteStop> SwapStops(
        IReadOnlyList<MapRouteStop>? stops,
        string sourceOrderId,
        string targetOrderId)
    {
        var ordered = Normalize(stops);
        if (string.IsNullOrWhiteSpace(sourceOrderId) || string.IsNullOrWhiteSpace(targetOrderId))
        {
            return ordered;
        }

        var sourceIndex = ordered.FindIndex(x => string.Equals(x.OrderId, sourceOrderId, StringComparison.OrdinalIgnoreCase));
        var targetIndex = ordered.FindIndex(x => string.Equals(x.OrderId, targetOrderId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return ordered;
        }

        var moved = ordered[sourceIndex];
        ordered.RemoveAt(sourceIndex);
        ordered.Insert(targetIndex, moved);
        return Reindex(ordered);
    }

    public IReadOnlyList<MapRouteStop> MoveStop(
        IReadOnlyList<MapRouteStop>? stops,
        string orderId,
        int delta)
    {
        var ordered = Normalize(stops);
        if (string.IsNullOrWhiteSpace(orderId) || delta == 0)
        {
            return ordered;
        }

        var sourceIndex = ordered.FindIndex(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
        {
            return ordered;
        }

        var targetIndex = sourceIndex + delta;
        if (targetIndex < 0 || targetIndex >= ordered.Count)
        {
            return ordered;
        }

        var moved = ordered[sourceIndex];
        ordered.RemoveAt(sourceIndex);
        ordered.Insert(targetIndex, moved);
        return Reindex(ordered);
    }

    public TourRecord BuildTour(
        IReadOnlyList<MapRouteStop>? routeStops,
        int nextTourId,
        string? routeName,
        string? routeDate,
        string? routeStartTime,
        string? companyName = null,
        string? companyAddress = null,
        GeoPoint? companyLocation = null,
        int defaultServiceMinutes = 10)
    {
        var ordered = Normalize(routeStops);
        if (ordered.Count == 0)
        {
            throw new ArgumentException("Route must contain at least one stop.", nameof(routeStops));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var normalizedDate = TryParseTourDate(routeDate, out var parsedDate)
            ? parsedDate.ToString("dd.MM.yyyy")
            : today.ToString("dd.MM.yyyy");

        var normalizedStartTime = string.IsNullOrWhiteSpace(routeStartTime) ? "08:00" : routeStartTime.Trim();
        var normalizedName = string.IsNullOrWhiteSpace(routeName) ? $"Karte Tour {normalizedDate}" : routeName.Trim();
        var safeServiceMinutes = defaultServiceMinutes < 0 ? 0 : defaultServiceMinutes;

        var normalizedCompanyName = string.IsNullOrWhiteSpace(companyName) ? "Firma" : companyName.Trim();
        var normalizedCompanyAddress = string.IsNullOrWhiteSpace(companyAddress) ? "Firmenadresse nicht gesetzt" : companyAddress.Trim();
        var tourStops = new List<TourStopRecord>
        {
            new()
            {
                Id = TourStopIdentity.CompanyStartStopId,
                Auftragsnummer = TourStopIdentity.CompanyStartOrderNumber,
                Name = $"{normalizedCompanyName} (Start)",
                Address = normalizedCompanyAddress,
                Order = 1,
                Lat = companyLocation?.Latitude,
                Lon = companyLocation?.Longitude,
                Lng = companyLocation?.Longitude,
                ServiceMinutes = 0
            }
        };

        tourStops.AddRange(ordered.Select(x => new TourStopRecord
        {
            Id = $"auftrag:{x.OrderId}",
            Auftragsnummer = x.OrderId,
            Name = x.Customer,
            Address = x.Address,
            Order = x.Position + 1,
            Lat = x.Latitude,
            Lon = x.Longitude,
            Lng = x.Longitude,
            ServiceMinutes = x.ServiceMinutes < 0 ? safeServiceMinutes : x.ServiceMinutes
        }));

        tourStops.Add(new TourStopRecord
        {
            Id = TourStopIdentity.CompanyEndStopId,
            Auftragsnummer = TourStopIdentity.CompanyEndOrderNumber,
            Name = $"{normalizedCompanyName} (Ende)",
            Address = normalizedCompanyAddress,
            Order = tourStops.Count + 1,
            Lat = companyLocation?.Latitude,
            Lon = companyLocation?.Longitude,
            Lng = companyLocation?.Longitude,
            ServiceMinutes = 0
        });

        return new TourRecord
        {
            Id = nextTourId,
            Name = normalizedName,
            Date = normalizedDate,
            StartTime = normalizedStartTime,
            RouteMode = "car",
            Stops = tourStops
        };
    }

    public IReadOnlySet<string> ExtractRouteOrderIds(IReadOnlyList<MapRouteStop>? routeStops)
    {
        return Normalize(routeStops)
            .Select(x => x.OrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParseTourDate(string? value, out DateOnly result)
    {
        return DateOnly.TryParseExact(value?.Trim(), "dd.MM.yyyy", out result) ||
               DateOnly.TryParse(value?.Trim(), out result);
    }

    private static List<MapRouteStop> Normalize(IReadOnlyList<MapRouteStop>? stops)
    {
        return Reindex((stops ?? Array.Empty<MapRouteStop>())
            .Where(x => x is not null)
            .OrderBy(x => x.Position)
            .ToList());
    }

    private static List<MapRouteStop> Reindex(List<MapRouteStop> stops)
    {
        for (var i = 0; i < stops.Count; i++)
        {
            var x = stops[i];
            stops[i] = x with { Position = i + 1 };
        }

        return stops;
    }
}
