using Smartstore;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Data;
using Smartstore.Core.Messaging.Events;
using Smartstore.Events;

namespace Gawela.DrumRackConfig;

/// <summary>
/// Adds the Fassregal configurator source marker to Smartstore order message items.
/// This runs while the order e-mail model is being created, i.e. before OrderPlacedEvent
/// is published. The pending cart markers therefore make the source visible already in
/// the first "new order" notification.
/// </summary>
internal sealed class DrumRackOrderMessageEvents : IConsumer
{
    public async Task HandleEventAsync(
        MessageModelPartCreatedEvent<OrderItem> message,
        SmartDbContext db,
        CancellationToken cancelToken)
    {
        var item = message.Source;
        var order = item?.Order;

        if (order == null || order.CustomerId <= 0 || message.Part is not IDictionary<string, object> part)
            return;

        var isMarked = string.Equals(
            order.GenericAttributes?.Get<string>(DrumRackTracking.SourceKey),
            DrumRackTracking.SourceValue,
            StringComparison.Ordinal);

        List<DrumRackPendingMarker> matched = [];

        // Initial order e-mails are sent before OrderPlacedEvent. During that window the
        // customer still owns the pending configurator markers written by MarkSource().
        if (!isMarked)
        {
            var customer = await db.Customers.FindByIdAsync(order.CustomerId, true, cancelToken);
            if (customer?.GenericAttributes == null)
                return;

            var pending = DrumRackTracking.ReadPending(
                customer.GenericAttributes.Get<string>(DrumRackTracking.PendingKey));

            if (pending.Count == 0)
                return;

            var earliest = order.CreatedOnUtc.AddHours(-2);
            var latest = DateTime.UtcNow.AddMinutes(5);

            matched = pending
                .Where(x =>
                    x.StoreId == order.StoreId &&
                    x.ProductId == item.ProductId &&
                    x.MarkedOnUtc >= earliest &&
                    x.MarkedOnUtc <= latest)
                .ToList();

            isMarked = matched.Count > 0;
        }

        if (!isMarked)
            return;

        var existing = part.TryGetValue("AttributeDescription", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

        if (existing.Contains(DrumRackTracking.SourceValue, StringComparison.OrdinalIgnoreCase))
            return;

        var lines = matched
            .Where(x => x.Line > 0)
            .Select(x => x.Line)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var lineInfo = lines.Length > 0
            ? $" <span style=\"font-weight:400;color:#6c757d\">(Regalzeile {string.Join(", ", lines)})</span>"
            : string.Empty;

        var sourceBadge =
            $"<div style=\"margin-top:8px;padding:6px 8px;border-left:3px solid #f28c00;background:#fff7ed;color:#7a4300;font-weight:700\">" +
            $"Quelle: {DrumRackTracking.SourceValue}{lineInfo}</div>";

        part["AttributeDescription"] = string.IsNullOrWhiteSpace(existing)
            ? sourceBadge
            : existing + sourceBadge;
    }
}
