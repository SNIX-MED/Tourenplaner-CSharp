using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Common;

public static class OrderProductWeightNormalization
{
    public static double ResolveUnitWeightKg(OrderProductInfo product)
    {
        var quantity = OrderProductInfo.NormalizeQuantity(product.Quantity);
        if (product.UnitWeightKg > 0)
        {
            return product.UnitWeightKg;
        }

        if (product.WeightKg != 0)
        {
            return product.WeightKg / quantity;
        }

        return 0d;
    }

    public static double ResolveTotalWeightKg(OrderProductInfo product)
    {
        var quantity = OrderProductInfo.NormalizeQuantity(product.Quantity);
        if (product.WeightKg != 0)
        {
            return product.WeightKg;
        }

        return ResolveUnitWeightKg(product) * quantity;
    }
}
