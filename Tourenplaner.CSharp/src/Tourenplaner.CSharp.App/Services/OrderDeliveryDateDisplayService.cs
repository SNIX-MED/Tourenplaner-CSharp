using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderDeliveryDateDisplayService
{
    public static string BuildDisplayText(Order? order, IEnumerable<TourRecord> tours, OrderDelivery? delivery = null)
    {
        if (order is null)
        {
            return string.Empty;
        }

        if (delivery?.PlannedDeliveryDate is { } deliveryDate && deliveryDate != DateOnly.MinValue)
        {
            return FormatDate(deliveryDate);
        }

        if (order.DeliveryDate.HasValue && order.DeliveryDate.Value != DateOnly.MinValue)
        {
            return FormatDate(order.DeliveryDate.Value);
        }

        var assignedTourDate = ResolveAssignedTourDate(delivery is null ? order.AssignedTourId : delivery.AssignedTourId, tours);
        if (!assignedTourDate.HasValue)
        {
            return string.Empty;
        }

        return $"{FormatDate(assignedTourDate.Value)} \u00B7 {ResolvePlanningSuffix(order.AvisoStatus)}";
    }

    private static DateOnly? ResolveAssignedTourDate(string? tourId, IEnumerable<TourRecord> tours)
    {
        var assignedTourId = (tourId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(assignedTourId))
        {
            return null;
        }

        var tour = (tours ?? [])
            .FirstOrDefault(x => string.Equals(
                x.Id.ToString(CultureInfo.InvariantCulture),
                assignedTourId,
                StringComparison.OrdinalIgnoreCase));

        return tour is null ? null : ResourceAvailabilityService.ParseDate(tour.Date);
    }

    private static string ResolvePlanningSuffix(string? avisoStatus)
    {
        var normalized = (avisoStatus ?? string.Empty).Trim();
        return string.Equals(normalized, "Best\u00E4tigt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "Bestaetigt", StringComparison.OrdinalIgnoreCase)
            ? "Best\u00E4tigt"
            : "Provisorisch";
    }

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }
}
