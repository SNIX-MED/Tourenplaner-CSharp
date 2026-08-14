using System.ComponentModel.DataAnnotations;

namespace Gawela.ColorConfigurator.Models;

public class GawelaAssetAdminModel
{
    public string ActiveTab { get; set; } = "products";

    // Page state.
    public bool IsEditor { get; set; }
    public bool IsNew { get; set; }
    public int OriginalMasterProductId { get; set; }
    public string ConfiguratorName { get; set; }
    public string PageMessage { get; set; }

    // Base/master product.
    public int ProductId { get; set; }
    public string ProductSku { get; set; }
    public string ProductName { get; set; }
    public string ThumbnailLabel { get; set; } = "Farbe konfigurieren";

    // Smartstore Media Manager: uploads and selections use the standard catalog album.
    [UIHint("Media"), AdditionalMetadata("album", "catalog"), AdditionalMetadata("typeFilter", "image")]
    public int? BaseMediaFileId { get; set; }
    public bool HasExistingBaseImage { get; set; }
    public bool HasLocalBaseImage { get; set; }

    // Shared products.
    public string AdditionalProductIds { get; set; }
    public string[] AdditionalProductIdStrings { get; set; } = Array.Empty<string>();
    public List<GawelaAssignedProductAdminModel> AssignedProducts { get; set; } = new();

    // Layers and source attributes of the base product.
    public List<GawelaLayerAdminModel> Layers { get; set; } = new();
    public List<GawelaAttributeOptionModel> AvailableAttributes { get; set; } = new();

    // Overview.
    public List<GawelaConfiguratorOverviewModel> Configurators { get; set; } = new();

    // RAL palette.
    public GawelaPaletteAdminModel Palette { get; set; } = new();
}

public class GawelaLayerAdminModel
{
    public bool IsActive { get; set; }
    public string Key { get; set; }
    public string Name { get; set; }
    public int ProductVariantAttributeId { get; set; }
    public string BaseRal { get; set; } = "7035";
    public string DefaultRal { get; set; } = "7035";

    [UIHint("Media"), AdditionalMetadata("album", "catalog"), AdditionalMetadata("typeFilter", "image")]
    public int? MaskMediaFileId { get; set; }

    public bool HasExistingMask { get; set; }
    public bool HasLocalMask { get; set; }
}

public class GawelaAttributeOptionModel
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class GawelaAssignedProductAdminModel
{
    public int ProductId { get; set; }
    public string Sku { get; set; }
    public string ProductName { get; set; }
}

public class GawelaConfiguratorOverviewModel
{
    public string Name { get; set; }
    public int MasterProductId { get; set; }
    public string MasterSku { get; set; }
    public string MasterProductName { get; set; }
    public int ProductCount { get; set; }
    public int LayerCount { get; set; }
    public bool HasBaseImage { get; set; }
    public bool UsesSmartstoreMedia { get; set; }
    public bool IsComplete { get; set; }
    public bool IsLegacy { get; set; }
}
