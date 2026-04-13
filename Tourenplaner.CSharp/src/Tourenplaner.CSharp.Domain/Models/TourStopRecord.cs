namespace Tourenplaner.CSharp.Domain.Models;

public sealed class TourStopRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Auftragsnummer { get; set; } = string.Empty;
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public double? Lng { get; set; }
    public int Order { get; set; }
    public string TimeWindowStart { get; set; } = string.Empty;
    public string TimeWindowEnd { get; set; } = string.Empty;
    public int ServiceMinutes { get; set; }
    public string PlannedArrival { get; set; } = string.Empty;
    public string PlannedDeparture { get; set; } = string.Empty;
    public int WaitMinutes { get; set; }
    public bool ScheduleConflict { get; set; }
    public string ScheduleConflictText { get; set; } = string.Empty;
    public string Gewicht { get; set; } = string.Empty;
    public string EmployeeInfoText { get; set; } = string.Empty;
}
