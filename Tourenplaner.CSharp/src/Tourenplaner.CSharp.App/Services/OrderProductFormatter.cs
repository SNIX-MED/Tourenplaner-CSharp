using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderProductFormatter
{
    public static string BuildSummary(IEnumerable<OrderProductInfo>? products)
    {
        var lines = (products ?? [])
            .Select(BuildSingleSummary)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return lines.Count == 0 ? "N/A" : string.Join(", ", lines);
    }

    public static string BuildDetails(IEnumerable<OrderProductInfo>? products)
    {
        var lines = (products ?? [])
            .Select(BuildSingleDetail)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return lines.Count == 0 ? "N/A" : string.Join(Environment.NewLine, lines);
    }

    public static double ResolveUnitWeightKg(OrderProductInfo product)
    {
        var quantity = Math.Max(1, product.Quantity);
        if (product.UnitWeightKg > 0)
        {
            return product.UnitWeightKg;
        }

        if (product.WeightKg > 0)
        {
            return product.WeightKg / quantity;
        }

        return 0d;
    }

    public static double ResolveTotalWeightKg(OrderProductInfo product)
    {
        var quantity = Math.Max(1, product.Quantity);
        if (product.WeightKg > 0)
        {
            return product.WeightKg;
        }

        return ResolveUnitWeightKg(product) * quantity;
    }

    private static string BuildSingleSummary(OrderProductInfo? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Name))
        {
            return string.Empty;
        }

        var quantity = Math.Max(1, product.Quantity);
        var totalWeightKg = ResolveTotalWeightKg(product);
        return $"{quantity}x {product.Name.Trim()} ({totalWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg)";
    }

    private static string BuildSingleDetail(OrderProductInfo? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Name))
        {
            return string.Empty;
        }

        var quantity = Math.Max(1, product.Quantity);
        var unitWeightKg = ResolveUnitWeightKg(product);
        var totalWeightKg = ResolveTotalWeightKg(product);
        var parts = new List<string>
        {
            $"{quantity}x {product.Name.Trim()}",
            $"{unitWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg/Stk",
            $"Total: {totalWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg"
        };

        if (!string.IsNullOrWhiteSpace(product.Dimensions))
        {
            parts.Add($"Masse: {product.Dimensions.Trim()}");
        }

        return string.Join(" | ", parts);
    }
}
