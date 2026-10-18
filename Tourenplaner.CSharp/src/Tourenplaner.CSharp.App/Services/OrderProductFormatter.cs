using System.Globalization;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderProductFormatter
{
    public static string BuildSummary(IEnumerable<OrderProductInfo>? products)
        => FormatProducts(products, BuildSingleSummary, ", ");

    public static string BuildDetails(IEnumerable<OrderProductInfo>? products)
        => FormatProducts(products, BuildSingleDetail, Environment.NewLine);

    public static double ResolveUnitWeightKg(OrderProductInfo product) =>
        OrderProductWeightNormalization.ResolveUnitWeightKg(product);

    public static double ResolveTotalWeightKg(OrderProductInfo product) =>
        OrderProductWeightNormalization.ResolveTotalWeightKg(product);

    private static string FormatProducts(
        IEnumerable<OrderProductInfo>? products,
        Func<OrderProductInfo?, string> formatProduct,
        string separator)
    {
        var lines = (products ?? [])
            .Select(formatProduct)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        return lines.Count == 0 ? "N/A" : string.Join(separator, lines);
    }

    private static string BuildSingleSummary(OrderProductInfo? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Name))
        {
            return string.Empty;
        }

        var quantity = OrderProductInfo.NormalizeQuantity(product.Quantity);
        var totalWeightKg = ResolveTotalWeightKg(product);
        var summary = $"{quantity}x {product.Name.Trim()} ({totalWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg)";
        if (!string.IsNullOrWhiteSpace(product.Supplier))
        {
            summary += $" [{product.Supplier.Trim()}]";
        }

        return summary;
    }

    private static string BuildSingleDetail(OrderProductInfo? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Name))
        {
            return string.Empty;
        }

        var quantity = OrderProductInfo.NormalizeQuantity(product.Quantity);
        var unitWeightKg = ResolveUnitWeightKg(product);
        var totalWeightKg = ResolveTotalWeightKg(product);
        var parts = new List<string>
        {
            $"{quantity}x {product.Name.Trim()}",
            $"{unitWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg/Stk",
            $"Total: {totalWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg"
        };

        if (!string.IsNullOrWhiteSpace(product.Supplier))
        {
            parts.Add($"Lieferant: {product.Supplier.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(product.Dimensions))
        {
            parts.Add($"Masse: {product.Dimensions.Trim()}");
        }

        var deliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(product.DeliveryStatus);
        parts.Add($"Status: {deliveryStatus}");

        return string.Join(" | ", parts);
    }
}
