namespace Tourenplaner.CSharp.Domain.Models;

public sealed class PinRecord
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Status { get; set; } = "nicht festgelegt";
    public SqlOrderRecord Data { get; set; } = new();
}
