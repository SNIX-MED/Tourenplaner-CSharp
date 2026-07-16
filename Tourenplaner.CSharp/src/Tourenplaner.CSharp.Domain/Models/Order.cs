namespace Tourenplaner.CSharp.Domain.Models;

public sealed class Order
{
    private static readonly IReadOnlyList<string> OrderStatusValues =
    [
        "nicht festgelegt",
        "Bestellt",
        "Unterwegs",
        "Teilweise Unterwegs",
        "Zu richten",
        "Teilweise zu richten",
        "Teilweise bereit",
        "Lieferbereit"
    ];

    public const string DefaultOrderStatus = "nicht festgelegt";
    public const string OrderedStatus = "Bestellt";
    public const string InTransitStatus = "Unterwegs";
    public const string PartiallyInTransitStatus = "Teilweise Unterwegs";
    public const string PartiallyReadyStatus = "Teilweise bereit";
    public const string ReadyToDeliverStatus = "Lieferbereit";
    public const string PendingPreparationStatus = "Zu richten";
    public const string PartiallyPendingPreparationStatus = "Teilweise zu richten";

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
    public bool IstVorauszahlung { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public string? ConcurrencyToken { get; set; }

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

        if (string.Equals(normalized, OrderProductInfo.PendingPreparationStatus, StringComparison.OrdinalIgnoreCase))
        {
            return PendingPreparationStatus;
        }

        if (string.Equals(normalized, PartiallyPendingPreparationStatus, StringComparison.OrdinalIgnoreCase))
        {
            return PartiallyPendingPreparationStatus;
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

        if (normalizedStatuses.Any(x =>
                string.Equals(x, OrderProductInfo.DefaultDeliveryStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return DefaultOrderStatus;
        }

        var allInStock = normalizedStatuses.All(x =>
            string.Equals(x, OrderProductInfo.InStockStatus, StringComparison.OrdinalIgnoreCase));
        if (allInStock)
        {
            return ReadyToDeliverStatus;
        }

        var allPendingPreparation = normalizedStatuses.All(x =>
            string.Equals(x, OrderProductInfo.PendingPreparationStatus, StringComparison.OrdinalIgnoreCase));
        if (allPendingPreparation)
        {
            return PendingPreparationStatus;
        }

        var hasPendingPreparation = normalizedStatuses.Any(x =>
            string.Equals(x, OrderProductInfo.PendingPreparationStatus, StringComparison.OrdinalIgnoreCase));
        var hasInStock = normalizedStatuses.Any(x =>
            string.Equals(x, OrderProductInfo.InStockStatus, StringComparison.OrdinalIgnoreCase));
        var hasReadyFamily = hasPendingPreparation || hasInStock;
        if (hasReadyFamily)
        {
            return hasPendingPreparation
                ? PartiallyPendingPreparationStatus
                : PartiallyReadyStatus;
        }

        var hasOnTheWay = normalizedStatuses.Any(x =>
            string.Equals(x, OrderProductInfo.InTransitStatus, StringComparison.OrdinalIgnoreCase));
        if (hasOnTheWay)
        {
            var hasNotOnTheWay = normalizedStatuses.Any(x =>
                !string.Equals(x, OrderProductInfo.InTransitStatus, StringComparison.OrdinalIgnoreCase));
            return hasNotOnTheWay
                ? PartiallyInTransitStatus
                : InTransitStatus;
        }

        if (normalizedStatuses.All(x =>
                string.Equals(x, OrderProductInfo.OrderedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return OrderedStatus;
        }

        return OrderedStatus;
    }

    public static string ResolvePartiallyPendingPreparationBaseStatus(IEnumerable<OrderProductInfo>? products)
    {
        var normalizedStatuses = (products ?? [])
            .Where(x => x is not null)
            .Select(x => OrderProductInfo.NormalizeDeliveryStatus(x.DeliveryStatus))
            .ToList();

        var nonPendingPreparationStatuses = normalizedStatuses
            .Where(x => !string.Equals(x, OrderProductInfo.PendingPreparationStatus, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nonPendingPreparationStatuses.Any(x =>
                string.Equals(x, OrderProductInfo.InStockStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return ReadyToDeliverStatus;
        }

        if (nonPendingPreparationStatuses.Any(x =>
                string.Equals(x, OrderProductInfo.InTransitStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return InTransitStatus;
        }

        if (nonPendingPreparationStatuses.Any(x =>
                string.Equals(x, OrderProductInfo.OrderedStatus, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, OrderProductInfo.DefaultDeliveryStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return OrderedStatus;
        }

        return PendingPreparationStatus;
    }
}

public sealed class OrderAddressInfo
{
    public string Name { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Additional { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public sealed class DeliveryAddressInfo
{
    public string Name { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Additional { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
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
        "Zu richten",
        "An Lager"
    ];

    public const string DefaultDeliveryStatus = "nicht festgelegt";
    public const string OrderedStatus = "Bestellt";
    public const string InTransitStatus = "Auf dem Weg";
    public const string PendingPreparationStatus = "Zu richten";
    public const string InStockStatus = "An Lager";

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

    public static bool IsReadyFamilyStatus(string? value)
    {
        var normalized = NormalizeDeliveryStatus(value);
        return string.Equals(normalized, InStockStatus, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, PendingPreparationStatus, StringComparison.OrdinalIgnoreCase);
    }
}
