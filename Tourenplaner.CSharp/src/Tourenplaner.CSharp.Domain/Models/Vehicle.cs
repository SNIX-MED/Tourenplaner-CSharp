namespace Tourenplaner.CSharp.Domain.Models;

public sealed class Vehicle
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "other";
    public string Name { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public int MaxPayloadKg { get; set; }
    public int MaxTrailerLoadKg { get; set; }
    public bool Active { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public int VolumeM3 { get; set; }
    public VehicleDimensions? LoadingArea { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public List<ResourceUnavailabilityPeriod> UnavailabilityPeriods { get; set; } = new();
}
