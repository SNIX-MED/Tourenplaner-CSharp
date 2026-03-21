namespace Tourenplaner.CSharp.Domain.Models;

public sealed class TourRecord
{
    public int Id { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<TourStopRecord> Stops { get; set; } = new();
    public List<string> EmployeeIds { get; set; } = new();
    public string StartTime { get; set; } = "08:00";
    public string RouteMode { get; set; } = "car";
    public string? VehicleId { get; set; }
    public string? TrailerId { get; set; }
    public Dictionary<string, int> TravelTimeCache { get; set; } = new();
}
