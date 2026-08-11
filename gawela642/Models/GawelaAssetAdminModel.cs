using Microsoft.AspNetCore.Http;

namespace Gawela.ColorConfigurator.Models;

public class GawelaAssetAdminModel
{
    public string ProductReference { get; set; }
    public int ProductId { get; set; }
    public string ProductSku { get; set; }
    public string ProductName { get; set; }
    public string ThumbnailLabel { get; set; } = "Farbe konfigurieren";
    public IFormFile BaseImage { get; set; }
    public List<GawelaLayerAdminModel> Layers { get; set; } = new();
    public List<GawelaAttributeOptionModel> AvailableAttributes { get; set; } = new();
    public List<ConfiguredProductModel> ConfiguredProducts { get; set; } = new();
}

public class GawelaLayerAdminModel
{
    public string Key { get; set; }
    public string Name { get; set; }
    public int ProductVariantAttributeId { get; set; }
    public string BaseRal { get; set; } = "7035";
    public string DefaultRal { get; set; } = "7035";
    public IFormFile Mask { get; set; }
    public bool HasExistingMask { get; set; }
}

public class GawelaAttributeOptionModel
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class ConfiguredProductModel
{
    public int ProductId { get; set; }
    public string Sku { get; set; }
    public string ProductName { get; set; }
    public bool HasBaseImage { get; set; }
    public int LayerCount { get; set; }
    public bool IsLegacy { get; set; }
    public bool IsComplete { get; set; }
}
