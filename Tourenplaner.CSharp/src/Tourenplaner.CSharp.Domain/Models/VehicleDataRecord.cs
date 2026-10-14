namespace Tourenplaner.CSharp.Domain.Models;

public sealed class VehicleDataRecord
{
    public string? ConcurrencyToken { get; set; }
    public List<Vehicle> Vehicles { get; set; } = new();
    public List<TrailerRecord> Trailers { get; set; } = new();
    public List<VehicleCombinationRecord> VehicleCombinations { get; set; } = new();
}
