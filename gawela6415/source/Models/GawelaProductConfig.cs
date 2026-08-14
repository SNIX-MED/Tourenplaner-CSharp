namespace Gawela.ColorConfigurator.Models;

public sealed class GawelaProductConfig
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ThumbnailLabel { get; set; } = "Farbe konfigurieren";
    public int? BaseMediaFileId { get; set; }
    public List<GawelaLayerConfig> Layers { get; set; } = new();
}

public sealed class GawelaLayerConfig
{
    public string Key { get; set; }
    public string Name { get; set; }
    public int ProductVariantAttributeId { get; set; }
    public string AttributeLabel { get; set; }
    public string AssetKind { get; set; }
    public string BaseRal { get; set; } = "7035";
    public string DefaultRal { get; set; } = "7035";
    public int? MaskMediaFileId { get; set; }
}
