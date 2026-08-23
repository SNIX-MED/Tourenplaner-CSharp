using Smartstore;
using Smartstore.Core.Checkout.Orders.Events;
using Smartstore.Core.Data;
using Smartstore.Events;

namespace Gawela.RackConfig;

internal class Events : IConsumer
{
    public async Task HandleEventAsync(OrderPlacedEvent message, SmartDbContext db, CancellationToken cancelToken)
    {
        var order = message.Order;
        if (order == null || order.CustomerId <= 0)
            return;

        var customer = await db.Customers.FindByIdAsync(order.CustomerId, true, cancelToken);
        if (customer?.GenericAttributes == null)
            return;

        var pending = RackConfigTracking.ReadPending(customer.GenericAttributes.Get<string>(RackConfigTracking.PendingKey));
        if (pending.Count == 0)
            return;

        var orderedProductIds = order.OrderItems.Select(x => x.ProductId).ToHashSet();
        var matched = pending.Where(x => x.StoreId == order.StoreId && orderedProductIds.Contains(x.ProductId)).ToList();

        customer.GenericAttributes.Set(RackConfigTracking.PendingKey, string.Empty);
        await customer.GenericAttributes.SaveChangesAsync(cancelToken);

        if (matched.Count == 0)
            return;

        var lines = matched.Where(x => x.Line > 0).Select(x => x.Line).Distinct().OrderBy(x => x).ToArray();
        var matchedIds = matched.Select(x => x.ProductId).ToHashSet();
        var itemSummary = order.OrderItems.Where(x => matchedIds.Contains(x.ProductId)).Select(x => $"{x.Sku} × {x.Quantity}").Distinct().ToArray();

        if (order.GenericAttributes != null)
        {
            order.GenericAttributes.Set(RackConfigTracking.SourceKey, RackConfigTracking.SourceValue);
            order.GenericAttributes.Set(RackConfigTracking.VersionKey, RackConfigTracking.Version);
            order.GenericAttributes.Set(RackConfigTracking.LineKey, lines.Length > 0 ? string.Join(", ", lines) : "Konfigurator");
            order.GenericAttributes.Set(RackConfigTracking.QuantityKey, matched.Sum(x => Math.Max(1, x.Quantity)));
            await order.GenericAttributes.SaveChangesAsync(cancelToken);
        }

        var lineText = lines.Length > 0 ? $"Regalzeile(n): {string.Join(", ", lines)}. " : string.Empty;
        var itemText = itemSummary.Length > 0 ? $"Artikel: {string.Join("; ", itemSummary)}." : string.Empty;
        db.OrderNotes.Add(order, $"Quelle: {RackConfigTracking.SourceValue}. {lineText}{itemText} Tracking-Version {RackConfigTracking.Version}.");
        await db.SaveChangesAsync(cancelToken);
    }
}
