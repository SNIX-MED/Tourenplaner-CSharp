namespace Tourenplaner.CSharp.Domain.Models;

public sealed class VehicleCombinationRecord
{
    public string Id { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string TrailerId { get; set; } = string.Empty;
    public int VehiclePayloadKg { get; set; }
    public int TrailerLoadKg { get; set; }
    public bool Active { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
