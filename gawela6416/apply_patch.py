from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
models = root / 'Models'
components = root / 'Components'
seo_views = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorSeo'

(models / 'GawelaSeoModel.cs').write_text(r'''namespace Gawela.ColorConfigurator.Models;

public sealed class GawelaSeoModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;

    // Number of distinct RAL colours available across all effective colour attributes.
    public int ColorCount { get; set; }
    public int LayerCount { get; set; }
    public int AttributeCount { get; set; }
    public long CombinationCount { get; set; }

    public List<string> LayerNames { get; set; } = new();
    public List<GawelaSeoColorAreaModel> ColorAreas { get; set; } = new();
    public string ResolutionMode { get; set; } = string.Empty;
}

public sealed class GawelaSeoColorAreaModel
{
    public int ProductVariantAttributeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ColorCount { get; set; }
    public List<string> RalCodes { get; set; } = new();
}
''', encoding='utf-8')

(components / 'GawelaColorSeoViewComponent.cs').write_text(r'''using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Web.Models.Catalog;
using Gawela.ColorConfigurator.Models;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Components;

public sealed class GawelaColorSeoViewComponent : ViewComponent
{
    public const string ProductModelItemKey = "Gawela.ColorConfigurator.ProductModel";

    private static readonly Regex RalRegex = new(
        @"(?:RAL\s*[-:]?\s*)?(?<!\d)(\d{4})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SmartDbContext _db;
    private readonly GawelaAssetStore _assetStore;
    private readonly GawelaPaletteStore _paletteStore;
    private readonly GawelaProductGroupStore _groupStore;

    public GawelaColorSeoViewComponent(
        SmartDbContext db,
        GawelaAssetStore assetStore,
        GawelaPaletteStore paletteStore,
        GawelaProductGroupStore groupStore)
    {
        _db = db;
        _assetStore = assetStore;
        _paletteStore = paletteStore;
        _groupStore = groupStore;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (HttpContext.Items[ProductModelItemKey] is not ProductDetailsModel product || product.Id <= 0)
        {
            return Content(string.Empty);
        }

        var resolution = await ResolveOwnerProductIdAsync(product.Id);
        var ownerProductId = resolution.OwnerProductId;
        var config = _assetStore.LoadEffectiveConfig(ownerProductId);

        if (config?.Layers == null || config.Layers.Count == 0 || !_assetStore.IsComplete(ownerProductId))
        {
            return Content(string.Empty);
        }

        var allowedRals = _paletteStore.Load()
            .Select(x => x.Ral)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Use the ProductDetailsModel that Smartstore already prepared for the current page.
        // This is important for shared configurators: the master can have different local
        // ProductVariantAttribute IDs than the product that is currently being viewed.
        var currentAttributes = product.ProductVariantAttributes
            .Where(x => x != null && x.ShouldBeRendered)
            .ToList();

        var resolved = new List<(GawelaLayerConfig Layer, ProductDetailsModel.ProductVariantAttributeModel Attribute)>();
        foreach (var layer in config.Layers)
        {
            var attribute = currentAttributes.FirstOrDefault(x => x.Id == layer.ProductVariantAttributeId)
                ?? currentAttributes.FirstOrDefault(x => NamesMatch(x.GetLabel(), layer.AttributeLabel))
                ?? currentAttributes.FirstOrDefault(x => NamesMatch(x.Name, layer.AttributeLabel))
                ?? currentAttributes.FirstOrDefault(x => NamesMatch(x.GetLabel(), layer.Name))
                ?? currentAttributes.FirstOrDefault(x => NamesMatch(x.Name, layer.Name));

            if (attribute != null)
            {
                resolved.Add((layer, attribute));
            }
        }

        var areas = new List<GawelaSeoColorAreaModel>();
        foreach (var attributeGroup in resolved.GroupBy(x => x.Attribute.Id))
        {
            var attribute = attributeGroup.First().Attribute;
            var rals = attribute.Values
                .SelectMany(x => new[] { x.Name, x.Alias, x.Title })
                .Select(x => ExtractRal(x, allowedRals))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            if (rals.Count == 0)
            {
                continue;
            }

            var names = attributeGroup
                .Select(x => string.IsNullOrWhiteSpace(x.Layer.Name) ? x.Layer.AttributeLabel : x.Layer.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fallbackName = attribute.GetLabel();
            var displayName = names.Count switch
            {
                0 => fallbackName ?? $"Farbbereich {attribute.Id}",
                1 => names[0],
                _ => string.Join(" / ", names)
            };

            areas.Add(new GawelaSeoColorAreaModel
            {
                ProductVariantAttributeId = attribute.Id,
                Name = displayName,
                ColorCount = rals.Count,
                RalCodes = rals
            });
        }

        // Without an effective selectable RAL value there is no useful configurator fact to expose.
        if (areas.Count == 0)
        {
            return Content(string.Empty);
        }

        long combinations = 1;
        foreach (var area in areas)
        {
            if (area.ColorCount <= 0)
            {
                combinations = 0;
                break;
            }
            if (combinations > long.MaxValue / area.ColorCount)
            {
                combinations = 0;
                break;
            }
            combinations *= area.ColorCount;
        }

        var layerNames = config.Layers
            .Select(x => string.IsNullOrWhiteSpace(x.Name) ? x.AttributeLabel : x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var distinctRals = areas
            .SelectMany(x => x.RalCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return View(new GawelaSeoModel
        {
            ProductId = product.Id,
            ProductName = product.Name.Value ?? string.Empty,
            Sku = product.Sku ?? string.Empty,
            ColorCount = distinctRals,
            LayerCount = config.Layers.Count,
            AttributeCount = areas.Count,
            CombinationCount = combinations,
            LayerNames = layerNames,
            ColorAreas = areas,
            ResolutionMode = resolution.Mode
        });
    }

    private static string ExtractRal(string value, HashSet<string> allowedRals)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        foreach (Match match in RalRegex.Matches(value))
        {
            var code = match.Groups[1].Value;
            if (allowedRals.Contains(code)) return code;
        }
        return null;
    }

    private static bool NamesMatch(string a, string b)
    {
        var x = NormalizeName(a);
        var y = NormalizeName(b);
        return x.Length > 0 && y.Length > 0 && (x == y || x.Contains(y) || y.Contains(x));
    }

    private static string NormalizeName(string value)
    {
        return string.Join(' ', (value ?? string.Empty)
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();
    }

    private async Task<(int OwnerProductId, string Mode)> ResolveOwnerProductIdAsync(int productId)
    {
        if (productId <= 0) return (productId, "invalid");

        var directGroup = _groupStore.FindByProduct(productId);
        if (directGroup != null)
        {
            return (directGroup.MasterProductId, "product-id");
        }

        if (_assetStore.IsComplete(productId))
        {
            return (productId, "local");
        }

        var currentSku = await _db.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => x.Sku)
            .FirstOrDefaultAsync();

        var normalizedCurrentSku = NormalizeSku(currentSku);
        if (normalizedCurrentSku.Length == 0) return (productId, "none-no-sku");

        var groups = _groupStore.Load();
        var memberIds = groups
            .SelectMany(x => x.ProductIds ?? new List<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (memberIds.Length == 0) return (productId, "none-no-members");

        var memberProducts = await _db.Products.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Sku })
            .ToListAsync();

        var matchingMemberIds = memberProducts
            .Where(x => NormalizeSku(x.Sku) == normalizedCurrentSku)
            .Select(x => x.Id)
            .ToHashSet();

        if (matchingMemberIds.Count > 0)
        {
            var skuGroup = groups.FirstOrDefault(x =>
                (x.ProductIds ?? new List<int>()).Any(matchingMemberIds.Contains));
            if (skuGroup != null) return (skuGroup.MasterProductId, "sku-fallback");
        }

        return (productId, "none");
    }

    private static string NormalizeSku(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }
}
''', encoding='utf-8')

(seo_views / 'Default.cshtml').write_text(r'''@using Gawela.ColorConfigurator.Models
@model GawelaSeoModel
@{
    var areas = Model.ColorAreas.Where(x => x.ColorCount > 0).ToList();
    var sameCount = areas.Count > 1 && areas.Select(x => x.ColorCount).Distinct().Count() == 1;
    var areaText = areas.Count switch
    {
        0 => "die konfigurierbaren Farbbereiche",
        1 => areas[0].Name,
        2 => $"{areas[0].Name} und {areas[1].Name}",
        _ => string.Join(", ", areas.Take(areas.Count - 1).Select(x => x.Name)) + " und " + areas.Last().Name
    };
}
<section class="gawela-configurator-seo" aria-labelledby="gawela-configurator-seo-title-@Model.ProductId" data-gawela-seo="true" data-resolution="@Model.ResolutionMode">
    <h2 id="gawela-configurator-seo-title-@Model.ProductId" class="gawela-configurator-seo-title">RAL-Farbkonfigurator</h2>
    <p class="gawela-configurator-seo-text">
        @if (areas.Count == 1)
        {
            <text>@areas[0].Name direkt online gestalten: </text>
            <span itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
                <meta itemprop="name" content="RAL-Farben – @areas[0].Name" />
                <strong itemprop="value">@areas[0].ColorCount</strong> RAL-Farben
            </span>
        }
        else if (sameCount)
        {
            <text>@areaText direkt online gestalten: </text>
            <span itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
                <meta itemprop="name" content="RAL-Farben je konfigurierbarem Farbbereich" />
                <strong itemprop="value">je @areas[0].ColorCount</strong> RAL-Farben
            </span>
        }
        else
        {
            @for (var i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                <span itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
                    <meta itemprop="name" content="RAL-Farben – @area.Name" />
                    <strong>@area.Name:</strong> <span itemprop="value">@area.ColorCount</span> RAL-Farben
                </span>
                @if (i < areas.Count - 1)
                {
                    <text> · </text>
                }
            }
        }

        @if (Model.CombinationCount > 0 && Model.AttributeCount > 1)
        {
            <text> · </text>
            <span itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
                <meta itemprop="name" content="Mögliche Farbkombinationen in der Vorschau" />
                <strong itemprop="value">bis zu @Model.CombinationCount</strong> Farbkombinationen
            </span>
        }

        <span itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
            <meta itemprop="name" content="Interaktiver RAL-Farbkonfigurator" />
            <meta itemprop="value" content="verfügbar" />
        </span>
    </p>
    <p class="gawela-configurator-seo-note">Bildschirmdarstellung unverbindlich; Farbe, Proportionen, Details und Ausführung können abweichen.</p>
</section>
''', encoding='utf-8')

# Keep browser cache-busting in sync with the module version.
host = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorHost' / 'Default.cshtml'
host_text = host.read_text(encoding='utf-8')
host_text = host_text.replace('v=6.4.15', 'v=6.4.16')
host.write_text(host_text, encoding='utf-8')

module = root / 'module.json'
module_text = module.read_text(encoding='utf-8')
if '"Version": "6.4.15"' not in module_text:
    raise SystemExit('Expected 6.4.15 module version before applying 6.4.16 patch.')
module_text = module_text.replace('"Version": "6.4.15"', '"Version": "6.4.16"', 1)
module.write_text(module_text, encoding='utf-8')

# Build-time safety checks.
checks = {
    'Models/GawelaSeoModel.cs': ['GawelaSeoColorAreaModel', 'AttributeCount', 'RalCodes'],
    'Components/GawelaColorSeoViewComponent.cs': ['ProductVariantAttributes', 'ExtractRal', 'CombinationCount = combinations', 'GroupBy(x => x.Attribute.Id)'],
    'Views/Shared/Components/GawelaColorSeo/Default.cshtml': ['je @areas[0].ColorCount', 'area.ColorCount', 'bis zu @Model.CombinationCount'],
    'Views/Shared/Components/GawelaColorHost/Default.cshtml': ['v=6.4.16'],
}
for rel, needles in checks.items():
    text = (root / rel).read_text(encoding='utf-8')
    for needle in needles:
        if needle not in text:
            raise SystemExit(f'6.4.16 verification failed: {needle!r} missing in {rel}')
