namespace Gawela.ColorConfigurator.Models;

public sealed class GawelaSeoModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int ColorCount { get; set; }
    public int LayerCount { get; set; }
    public int AttributeCount { get; set; }
    public long CombinationCount { get; set; }
    public string ResolutionMode { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string JsonLd { get; set; } = string.Empty;
    public string ProductGroupId { get; set; } = string.Empty;
    public string CurrentVariantUrl { get; set; } = string.Empty;
    public string CurrentVariantProductId { get; set; } = string.Empty;
    public string CurrentColorText { get; set; } = string.Empty;
    public List<GawelaSeoColorAreaModel> ColorAreas { get; set; } = new();
}

public sealed class GawelaSeoColorAreaModel
{
    public int ProductVariantAttributeId { get; set; }
    public int ProductAttributeId { get; set; }
    public int ProductId { get; set; }
    public int BundleItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string QueryKey { get; set; } = string.Empty;
    public int ColorCount { get; set; }
    public List<GawelaSeoColorOptionModel> Options { get; set; } = new();
}

public sealed class GawelaSeoColorOptionModel
{
    public int ProductVariantAttributeValueId { get; set; }
    public string Ral { get; set; } = string.Empty;
    public string ColorName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SemanticValue { get; set; } = string.Empty;
    public bool IsPreSelected { get; set; }
}
