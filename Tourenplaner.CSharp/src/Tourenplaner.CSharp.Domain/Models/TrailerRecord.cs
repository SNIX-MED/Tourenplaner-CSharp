namespace Tourenplaner.CSharp.Domain.Models;

public sealed class TrailerRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public int MaxPayloadKg { get; set; }
    public bool Active { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public int VolumeM3 { get; set; }
    public VehicleDimensions? LoadingArea { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
