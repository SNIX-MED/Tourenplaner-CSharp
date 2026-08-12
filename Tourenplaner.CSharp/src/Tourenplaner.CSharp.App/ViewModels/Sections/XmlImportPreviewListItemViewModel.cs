using Tourenplaner.CSharp.Application.Common;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class XmlImportPreviewListItemViewModel
{
    private XmlImportPreviewListItemViewModel(
        string actionLabel,
        string actionBackground,
        string actionBorder,
        string actionForeground,
        string orderId,
        string customerName,
        string deliveryType,
        string orderTypeLabel,
        string changeSummary)
    {
        ActionLabel = actionLabel;
        ActionBackground = actionBackground;
        ActionBorder = actionBorder;
        ActionForeground = actionForeground;
        OrderId = orderId;
        CustomerName = customerName;
        DeliveryType = deliveryType;
        OrderTypeLabel = orderTypeLabel;
        ChangeSummary = changeSummary;
    }

    public string ActionLabel { get; }
    public string ActionBackground { get; }
    public string ActionBorder { get; }
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
        var (label, background, border, foreground) = item.Action switch
        {
            ImportPreviewAction.Create => ("Neu", "#ECFDF3", "#BBF7D0", "#15803D"),
            ImportPreviewAction.Update => ("Update", "#EDE9FE", "#C4B5FD", "#6D28D9"),
            _ => ("Unveraendert", "#F1F5F9", "#CBD5E1", "#475569")
        };

        var changeSummary = item.Changes.Count == 0
            ? "Keine Aenderungen erkannt."
            : string.Join(" | ", item.Changes);

        return new XmlImportPreviewListItemViewModel(
            label,
            background,
            border,
            foreground,
            (item.OrderId ?? string.Empty).Trim(),
            (item.CustomerName ?? string.Empty).Trim(),
            (item.DeliveryType ?? string.Empty).Trim(),
            (item.OrderTypeLabel ?? string.Empty).Trim(),
            changeSummary);
    }
}
