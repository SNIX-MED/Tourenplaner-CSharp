using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderDeliveryDateDisplayService
{
    public sealed record CardDate(string Text, string Kind);

    public static CardDate BuildCardDate(Order order, IEnumerable<TourRecord> tours, OrderDelivery? delivery = null,
        bool tourConfirmed = false)
    {
        var explicitDate = delivery?.PlannedDeliveryDate is { } planned && planned != DateOnly.MinValue
            ? planned : order.DeliveryDate is { } date && date != DateOnly.MinValue ? date : (DateOnly?)null;
        var tourId = delivery is null ? order.AssignedTourId : delivery.AssignedTourId;
        var tourDate = ResolveAssignedTourDate(tourId, tours);
        var displayedDate = explicitDate ?? tourDate;
        if (displayedDate is null) return new CardDate(string.Empty, string.Empty);

        // A tour assignment or an aviso confirmation never establishes a fixed date.
        var isFixed = order.DeliveryDate == displayedDate && !order.DeliveryCanOccurEarlier;
        var isProvisional = !isFixed && tourDate == displayedDate &&
                            ResolvePlanningSuffix(order.AvisoStatus) == "Provisorisch";
        return new CardDate(FormatDate(displayedDate.Value),
            isFixed ? "fixed" : tourConfirmed && tourDate == displayedDate ? "confirmed" : isProvisional ? "provisional" : "planned");
    }

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
