using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderDeliveryDateDisplayService
{
    public static string BuildDisplayText(Order? order, IEnumerable<TourRecord> tours)
    {
        if (order is null)
        {
            return string.Empty;
        }

        if (order.DeliveryDate.HasValue)
        {
            return FormatDate(order.DeliveryDate.Value);
        }

        var assignedTourDate = ResolveAssignedTourDate(order, tours);
        if (!assignedTourDate.HasValue)
        {
            return string.Empty;
        }

        return $"{FormatDate(assignedTourDate.Value)} \u00B7 {ResolvePlanningSuffix(order.AvisoStatus)}";
    }

    private static DateOnly? ResolveAssignedTourDate(Order order, IEnumerable<TourRecord> tours)
    {
        var assignedTourId = (order.AssignedTourId ?? string.Empty).Trim();
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
