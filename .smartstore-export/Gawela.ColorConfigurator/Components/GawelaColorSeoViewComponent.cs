using System.Text.Json;
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
                        SemanticValue = $"ral-{ral}{(colorName.Length > 0 ? "-" + Slugify(colorName) : string.Empty)}",
                        IsPreSelected = x.Value.IsPreSelected
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
        var currentSelection = ResolveCurrentSelection(areas);
        var productGroupId = $"gawela-product-{product.Id}";
        var currentVariantUrl = currentSelection.Count == areas.Count
            ? BuildVariantUrl(baseUrl, currentSelection)
            : baseUrl;
        var currentVariantProductId = currentSelection.Count == areas.Count
            ? BuildVariantProductId(product.Id, currentSelection)
            : productGroupId;
        var currentColorText = currentSelection.Count == areas.Count
            ? string.Join(" / ", currentSelection.Select(x => x.Option.DisplayName))
            : string.Empty;
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
            ProductGroupId = productGroupId,
            CurrentVariantUrl = currentVariantUrl,
            CurrentVariantProductId = currentVariantProductId,
            CurrentColorText = currentColorText,
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
        var productGroupId = $"gawela-product-{product.Id}";
        var variants = new List<Dictionary<string, object>>();

        if (combinations > 0)
        {
            var selected = new List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)>();
            BuildVariantsRecursive(product.Id, productName, areas, 0, selected, groupId, productGroupId, baseUrl, variants);
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
            ["productGroupID"] = productGroupId,
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
        int productId,
        string productName,
        IReadOnlyList<GawelaSeoColorAreaModel> areas,
        int index,
        List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> selected,
        string groupId,
        string productGroupId,
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
            var variantProductId = BuildVariantProductId(productId, selected);
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
                ["productID"] = variantProductId,
                ["inProductGroupWithID"] = productGroupId,
                ["color"] = string.Join(" / ", selected.Select(x => x.Option.DisplayName)),
                ["description"] = $"{productName}; {string.Join("; ", selectionText)}",
                ["isVariantOf"] = new Dictionary<string, object> { ["@id"] = groupId },
                ["additionalProperty"] = properties
            });
            return;
        }

        var area = areas[index];
        foreach (var option in area.Options)
        {
            selected.Add((area, option));
            BuildVariantsRecursive(productId, productName, areas, index + 1, selected, groupId, productGroupId, baseUrl, variants);
            selected.RemoveAt(selected.Count - 1);

            if (variants.Count >= MaxStructuredVariants)
            {
                return;
            }
        }
    }

    private List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> ResolveCurrentSelection(
        IReadOnlyList<GawelaSeoColorAreaModel> areas)
    {
        var selected = new List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)>();

        foreach (var area in areas)
        {
            GawelaSeoColorOptionModel option = null;
            if (Request.Query.TryGetValue(area.QueryKey, out var queryValues))
            {
                foreach (var raw in queryValues)
                {
                    if (int.TryParse(raw, out var valueId))
                    {
                        option = area.Options.FirstOrDefault(x => x.ProductVariantAttributeValueId == valueId);
                        if (option != null) break;
                    }
                }
            }

            option ??= area.Options.FirstOrDefault(x => x.IsPreSelected);
            option ??= area.Options.FirstOrDefault();
            if (option == null)
            {
                return new();
            }

            selected.Add((area, option));
        }

        return selected;
    }

    private static string BuildVariantProductId(
        int productId,
        IEnumerable<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> selected)
    {
        var parts = selected.Select(x =>
            $"{x.Area.ProductVariantAttributeId}-{x.Option.ProductVariantAttributeValueId}");
        return $"gawela-{productId}-{string.Join("-", parts)}";
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
