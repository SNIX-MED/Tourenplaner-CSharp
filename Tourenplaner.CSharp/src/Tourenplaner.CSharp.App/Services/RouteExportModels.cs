using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public enum RouteExportOption
{
    GoogleMaps,
    Pdf
}

public sealed record RouteExportCompanyInfo(string Name, string Address, double Latitude, double Longitude);

public sealed record RouteExportStopInfo(
    int Position,
    string Label,
    string Name,
    string Address,
    string OrderNumber,
    double Latitude,
    double Longitude,
    string TimeWindow,
    string Arrival,
    string WeightText);

public sealed record RouteExportSnapshot(
    string TourName,
    string TourDate,
    string StartTime,
    string? VehicleLabel,
    string? TrailerLabel,
    IReadOnlyList<RouteExportStopInfo> Stops,
    IReadOnlyList<GeoPoint> GoogleMapsPoints,
    IReadOnlyList<GeoPoint> GeometryPoints,
    RouteExportCompanyInfo? Company);

public sealed record RoutePdfExportResult(bool Succeeded, bool Cancelled, string Message)
{
    public static RoutePdfExportResult Success(string message) => new(true, false, message);

    public static RoutePdfExportResult Failure(string message) => new(false, false, message);

    public static RoutePdfExportResult UserCancelled() => new(false, true, string.Empty);
}
