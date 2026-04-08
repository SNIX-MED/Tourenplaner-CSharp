namespace Tourenplaner.CSharp.Domain.Models;

public sealed class Employee
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public List<ResourceUnavailabilityPeriod> UnavailabilityPeriods { get; set; } = new();
}
