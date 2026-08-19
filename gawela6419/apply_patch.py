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
    public int ColorCount { get; set; }
    public int LayerCount { get; set; }
    public int AttributeCount { get; set; }
    public long CombinationCount { get; set; }
    public string ResolutionMode { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string JsonLd { get; set; } = string.Empty;
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
}
''', encoding='utf-8')

(components / 'GawelaColorSeoViewComponent.cs').write_text(r'''using System.Text.Json;
using System.Text.RegularExpressions;
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

    private const int MaxStructuredVariants = 4096;

    private static readonly Regex RalRegex = new(
        @"(?:RAL\s*[-:]?\s*)?(?<!\d)(\d{4})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlugSeparatorRegex = new(
        @"[^a-z0-9]+",
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

        var palette = _paletteStore.Load()
            .Where(x => !string.IsNullOrWhiteSpace(x.Ral))
            .GroupBy(x => x.Ral.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.First().Name?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var allowedRals = palette.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Use the exact ProductDetailsModel that Smartstore rendered for this request.
        // This preserves shared configurators while binding SEO data to the real current
        // ProductVariantAttribute and ProductVariantAttributeValue IDs.
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
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attributeGroup in resolved.GroupBy(x => x.Attribute.Id))
        {
            var attribute = attributeGroup.First().Attribute;
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

            var areaSlug = Slugify(displayName);
            if (areaSlug.Length == 0)
            {
                areaSlug = $"bereich-{attribute.Id}";
            }
            if (!usedSlugs.Add(areaSlug))
            {
                areaSlug = $"{areaSlug}-{attribute.Id}";
                usedSlugs.Add(areaSlug);
            }

            var options = attribute.Values
                .Where(x => x != null && !x.IsDisabled)
                .Select(x => new
                {
                    Value = x,
                    Ral = ExtractRal(string.Join(" ", new[] { x.Name, x.Alias, x.Title }), allowedRals)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Ral))
                .GroupBy(x => x.Ral, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderByDescending(v => v.Value.IsPreSelected).ThenBy(v => v.Value.DisplayOrder).First())
                .OrderBy(x => x.Ral)
                .Select(x =>
                {
                    var ral = x.Ral.Trim();
                    var colorName = palette.TryGetValue(ral, out var name) ? name : string.Empty;
                    var display = $"RAL {ral}{(colorName.Length > 0 ? " " + colorName : string.Empty)}";
                    return new GawelaSeoColorOptionModel
                    {
                        ProductVariantAttributeValueId = x.Value.Id,
                        Ral = ral,
                        ColorName = colorName,
                        DisplayName = display,
                        SemanticValue = $"ral-{ral}{(colorName.Length > 0 ? "-" + Slugify(colorName) : string.Empty)}"
                    };
                })
                .ToList();

            if (options.Count == 0)
            {
                continue;
            }

            areas.Add(new GawelaSeoColorAreaModel
            {
                ProductVariantAttributeId = attribute.Id,
                ProductAttributeId = attribute.ProductAttributeId,
                ProductId = attribute.ProductId,
                BundleItemId = attribute.BundleItemId,
                Name = displayName,
                Slug = areaSlug,
                QueryKey = attribute.BuildControlId(),
                ColorCount = options.Count,
                Options = options
            });
        }

        if (areas.Count == 0)
        {
            return Content(string.Empty);
        }

        long combinations = 1;
        foreach (var area in areas)
        {
            if (area.ColorCount <= 0 || combinations > long.MaxValue / area.ColorCount)
            {
                combinations = 0;
                break;
            }
            combinations *= area.ColorCount;
        }

        var distinctRals = areas
            .SelectMany(x => x.Options.Select(v => v.Ral))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var baseUrl = BuildBaseUrl(product);
        var jsonLd = BuildJsonLd(product, areas, baseUrl, combinations);

        return View(new GawelaSeoModel
        {
            ProductId = product.Id,
            ProductName = product.Name.Value ?? string.Empty,
            Sku = product.Sku ?? string.Empty,
            ColorCount = distinctRals,
            LayerCount = config.Layers.Count,
            AttributeCount = areas.Count,
            CombinationCount = combinations,
            ResolutionMode = resolution.Mode,
            BaseUrl = baseUrl,
            JsonLd = jsonLd,
            ColorAreas = areas
        });
    }

    private string BuildJsonLd(
        ProductDetailsModel product,
        IReadOnlyList<GawelaSeoColorAreaModel> areas,
        string baseUrl,
        long combinations)
    {
        var productName = product.Name.Value ?? string.Empty;
        var groupId = $"{baseUrl}#gawela-ral-variants";
        var variants = new List<Dictionary<string, object>>();

        if (combinations > 0)
        {
            var selected = new List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)>();
            BuildVariantsRecursive(productName, areas, 0, selected, groupId, baseUrl, variants);
        }

        var colorCatalog = areas.Select(area => new Dictionary<string, object>
        {
            ["@type"] = "PropertyValue",
            ["name"] = $"RAL-Farben – {area.Name}",
            ["value"] = string.Join(", ", area.Options.Select(x => x.DisplayName))
        }).ToArray();

        var group = new Dictionary<string, object>
        {
            ["@type"] = "ProductGroup",
            ["@id"] = groupId,
            ["name"] = productName,
            ["url"] = baseUrl,
            ["productGroupID"] = $"gawela-product-{product.Id}",
            ["variesBy"] = new[] { "https://schema.org/color" },
            ["additionalProperty"] = colorCatalog,
            ["hasVariant"] = variants
        };

        var application = new Dictionary<string, object>
        {
            ["@type"] = "WebApplication",
            ["@id"] = $"{baseUrl}#gawela-ral-farbkonfigurator",
            ["name"] = "GAWELA RAL-Farbkonfigurator",
            ["url"] = baseUrl,
            ["applicationCategory"] = "DesignApplication",
            ["operatingSystem"] = "Web",
            ["isPartOf"] = new Dictionary<string, object> { ["@id"] = groupId },
            ["description"] = "Moderner interaktiver RAL-Farbkonfigurator mit Live-Farbvisualisierung direkt am Produkt.",
            ["featureList"] = new[]
            {
                "Interaktive RAL-Farbvorschau",
                "Separate Farbauswahl je konfigurierbarem Produktbereich",
                "Live-Visualisierung direkt in der Produktgalerie",
                "Semantische URLs für konkrete Farbkombinationen",
                "Serverseitig ausgegebene Produktvarianten und RAL-Farbnamen"
            }
        };

        var graph = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new object[] { group, application }
        };

        return JsonSerializer.Serialize(graph);
    }

    private static void BuildVariantsRecursive(
        string productName,
        IReadOnlyList<GawelaSeoColorAreaModel> areas,
        int index,
        List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> selected,
        string groupId,
        string baseUrl,
        List<Dictionary<string, object>> variants)
    {
        if (variants.Count >= MaxStructuredVariants)
        {
            return;
        }

        if (index >= areas.Count)
        {
            var variantUrl = BuildVariantUrl(baseUrl, selected);
            var selectionText = selected.Select(x => $"{x.Area.Name}: {x.Option.DisplayName}").ToArray();
            var properties = selected.Select(x => new Dictionary<string, object>
            {
                ["@type"] = "PropertyValue",
                ["name"] = x.Area.Name,
                ["value"] = x.Option.DisplayName,
                ["propertyID"] = $"gawela-color-area-{x.Area.ProductVariantAttributeId}"
            }).ToArray();

            variants.Add(new Dictionary<string, object>
            {
                ["@type"] = "Product",
                ["@id"] = $"{variantUrl}#gawela-product",
                ["name"] = $"{productName} – {string.Join(" – ", selectionText)}",
                ["url"] = variantUrl,
                ["color"] = string.Join(" / ", selected.Select(x => x.Option.DisplayName)),
                ["isVariantOf"] = new Dictionary<string, object> { ["@id"] = groupId },
                ["additionalProperty"] = properties
            });
            return;
        }

        var area = areas[index];
        foreach (var option in area.Options)
        {
            selected.Add((area, option));
            BuildVariantsRecursive(productName, areas, index + 1, selected, groupId, baseUrl, variants);
            selected.RemoveAt(selected.Count - 1);

            if (variants.Count >= MaxStructuredVariants)
            {
                return;
            }
        }
    }

    private static string BuildVariantUrl(
        string baseUrl,
        IEnumerable<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> selected)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var item in selected)
        {
            pairs.Add(new(item.Area.QueryKey, item.Option.ProductVariantAttributeValueId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            pairs.Add(new($"farbe-{item.Area.Slug}", item.Option.SemanticValue));
        }

        return baseUrl + "?" + string.Join("&", pairs.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private string BuildBaseUrl(ProductDetailsModel product)
    {
        if (!string.IsNullOrWhiteSpace(product.CanonicalUrl))
        {
            if (Uri.TryCreate(product.CanonicalUrl, UriKind.Absolute, out var absolute))
            {
                return absolute.GetLeftPart(UriPartial.Path);
            }

            var relative = product.CanonicalUrl.Split('?', '#')[0];
            if (relative.StartsWith('/'))
            {
                return $"{Request.Scheme}://{Request.Host}{relative}";
            }
        }

        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}";
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

    private static string Slugify(string value)
    {
        var text = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("ä", "ae")
            .Replace("ö", "oe")
            .Replace("ü", "ue")
            .Replace("ß", "ss");
        return SlugSeparatorRegex.Replace(text, "-").Trim('-');
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

# SEO/KI information is intentionally emitted only as standards-based structured data.
# There is no hidden keyword paragraph and no extra visible marketing block for customers.
(seo_views / 'Default.cshtml').write_text(r'''@using Gawela.ColorConfigurator.Models
@model GawelaSeoModel
@if (!string.IsNullOrWhiteSpace(Model.JsonLd))
{
    <script type="application/ld+json" data-gawela-structured-data="true">@Html.Raw(Model.JsonLd)</script>
}
''', encoding='utf-8')

# Keep all existing configurator behaviour and add semantic URLs as a non-invasive enhancement.
js = root / 'wwwroot' / 'gawela-color.js'
js_text = js.read_text(encoding='utf-8')
old_disclaimer = 'Bildschirmdarstellung unverbindlich. Farbabweichungen sind möglich. Die Abbildung dient der Farb- und Höhenvisualisierung; Breite, Tiefe, Proportionen, Details und die tatsächliche Ausführung können vom dargestellten Bild abweichen.'
new_disclaimer = 'Bildschirmdarstellung unverbindlich; Farbe, Proportionen, Details und Ausführung können abweichen.'
if old_disclaimer not in js_text:
    raise SystemExit('Expected original disclaimer before applying 6.4.19 patch.')
js_text = js_text.replace(old_disclaimer, new_disclaimer, 1)

selected_anchor = '''    function selected(layer) {\n        return ralFrom(choiceText(choice(layer.attributeLabel))) || layer.defaultRal;\n    }\n'''
semantic_helpers = selected_anchor + r'''

    function semanticSlug(value) {
        return (value || '')
            .trim()
            .toLowerCase()
            .replace(/ä/g, 'ae')
            .replace(/ö/g, 'oe')
            .replace(/ü/g, 'ue')
            .replace(/ß/g, 'ss')
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    function syncSemanticUrl(state) {
        try {
            const url = new URL(window.location.href);
            for (const key of [...url.searchParams.keys()]) {
                if (key.startsWith('farbe-')) url.searchParams.delete(key);
            }

            const used = new Set();
            state.layers.forEach((layer, index) => {
                let ral = selected(layer);
                if (!state.colors[ral]) ral = layer.defaultRal;
                if (!state.colors[ral]) return;

                const color = state.colors[ral];
                let area = semanticSlug(layer.name || layer.attributeLabel || ('bereich-' + (index + 1)));
                if (!area) area = 'bereich-' + (index + 1);
                if (used.has(area)) area += '-' + (index + 1);
                used.add(area);

                const colorName = semanticSlug(color.name || '');
                const semanticValue = 'ral-' + ral + (colorName ? '-' + colorName : '');
                url.searchParams.set('farbe-' + area, semanticValue);
            });

            const next = url.pathname + (url.searchParams.toString() ? '?' + url.searchParams.toString() : '') + url.hash;
            const current = window.location.pathname + window.location.search + window.location.hash;
            if (next !== current) history.replaceState(history.state, '', next);
        } catch (_) {
            // Semantic URL enrichment must never interfere with the configurator itself.
        }
    }
'''
if selected_anchor not in js_text:
    raise SystemExit('Selected-color helper insertion point missing in gawela-color.js.')
js_text = js_text.replace(selected_anchor, semantic_helpers, 1)

draw_anchor = '''        state.ctx.putImageData(output, 0, 0);\n        if (state.info) state.info.textContent = labels.join(' · ');\n        state.currentLabel = labels.join(' · ');\n        updateMobilePreview(state);\n'''
draw_replacement = '''        state.ctx.putImageData(output, 0, 0);\n        if (state.info) state.info.textContent = labels.join(' · ');\n        state.currentLabel = labels.join(' · ');\n        syncSemanticUrl(state);\n        updateMobilePreview(state);\n'''
if draw_anchor not in js_text:
    raise SystemExit('Draw insertion point missing in gawela-color.js.')
js_text = js_text.replace(draw_anchor, draw_replacement, 1)
js.write_text(js_text, encoding='utf-8')

# Keep browser cache-busting in sync with the rebuilt module version.
host = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorHost' / 'Default.cshtml'
host_text = host.read_text(encoding='utf-8')
if 'v=6.4.16' not in host_text:
    raise SystemExit('Expected 6.4.16 asset version before applying 6.4.19 patch.')
host_text = host_text.replace('v=6.4.16', 'v=6.4.19')
host.write_text(host_text, encoding='utf-8')

module = root / 'module.json'
module_text = module.read_text(encoding='utf-8')
if '"Version": "6.4.16"' not in module_text:
    raise SystemExit('Expected 6.4.16 module version before applying 6.4.19 patch.')
module_text = module_text.replace('"Version": "6.4.16"', '"Version": "6.4.19"', 1)
module.write_text(module_text, encoding='utf-8')

# Build-time regression and feature checks. These deliberately cover both the new
# machine-readable layer and existing high-value configurator capabilities.
checks = {
    'Models/GawelaSeoModel.cs': [
        'GawelaSeoColorOptionModel', 'ProductVariantAttributeValueId', 'QueryKey', 'SemanticValue'
    ],
    'Components/GawelaColorSeoViewComponent.cs': [
        'ProductGroup', 'hasVariant', 'variesBy', 'WebApplication', 'BuildControlId()',
        'ProductVariantAttributeValueId', 'farbe-', 'RAL-Farben –', 'MaxStructuredVariants'
    ],
    'Views/Shared/Components/GawelaColorSeo/Default.cshtml': [
        'application/ld+json', 'data-gawela-structured-data', 'Html.Raw(Model.JsonLd)'
    ],
    'Views/Shared/Components/GawelaColorHost/Default.cshtml': ['v=6.4.19'],
    'wwwroot/gawela-color.js': [
        new_disclaimer, 'syncSemanticUrl(state)', "url.searchParams.set('farbe-' + area, semanticValue)",
        'gawela-mobile-preview', 'smartGallery'
    ],
    'Controllers/GawelaColorAdminController.cs': [
        'SaveConfigurator', 'DeleteConfigurator', 'ProductSummaries'
    ],
    'Controllers/GawelaColorController.cs': [
        'Config', 'Asset', 'Palette', 'ResolveOwnerProductIdAsync', 'sku-fallback'
    ],
    'Services/GawelaProductGroupStore.cs': ['GawelaProductGroupStore'],
}
for rel, needles in checks.items():
    text = (root / rel).read_text(encoding='utf-8')
    for needle in needles:
        if needle not in text:
            raise SystemExit(f'6.4.19 verification failed: {needle!r} missing in {rel}')

seo_view_text = (seo_views / 'Default.cshtml').read_text(encoding='utf-8')
for forbidden in ['<section', 'RAL-Farbkonfigurator</h2>', 'bis zu @Model.CombinationCount', 'gawela-configurator-seo-note']:
    if forbidden in seo_view_text:
        raise SystemExit(f'Visible legacy SEO output still present: {forbidden!r}')

if old_disclaimer in js_text:
    raise SystemExit('Old verbose disclaimer still present after patch.')
