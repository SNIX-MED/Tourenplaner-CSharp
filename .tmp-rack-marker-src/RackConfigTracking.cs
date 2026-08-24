using System.Text.Json;

namespace Gawela.RackConfig;

internal static class RackConfigTracking
{
    public const string SourceKey = "Gawela.RackConfig.Source";
    public const string TargetKey = "Gawela.RackConfig.Target";
    public const string LineKey = "Gawela.RackConfig.Line";
    public const string QuantityKey = "Gawela.RackConfig.Quantity";
    public const string MarkedOnUtcKey = "Gawela.RackConfig.MarkedOnUtc";
    public const string VersionKey = "Gawela.RackConfig.Version";
    public const string PendingKey = "Gawela.RackConfig.PendingOrderMarkers";
    public const string SourceValue = "GAWELA Palettenregal-Konfigurator";
    public const string Version = "6.4.43";

    public static List<RackConfigPendingMarker> ReadPending(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<RackConfigPendingMarker>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string WritePending(IEnumerable<RackConfigPendingMarker> markers)
        => JsonSerializer.Serialize(markers);
}

internal sealed class RackConfigPendingMarker
{
    public int ProductId { get; set; }
    public string? Sku { get; set; }
    public int Quantity { get; set; }
    public int Line { get; set; }
    public int StoreId { get; set; }
    public DateTime MarkedOnUtc { get; set; }
}
