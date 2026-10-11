using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class ProductStatusVisualService
{
    private const string PendingPreparationProductColor = "#16A34A";
    private static string _notSpecified = AppSettings.DefaultStatusColorNotSpecified;
    private static string _ordered = AppSettings.DefaultStatusColorOrdered;
    private static string _onTheWay = AppSettings.DefaultStatusColorOnTheWay;
    private static string _inStock = AppSettings.DefaultStatusColorInStock;

    public static void Configure(
        string? notSpecified,
        string? ordered,
        string? onTheWay,
        string? inStock)
    {
        _notSpecified = Normalize(notSpecified, AppSettings.DefaultStatusColorNotSpecified);
        _ordered = Normalize(ordered, AppSettings.DefaultStatusColorOrdered);
        _onTheWay = Normalize(onTheWay, AppSettings.DefaultStatusColorOnTheWay);
        _inStock = Normalize(inStock, AppSettings.DefaultStatusColorInStock);
    }

    public static string ResolveBorderColor(string? deliveryStatus)
    {
        var status = OrderProductInfo.NormalizeDeliveryStatus(deliveryStatus);
        if (string.Equals(status, OrderProductInfo.PendingPreparationStatus, StringComparison.OrdinalIgnoreCase))
        {
            return PendingPreparationProductColor;
        }

        if (string.Equals(status, OrderProductInfo.OrderedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return _ordered;
        }

        if (string.Equals(status, OrderProductInfo.InTransitStatus, StringComparison.OrdinalIgnoreCase))
        {
            return _onTheWay;
        }

        if (string.Equals(status, OrderProductInfo.InStockStatus, StringComparison.OrdinalIgnoreCase))
        {
            return _inStock;
        }

        return _notSpecified;
    }

    public static string ResolveBackgroundColor(string? deliveryStatus)
    {
        if (string.Equals(
                OrderProductInfo.NormalizeDeliveryStatus(deliveryStatus),
                OrderProductInfo.DefaultDeliveryStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            return "#FFFFFFFF";
        }

        var border = ResolveBorderColor(deliveryStatus);
        return border.Length == 7 && border.StartsWith('#') ? $"#33{border[1..]}" : "#FFF8FAFC";
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 7 && normalized.StartsWith('#') ? normalized.ToUpperInvariant() : fallback;
    }
}
