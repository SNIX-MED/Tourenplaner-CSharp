namespace Tourenplaner.CSharp.Domain.Models;

public sealed class ResourceUnavailabilityPeriod
{
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}
