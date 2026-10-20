using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using System.Text.Json;

namespace Tourenplaner.CSharp.App.Services;

public static class TourAddressReviewService
{
    public const string Warning = "Adresse geändert – Planung prüfen";

    internal static string DestinationSnapshot(IEnumerable<TourRecord> tours, IEnumerable<Order> orders)
    {
        var ids = tours.SelectMany(t => t.Stops).Select(s => s.Auftragsnummer?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(orders.Where(o => ids.Contains(o.Id.Trim())).OrderBy(o => o.Id)
            .Select(o => new { o.Id, o.DeliveryAddress, o.Location, o.ManualLocationRequiresReview, o.IsArchived,
                o.DeliveryType, o.IsAlternativeDeliveryEnabled, o.Deliveries }));
    }

    public static void Mark(TourRecord tour, IEnumerable<string> orderIds)
    {
        tour.AddressReviewOrderIds = tour.AddressReviewOrderIds.Concat(orderIds)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Cached times are keyed by stop IDs and would otherwise still describe the old destination.
        tour.TravelTimeCache.Clear();
        tour.TravelTimeProfileCache.Clear();
        tour.RouteGeometryPoints.Clear();
        tour.RouteGeometrySignature = string.Empty;
        tour.AddressReviewRoutingError = "Route muss nach der Adressänderung neu berechnet werden.";
    }

    public static async Task RecalculateAsync(TourRecord tour, IReadOnlyList<Order> orders,
        AppSettings settings, VehicleDataRecord vehicles, string userName,
        Func<IReadOnlyList<GeoPoint>, Task<OsrmRouteResult>>? routeBuilder = null)
    {
        tour.TravelTimeCache.Clear();
        tour.TravelTimeProfileCache.Clear();
        tour.RouteGeometryPoints.Clear();
        tour.RouteGeometrySignature = string.Empty;
        var ordered = tour.Stops.OrderBy(s => s.Order).ToList();
        var routed = ordered.Where(s => !string.Equals(s.StopKind, "pause", StringComparison.OrdinalIgnoreCase)).ToList();
        var orderById = orders.GroupBy(o => o.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var stop in routed)
        {
            if (!string.IsNullOrWhiteSpace(stop.Auftragsnummer) && !TourStopIdentity.IsCompanyStop(stop))
            {
                if (!orderById.TryGetValue(stop.Auftragsnummer.Trim(), out var order) ||
                    order.ManualLocationRequiresReview || order.Location is null ||
                    string.IsNullOrWhiteSpace(order.DeliveryAddress?.Street) ||
                    string.IsNullOrWhiteSpace(order.DeliveryAddress.PostalCode) ||
                    string.IsNullOrWhiteSpace(order.DeliveryAddress.City))
                {
                    tour.AddressReviewRoutingError = $"Auftrag {stop.Auftragsnummer}: Fahrtziel zuerst prüfen.";
                    return;
                }
                stop.Lat = order.Location.Latitude;
                stop.Lng = stop.Lon = order.Location.Longitude;
                stop.Address = OrderAddressFormatting.BuildAddressLine(order.DeliveryAddress.Street,
                    order.DeliveryAddress.HouseNumber, order.DeliveryAddress.PostalCode, order.DeliveryAddress.City);
            }
            if (!stop.Lat.HasValue || !(stop.Lng ?? stop.Lon).HasValue ||
                !double.IsFinite(stop.Lat.Value) || !double.IsFinite((stop.Lng ?? stop.Lon)!.Value) ||
                Math.Abs(stop.Lat.Value) > 90 || Math.Abs((stop.Lng ?? stop.Lon)!.Value) > 180)
            {
                tour.AddressReviewRoutingError = $"Stopp {stop.Name}: Koordinaten fehlen.";
                return;
            }
        }
        userName = !string.IsNullOrWhiteSpace(userName) ? userName.Trim() :
            !string.IsNullOrWhiteSpace(settings.CurrentUserName) ? settings.CurrentUserName.Trim() : Environment.UserName;
        var preference = settings.ResolveUserPreference(userName);
        settings.MapOverlayPreferencesByUser.TryGetValue(userName, out var overlay);
        overlay ??= new MapOverlayUserPreference();
        var vehicle = vehicles.Vehicles.FirstOrDefault(v => string.Equals(v.Id, tour.VehicleId, StringComparison.OrdinalIgnoreCase));
        var dimensions = vehicle?.ExternalDimensions;
        var hasVehicle = !string.IsNullOrWhiteSpace(tour.VehicleId) || !string.IsNullOrWhiteSpace(tour.SecondaryVehicleId);
        var hasTrailer = !string.IsNullOrWhiteSpace(tour.TrailerId) || !string.IsNullOrWhiteSpace(tour.SecondaryTrailerId);
        var speed = hasVehicle ? Math.Clamp(hasTrailer ? preference.TomTomVehicleWithTrailerMaxSpeedKmh : preference.TomTomVehicleOnlyMaxSpeedKmh, 1, 250) : 0;
        var profile = new TomTomRoutingProfile(TomTomRoutingMode.HeightAware,
            overlay.UseVehicleDimensions ? Math.Clamp((dimensions?.HeightCm ?? 0) / 100d, 0, 10) : 0,
            overlay.UseVehicleDimensions ? Math.Clamp((dimensions?.LengthCm ?? 0) / 100d, 0, 50) : 0,
            overlay.UseVehicleDimensions ? Math.Clamp((dimensions?.WidthCm ?? 0) / 100d, 0, 10) : 0,
            overlay.UseVehicleWeightRestrictions ? Math.Clamp(vehicle?.GrossWeightKg ?? 0, 0, 100000) : 0, speed,
            AppSettings.NormalizeTomTomTrafficSeverityMode(preference.TomTomTrafficSeverityMode) switch
            {
                "strict" => TomTomTrafficSeverityMode.Strict,
                "slightly_stricter" => TomTomTrafficSeverityMode.SlightlyStricter,
                _ => TomTomTrafficSeverityMode.Standard
            }, overlay.AvoidFerries);
        var schedule = new TourScheduleService(preference.TrafficBufferPercentFrom0500To0730,
            preference.TrafficBufferPercentFrom0730To0900, preference.TrafficBufferPercentFrom0900To1530,
            preference.TrafficBufferPercentFrom1530To1830);
        var departure = new DateTimeOffset(schedule.BuildSchedule(tour).Start);
        var points = routed.Select(s => new GeoPoint(s.Lat!.Value, (s.Lng ?? s.Lon)!.Value)).ToList();
        var result = points.Count < 2 ? OsrmRouteResult.Empty : routeBuilder is not null
            ? await routeBuilder(points)
            : await new TomTomRoutingService(settings.TomTomApiKey, profile).TryBuildRouteWithLegsAsync(points,
                overlay.UseDepartAtTraffic && departure > DateTimeOffset.Now ? departure : null);
        if (points.Count >= 2 && (result.Legs.Count != points.Count - 1 || result.GeometryPoints.Count < 2))
        {
            tour.AddressReviewRoutingError = "Routenberechnung fehlgeschlagen. Bitte erneut prüfen.";
            return;
        }
        tour.TravelTimeCache.Clear();
        tour.TravelTimeProfileCache.Clear();
        for (var i = 1; i < routed.Count; i++)
        {
            var fromIndex = ordered.IndexOf(routed[i - 1]);
            var toIndex = ordered.IndexOf(routed[i]);
            for (var j = fromIndex + 1; j <= toIndex; j++)
            {
                var leg = j == toIndex ? result.Legs[i - 1] : new OsrmRouteLeg(0, 0);
                var key = $"{ordered[j - 1].Id}|{ordered[j].Id}";
                tour.TravelTimeCache[key] = leg.RealisticDurationMinutes;
                tour.TravelTimeProfileCache[key] = new TourTravelTimeProfile
                {
                    OptimisticMinutes = leg.OptimisticDurationMinutes,
                    RealisticMinutes = leg.RealisticDurationMinutes,
                    PessimisticMinutes = leg.PessimisticDurationMinutes
                };
            }
        }
        tour.RouteGeometryPoints = result.GeometryPoints.ToList();
        tour.RouteGeometrySignature = string.Empty; // The map computes its own settings-sensitive signature.
        schedule.ApplySchedule(tour);
        tour.AddressReviewRoutingError = string.Empty;
    }
}
