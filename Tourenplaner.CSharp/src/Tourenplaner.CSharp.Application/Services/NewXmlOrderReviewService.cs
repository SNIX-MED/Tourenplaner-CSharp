using System.Globalization;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public static class NewXmlOrderReviewService
{
    public static string ReviewKey(Order order) => order.XmlCreatedAtUtc.HasValue
        ? order.Id.Trim() + ":" + order.XmlCreatedAtUtc.Value.UtcTicks.ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    public static bool IsPending(Order order, IReadOnlySet<string> seenKeys) =>
        order.IsXmlImported && !string.IsNullOrWhiteSpace(order.Id) && order.XmlCreatedAtUtc.HasValue && !order.XmlNewOrderWasPlanned &&
        !order.IsArchived && DeliveryMethodExtensions.CanUseLiefertour(order) &&
        string.IsNullOrWhiteSpace(order.AssignedTourId) &&
        !(order.Deliveries ?? []).Any(delivery => !string.IsNullOrWhiteSpace(delivery.AssignedTourId)) &&
        !seenKeys.Contains(ReviewKey(order));

    public static bool MarkPlanned(Order order)
    {
        if (!order.IsXmlImported || !order.XmlCreatedAtUtc.HasValue || order.XmlNewOrderWasPlanned) return false;
        if (string.IsNullOrWhiteSpace(order.AssignedTourId) &&
            !(order.Deliveries ?? []).Any(delivery => !string.IsNullOrWhiteSpace(delivery.AssignedTourId))) return false;
        order.XmlNewOrderWasPlanned = true;
        return true;
    }
}
