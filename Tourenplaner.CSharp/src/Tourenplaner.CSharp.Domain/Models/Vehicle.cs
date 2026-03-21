namespace Tourenplaner.CSharp.Domain.Models;

public sealed class Vehicle
{
    public string Id { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public int Capacity { get; set; }
}
