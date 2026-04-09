namespace Tourenplaner.CSharp.Domain.Models;

public sealed class Order
{
    private static readonly IReadOnlyList<string> OrderStatusValues =
    [
        "nicht festgelegt",
        "Bestellt",
        "Unterwegs",
        "Teilweise Unterwegs",
        "Teilweise bereit",
        "Lieferbereit"
    ];

    public const string DefaultOrderStatus = "nicht festgelegt";
    public const string OrderedStatus = "Bestellt";
    public const string InTransitStatus = "Unterwegs";
    public const string PartiallyInTransitStatus = "Teilweise Unterwegs";
    public const string PartiallyReadyStatus = "Teilweise bereit";
    public const string ReadyToDeliverStatus = "Lieferbereit";

    public static IReadOnlyList<string> OrderStatusOptions => OrderStatusValues;

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
    public string OrderStatus { get; set; } = DefaultOrderStatus;
    public string AvisoStatus { get; set; } = "nicht avisiert";
    public string Notes { get; set; } = string.Empty;

    public static string NormalizeOrderStatus(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultOrderStatus;
        }

        if (string.Equals(normalized, "Auf dem Weg", StringComparison.OrdinalIgnoreCase))
        {
            return InTransitStatus;
        }

        if (string.Equals(normalized, "An Lager", StringComparison.OrdinalIgnoreCase))
        {
            return ReadyToDeliverStatus;
        }

        var match = OrderStatusValues.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        return match ?? DefaultOrderStatus;
    }

    public static string ResolveOrderStatusFromProducts(IEnumerable<OrderProductInfo>? products)
    {
        var normalizedStatuses = (products ?? [])
            .Where(x => x is not null)
            .Select(x => OrderProductInfo.NormalizeDeliveryStatus(x.DeliveryStatus))
            .ToList();

        if (normalizedStatuses.Count == 0)
        {
            return DefaultOrderStatus;
        }

        var allInStock = normalizedStatuses.All(x =>
            string.Equals(x, "An Lager", StringComparison.OrdinalIgnoreCase));
        if (allInStock)
        {
            return ReadyToDeliverStatus;
        }

        var hasInStock = normalizedStatuses.Any(x =>
            string.Equals(x, "An Lager", StringComparison.OrdinalIgnoreCase));
        if (hasInStock)
        {
            return PartiallyReadyStatus;
        }

        var hasOnTheWay = normalizedStatuses.Any(x =>
            string.Equals(x, "Auf dem Weg", StringComparison.OrdinalIgnoreCase));
        if (hasOnTheWay)
        {
            var hasNotOnTheWay = normalizedStatuses.Any(x =>
                !string.Equals(x, "Auf dem Weg", StringComparison.OrdinalIgnoreCase));
            return hasNotOnTheWay
                ? PartiallyInTransitStatus
                : InTransitStatus;
        }

        if (normalizedStatuses.All(x =>
                string.Equals(x, "Bestellt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, "nicht festgelegt", StringComparison.OrdinalIgnoreCase)))
        {
            return OrderedStatus;
        }

        return OrderedStatus;
    }
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
    private static readonly IReadOnlyList<string> DeliveryStatusValues =
    [
        "nicht festgelegt",
        "Bestellt",
        "Auf dem Weg",
        "An Lager"
    ];

    public const string DefaultDeliveryStatus = "nicht festgelegt";

    public static IReadOnlyList<string> DeliveryStatusOptions => DeliveryStatusValues;

    public string Name { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public double UnitWeightKg { get; set; }
    public double WeightKg { get; set; }
    public string Dimensions { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = DefaultDeliveryStatus;

    public static string NormalizeDeliveryStatus(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultDeliveryStatus;
        }

        var match = DeliveryStatusValues.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        return match ?? DefaultDeliveryStatus;
    }
}
