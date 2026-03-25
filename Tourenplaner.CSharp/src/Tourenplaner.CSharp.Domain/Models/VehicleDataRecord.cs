namespace Tourenplaner.CSharp.Domain.Models;

public sealed class VehicleDataRecord
{
    public List<Vehicle> Vehicles { get; set; } = new();
    public List<TrailerRecord> Trailers { get; set; } = new();
    public List<VehicleCombinationRecord> VehicleCombinations { get; set; } = new();
}
