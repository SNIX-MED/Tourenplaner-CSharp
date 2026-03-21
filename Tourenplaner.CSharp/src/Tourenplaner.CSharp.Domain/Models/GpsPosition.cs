namespace Tourenplaner.CSharp.Domain.Models;

public sealed class GpsPosition
{
    public string VehicleId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public GeoPoint Position { get; set; } = new(0, 0);
    public double SpeedKmh { get; set; }
}
