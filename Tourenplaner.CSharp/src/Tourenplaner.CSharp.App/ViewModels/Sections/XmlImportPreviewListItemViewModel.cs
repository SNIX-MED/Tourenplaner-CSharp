using Tourenplaner.CSharp.Application.Common;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class XmlImportPreviewListItemViewModel
{
    private XmlImportPreviewListItemViewModel(
        string actionLabel,
        string actionBackground,
        string actionForeground,
        string orderId,
        string customerName,
        string deliveryType,
        string orderTypeLabel,
        string changeSummary)
    {
        ActionLabel = actionLabel;
        ActionBackground = actionBackground;
        ActionForeground = actionForeground;
        OrderId = orderId;
        CustomerName = customerName;
        DeliveryType = deliveryType;
        OrderTypeLabel = orderTypeLabel;
        ChangeSummary = changeSummary;
    }

    public string ActionLabel { get; }
    public string ActionBackground { get; }
    public string ActionForeground { get; }
    public string OrderId { get; }
    public string CustomerName { get; }
    public string DeliveryType { get; }
    public string OrderTypeLabel { get; }
    public string ChangeSummary { get; }
    public bool HasChangeSummary => !string.IsNullOrWhiteSpace(ChangeSummary);
    public string CustomerLine => string.IsNullOrWhiteSpace(CustomerName) ? "(ohne Kundenname)" : CustomerName;
    public string MetaLine => string.Join(" | ", new[]
    {
        string.IsNullOrWhiteSpace(OrderTypeLabel) ? string.Empty : OrderTypeLabel,
        string.IsNullOrWhiteSpace(DeliveryType) ? string.Empty : DeliveryType
    }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public static XmlImportPreviewListItemViewModel FromPreviewItem(ImportPreviewItem item)
    {
        var (label, background, foreground) = item.Action switch
        {
            ImportPreviewAction.Create => ("Neu", "#DCFCE7", "#166534"),
            ImportPreviewAction.Update => ("Update", "#EDE9FE", "#5B21B6"),
            _ => ("Unveraendert", "#E2E8F0", "#334155")
        };

        var changeSummary = item.Changes.Count == 0
            ? "Keine Aenderungen erkannt."
            : string.Join(" | ", item.Changes);

        return new XmlImportPreviewListItemViewModel(
            label,
            background,
            foreground,
            (item.OrderId ?? string.Empty).Trim(),
            (item.CustomerName ?? string.Empty).Trim(),
            (item.DeliveryType ?? string.Empty).Trim(),
            (item.OrderTypeLabel ?? string.Empty).Trim(),
            changeSummary);
    }
}
