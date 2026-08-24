using Smartstore;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Data;
using Smartstore.Core.Messaging.Events;
using Smartstore.Events;

namespace Gawela.CantileverRackConfig;

internal sealed class CantileverRackOrderMessageEvents : IConsumer
{
    public async Task HandleEventAsync(MessageModelPartCreatedEvent<OrderItem> message, SmartDbContext db, CancellationToken cancelToken)
    {
        var item = message.Source;
        var order = item?.Order;
        if (order == null || order.CustomerId <= 0 || message.Part is not IDictionary<string, object> part) return;

        var isMarked = string.Equals(order.GenericAttributes?.Get<string>(CantileverRackTracking.SourceKey), CantileverRackTracking.SourceValue, StringComparison.Ordinal);
        List<CantileverRackPendingMarker> matched = [];

        if (!isMarked)
        {
            var customer = await db.Customers.FindByIdAsync(order.CustomerId, true, cancelToken);
            if (customer?.GenericAttributes == null) return;
            var pending = CantileverRackTracking.ReadPending(customer.GenericAttributes.Get<string>(CantileverRackTracking.PendingKey));
            if (pending.Count == 0) return;
            var earliest = order.CreatedOnUtc.AddHours(-2);
            var latest = DateTime.UtcNow.AddMinutes(5);
            matched = pending.Where(x => x.StoreId == order.StoreId && x.ProductId == item.ProductId && x.MarkedOnUtc >= earliest && x.MarkedOnUtc <= latest).ToList();
            isMarked = matched.Count > 0;
        }

        if (!isMarked) return;

        var existing = part.TryGetValue("AttributeDescription", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        if (existing.Contains(CantileverRackTracking.SourceValue, StringComparison.OrdinalIgnoreCase)) return;

        var lines = matched.Where(x => x.Line > 0).Select(x => x.Line).Distinct().OrderBy(x => x).ToArray();
        var lineInfo = lines.Length > 0 ? $" <span style=\"font-weight:400;color:#6c757d\">(Regalzeile {string.Join(", ", lines)})</span>" : string.Empty;
        var sourceBadge = $"<div style=\"margin-top:8px;padding:6px 8px;border-left:3px solid #f28c00;background:#fff7ed;color:#7a4300;font-weight:700\">Quelle: {CantileverRackTracking.SourceValue}{lineInfo}</div>";
        part["AttributeDescription"] = string.IsNullOrWhiteSpace(existing) ? sourceBadge : existing + sourceBadge;
    }
}
