from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
model = root / 'Models' / 'GawelaSeoModel.cs'
component = root / 'Components' / 'GawelaColorSeoViewComponent.cs'
view = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorSeo' / 'Default.cshtml'
host = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorHost' / 'Default.cshtml'
module = root / 'module.json'

# Extend the 6.4.19 machine model with the exact currently rendered variant.
text = model.read_text(encoding='utf-8')
text = text.replace(
    '    public string JsonLd { get; set; } = string.Empty;\n    public List<GawelaSeoColorAreaModel> ColorAreas { get; set; } = new();',
    '    public string JsonLd { get; set; } = string.Empty;\n'
    '    public string ProductGroupId { get; set; } = string.Empty;\n'
    '    public string CurrentVariantUrl { get; set; } = string.Empty;\n'
    '    public string CurrentVariantProductId { get; set; } = string.Empty;\n'
    '    public string CurrentColorText { get; set; } = string.Empty;\n'
    '    public List<GawelaSeoColorAreaModel> ColorAreas { get; set; } = new();'
)
text = text.replace(
    '    public string SemanticValue { get; set; } = string.Empty;\n}',
    '    public string SemanticValue { get; set; } = string.Empty;\n'
    '    public bool IsPreSelected { get; set; }\n'
    '}'
)
model.write_text(text, encoding='utf-8')

text = component.read_text(encoding='utf-8')

text = text.replace(
    '                        SemanticValue = $"ral-{ral}{(colorName.Length > 0 ? "-" + Slugify(colorName) : string.Empty)}"\n',
    '                        SemanticValue = $"ral-{ral}{(colorName.Length > 0 ? "-" + Slugify(colorName) : string.Empty)}",\n'
    '                        IsPreSelected = x.Value.IsPreSelected\n'
)

old = '''        var baseUrl = BuildBaseUrl(product);\n        var jsonLd = BuildJsonLd(product, areas, baseUrl, combinations);\n\n        return View(new GawelaSeoModel\n        {'''
new = '''        var baseUrl = BuildBaseUrl(product);\n        var currentSelection = ResolveCurrentSelection(areas);\n        var productGroupId = $"gawela-product-{product.Id}";\n        var currentVariantUrl = currentSelection.Count == areas.Count\n            ? BuildVariantUrl(baseUrl, currentSelection)\n            : baseUrl;\n        var currentVariantProductId = currentSelection.Count == areas.Count\n            ? BuildVariantProductId(product.Id, currentSelection)\n            : productGroupId;\n        var currentColorText = currentSelection.Count == areas.Count\n            ? string.Join(" / ", currentSelection.Select(x => x.Option.DisplayName))\n            : string.Empty;\n        var jsonLd = BuildJsonLd(product, areas, baseUrl, combinations);\n\n        return View(new GawelaSeoModel\n        {'''
if old not in text:
    raise SystemExit('6.4.20: return preparation anchor missing')
text = text.replace(old, new, 1)

text = text.replace(
    '            JsonLd = jsonLd,\n            ColorAreas = areas',
    '            JsonLd = jsonLd,\n'
    '            ProductGroupId = productGroupId,\n'
    '            CurrentVariantUrl = currentVariantUrl,\n'
    '            CurrentVariantProductId = currentVariantProductId,\n'
    '            CurrentColorText = currentColorText,\n'
    '            ColorAreas = areas'
)

text = text.replace(
    '        var groupId = $"{baseUrl}#gawela-ral-variants";\n        var variants = new List<Dictionary<string, object>>();',
    '        var groupId = $"{baseUrl}#gawela-ral-variants";\n'
    '        var productGroupId = $"gawela-product-{product.Id}";\n'
    '        var variants = new List<Dictionary<string, object>>();'
)

text = text.replace(
    '            BuildVariantsRecursive(productName, areas, 0, selected, groupId, baseUrl, variants);',
    '            BuildVariantsRecursive(product.Id, productName, areas, 0, selected, groupId, productGroupId, baseUrl, variants);'
)
text = text.replace(
    '["productGroupID"] = $"gawela-product-{product.Id}",',
    '["productGroupID"] = productGroupId,'
)

text = text.replace(
    '''    private static void BuildVariantsRecursive(\n        string productName,\n        IReadOnlyList<GawelaSeoColorAreaModel> areas,\n        int index,\n        List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> selected,\n        string groupId,\n        string baseUrl,\n        List<Dictionary<string, object>> variants)''',
    '''    private static void BuildVariantsRecursive(\n        int productId,\n        string productName,\n        IReadOnlyList<GawelaSeoColorAreaModel> areas,\n        int index,\n        List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> selected,\n        string groupId,\n        string productGroupId,\n        string baseUrl,\n        List<Dictionary<string, object>> variants)'''
)

text = text.replace(
    '''            var variantUrl = BuildVariantUrl(baseUrl, selected);\n            var selectionText = selected.Select(x => $"{x.Area.Name}: {x.Option.DisplayName}").ToArray();''',
    '''            var variantUrl = BuildVariantUrl(baseUrl, selected);\n            var variantProductId = BuildVariantProductId(productId, selected);\n            var selectionText = selected.Select(x => $"{x.Area.Name}: {x.Option.DisplayName}").ToArray();'''
)

text = text.replace(
    '''                ["url"] = variantUrl,\n                ["color"] = string.Join(" / ", selected.Select(x => x.Option.DisplayName)),\n                ["isVariantOf"] = new Dictionary<string, object> { ["@id"] = groupId },\n                ["additionalProperty"] = properties''',
    '''                ["url"] = variantUrl,\n                ["productID"] = variantProductId,\n                ["inProductGroupWithID"] = productGroupId,\n                ["color"] = string.Join(" / ", selected.Select(x => x.Option.DisplayName)),\n                ["description"] = $"{productName}; {string.Join("; ", selectionText)}",\n                ["isVariantOf"] = new Dictionary<string, object> { ["@id"] = groupId },\n                ["additionalProperty"] = properties'''
)

text = text.replace(
    '            BuildVariantsRecursive(productName, areas, index + 1, selected, groupId, baseUrl, variants);',
    '            BuildVariantsRecursive(productId, productName, areas, index + 1, selected, groupId, productGroupId, baseUrl, variants);'
)

anchor = '''    private static string BuildVariantUrl(\n        string baseUrl,\n        IEnumerable<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> selected)\n    {'''
helpers = r'''    private List<(GawelaSeoColorAreaModel Area, GawelaSeoColorOptionModel Option)> ResolveCurrentSelection(
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

'''
if anchor not in text:
    raise SystemExit('6.4.20: BuildVariantUrl anchor missing')
text = text.replace(anchor, helpers + anchor, 1)
component.write_text(text, encoding='utf-8')

# Enrich Smartstore's own server-rendered Product JSON-LD. Smartstore itself contributes
# the real product name, image, SKU, Offer price, currency and availability. We only add
# the exact current colour variant facts and group relation, avoiding duplicate/fake offers.
view.write_text(r'''@using Gawela.ColorConfigurator.Models
@model GawelaSeoModel
@{
    if (!string.IsNullOrWhiteSpace(Model.CurrentVariantUrl))
    {
        Assets.JsonLd.Product
            .Prop("url", Model.CurrentVariantUrl)
            .Prop("color", Model.CurrentColorText)
            .Prop("productID", Model.CurrentVariantProductId)
            .Prop("inProductGroupWithID", Model.ProductGroupId);
    }
}
@if (!string.IsNullOrWhiteSpace(Model.JsonLd))
{
    <script type="application/ld+json" data-gawela-structured-data="true">@Html.Raw(Model.JsonLd)</script>
}
''', encoding='utf-8')

host_text = host.read_text(encoding='utf-8')
if 'v=6.4.19' not in host_text:
    raise SystemExit('Expected 6.4.19 asset version before applying 6.4.20 patch.')
host.write_text(host_text.replace('v=6.4.19', 'v=6.4.20'), encoding='utf-8')

module_text = module.read_text(encoding='utf-8')
if '"Version": "6.4.19"' not in module_text:
    raise SystemExit('Expected 6.4.19 module version before applying 6.4.20 patch.')
module.write_text(module_text.replace('"Version": "6.4.19"', '"Version": "6.4.20"', 1), encoding='utf-8')

checks = {
    'Models/GawelaSeoModel.cs': ['CurrentVariantUrl', 'CurrentVariantProductId', 'CurrentColorText', 'IsPreSelected'],
    'Components/GawelaColorSeoViewComponent.cs': [
        'ResolveCurrentSelection', 'BuildVariantProductId', 'productID', 'inProductGroupWithID',
        'ProductVariantAttributeValueId', 'BuildControlId()', 'ProductGroup', 'hasVariant'
    ],
    'Views/Shared/Components/GawelaColorSeo/Default.cshtml': [
        'Assets.JsonLd.Product', '.Prop("color", Model.CurrentColorText)',
        '.Prop("inProductGroupWithID", Model.ProductGroupId)', 'application/ld+json'
    ],
    'Views/Shared/Components/GawelaColorHost/Default.cshtml': ['v=6.4.20'],
}
for rel, needles in checks.items():
    data = (root / rel).read_text(encoding='utf-8')
    for needle in needles:
        if needle not in data:
            raise SystemExit(f'6.4.20 verification failed: {needle!r} missing in {rel}')

# Customer-facing SEO prose remains forbidden: only the actual configurator selection and
# the short disclaimer from gawela-color.js may be visible.
seo_view = view.read_text(encoding='utf-8')
for forbidden in ['<section', '<h2', 'gawela-configurator-seo-text', 'gawela-configurator-seo-note']:
    if forbidden in seo_view:
        raise SystemExit(f'Visible SEO block unexpectedly present: {forbidden}')
