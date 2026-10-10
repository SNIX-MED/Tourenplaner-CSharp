using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderDeliveryRoutingService
{
    public static async Task<AddressGeocodingResult?> ApplyAsync(
        Order order,
        GeoPoint? fallbackLocation,
        Func<Order, Task<AddressGeocodingResult?>> resolveOrderAsync,
        bool requirePreciseLocation = false)
    {
        order.Type = DeliveryMethodExtensions.ResolveOrderType(order.DeliveryType);

        // A manually confirmed position is user-owned data. Normal order edits,
        // delivery-method routing and automatic geocoding must never replace it.
        if (order.IsLocationManuallySet && order.Location is not null)
        {
            if (!DeliveryMethodExtensions.CanUseLiefertour(order))
            {
                order.AssignedTourId = string.Empty;
            }

            return null;
        }

        if (!DeliveryMethodExtensions.CanUseLiefertour(order))
        {
            order.Location = null;
            order.AssignedTourId = string.Empty;
            return null;
        }

        var geocodingResult = await resolveOrderAsync(order);
        var canUseResultLocation = geocodingResult is not null &&
                                   (!requirePreciseLocation || geocodingResult.IsPrecise);
        order.Location = canUseResultLocation
            ? geocodingResult!.Location
            : fallbackLocation ?? order.Location;
        return geocodingResult;
    }
}
