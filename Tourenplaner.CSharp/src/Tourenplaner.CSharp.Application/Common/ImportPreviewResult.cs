namespace Tourenplaner.CSharp.Application.Common;

public enum ImportPreviewAction
{
    Create,
    Update,
    Unchanged
}

public sealed class ImportPreviewResult
{
    public int InputOrderCount { get; set; }
    public int CreatedOrders { get; set; }
    public int UpdatedOrders { get; set; }
    public int UnchangedOrders { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<ImportPreviewItem> Items { get; set; } = new();

    public int ValidOrders => CreatedOrders + UpdatedOrders + UnchangedOrders;
    public bool HasChanges => CreatedOrders > 0 || UpdatedOrders > 0;
}

public sealed class ImportPreviewItem
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public ImportPreviewAction Action { get; set; }
    public string DeliveryType { get; set; } = string.Empty;
    public string OrderTypeLabel { get; set; } = string.Empty;
    public List<string> Changes { get; set; } = new();
}
