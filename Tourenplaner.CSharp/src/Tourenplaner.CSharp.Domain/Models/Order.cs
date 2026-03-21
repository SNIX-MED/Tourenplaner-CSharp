namespace Tourenplaner.CSharp.Domain.Models;

public sealed class Order
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateOnly ScheduledDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public OrderType Type { get; set; } = OrderType.Map;
    public GeoPoint? Location { get; set; }
    public string? AssignedTourId { get; set; }
}
