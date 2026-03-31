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
    public OrderAddressInfo OrderAddress { get; set; } = new();
    public DeliveryAddressInfo DeliveryAddress { get; set; } = new();
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public List<OrderProductInfo> Products { get; set; } = new();
    public string DeliveryType { get; set; } = "Frei Bordsteinkante";
    public string OrderStatus { get; set; } = "nicht festgelegt";
    public string AvisoStatus { get; set; } = "nicht avisiert";
    public string Notes { get; set; } = string.Empty;
}

public sealed class OrderAddressInfo
{
    public string Name { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public sealed class DeliveryAddressInfo
{
    public string Name { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public sealed class OrderProductInfo
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public double UnitWeightKg { get; set; }
    public double WeightKg { get; set; }
    public string Dimensions { get; set; } = string.Empty;
}
