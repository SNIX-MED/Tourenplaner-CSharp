namespace Tourenplaner.CSharp.Domain.Models;

public sealed class Tour
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly PlannedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string VehicleId { get; set; } = string.Empty;
    public List<string> OrderIds { get; set; } = new();
}
