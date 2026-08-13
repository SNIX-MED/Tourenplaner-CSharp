from pathlib import Path
import sys

root = Path(sys.argv[1])

# ---------------------------------------------------------------------------
# 6.4.11: make the configurator understandable without executing JavaScript.
# A second server-rendered component is placed below the Smartstore gallery
# only on products that have a complete local/shared configurator setup.
# The visible text is also marked up with schema.org PropertyValue microdata,
# nested inside Smartstore's existing Product itemscope. This avoids creating
# a duplicate Product JSON-LD entity while still exposing the feature and
# its facts to crawlers/AI systems.
# ---------------------------------------------------------------------------

models = root / 'Models'
components = root / 'Components'
seo_views = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorSeo'
models.mkdir(parents=True, exist_ok=True)
components.mkdir(parents=True, exist_ok=True)
seo_views.mkdir(parents=True, exist_ok=True)

(models / 'GawelaSeoModel.cs').write_text(r'''namespace Gawela.ColorConfigurator.Models;

public sealed class GawelaSeoModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int ColorCount { get; set; }
    public int LayerCount { get; set; }
    public long CombinationCount { get; set; }
    public List<string> LayerNames { get; set; } = new();
    public string ResolutionMode { get; set; } = string.Empty;
}
''', encoding='utf-8')

(components / 'GawelaColorSeoViewComponent.cs').write_text(r'''using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Web.Models.Catalog;
using Gawela.ColorConfigurator.Models;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Components;

public sealed class GawelaColorSeoViewComponent : ViewComponent
{
    public const string ProductModelItemKey = "Gawela.ColorConfigurator.ProductModel";

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

        var colors = _paletteStore.Load();
        var colorCount = colors.Count;
        var layerNames = config.Layers
            .Select(x => string.IsNullOrWhiteSpace(x.Name) ? x.AttributeLabel : x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        long combinations = colorCount > 0 ? 1 : 0;
        for (var i = 0; i < config.Layers.Count && combinations > 0; i++)
        {
            if (combinations > long.MaxValue / Math.Max(1, colorCount))
            {
                combinations = 0;
                break;
            }
            combinations *= colorCount;
        }

        return View(new GawelaSeoModel
        {
            ProductId = product.Id,
            ProductName = product.Name.Value ?? string.Empty,
            Sku = product.Sku ?? string.Empty,
            ColorCount = colorCount,
            LayerCount = config.Layers.Count,
            CombinationCount = combinations,
            LayerNames = layerNames,
            ResolutionMode = resolution.Mode
        });
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

# Register the SEO component below the gallery and pass the already prepared
# ProductDetailsModel through HttpContext.Items. This keeps the SEO output
# completely server-rendered and avoids another product lookup for name/SKU.
filter_path = root / 'Filters' / 'GawelaProductDetailFilter.cs'
filter_path.write_text(r'''using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core.Widgets;
using Smartstore.Web.Models.Catalog;
using Gawela.ColorConfigurator.Components;

namespace Gawela.ColorConfigurator.Filters;

public sealed class GawelaProductDetailFilter : IAsyncResultFilter
{
    private readonly IWidgetProvider _widgetProvider;

    public GawelaProductDetailFilter(IWidgetProvider widgetProvider)
    {
        _widgetProvider = widgetProvider;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ViewResult { Model: ProductDetailsModel productModel })
        {
            context.HttpContext.Items[GawelaColorSeoViewComponent.ProductModelItemKey] = productModel;
        }

        _widgetProvider.RegisterViewComponent<GawelaColorHostViewComponent>(
            "productdetails_pictures_top",
            order: -1000);

        _widgetProvider.RegisterViewComponent<GawelaColorSeoViewComponent>(
            "productdetails_pictures_bottom",
            order: 1000);

        await next();
    }
}
''', encoding='utf-8')

(seo_views / 'Default.cshtml').write_text(r'''@using Gawela.ColorConfigurator.Models
@model GawelaSeoModel
@{
    var layerText = Model.LayerNames.Count switch
    {
        0 => "die hinterlegten Farbbereiche",
        1 => Model.LayerNames[0],
        2 => $"{Model.LayerNames[0]} und {Model.LayerNames[1]}",
        _ => string.Join(", ", Model.LayerNames.Take(Model.LayerNames.Count - 1)) + " und " + Model.LayerNames.Last()
    };
    var combinationText = Model.CombinationCount > 0 && Model.LayerCount > 1
        ? $"Bis zu {Model.CombinationCount:N0} Farbkombinationen können direkt am Produkt visualisiert werden."
        : Model.ColorCount > 0
            ? $"Es stehen {Model.ColorCount} zentral gepflegte RAL-Bildschirmfarben für die Vorschau zur Verfügung."
            : string.Empty;
}
<section class="gawela-configurator-seo" aria-labelledby="gawela-configurator-seo-title-@Model.ProductId" data-gawela-seo="true" data-resolution="@Model.ResolutionMode">
    <h2 id="gawela-configurator-seo-title-@Model.ProductId" class="gawela-configurator-seo-title">RAL-Farbkonfigurator für @Model.ProductName</h2>
    <p class="gawela-configurator-seo-text">
        Dieses Produkt lässt sich mit dem interaktiven GAWELA Farbkonfigurator direkt online visualisieren.
        @if (Model.ColorCount > 0)
        {
            <text>Für @layerText stehen jeweils @Model.ColorCount RAL-Farben zur Auswahl. </text>
        }
        @combinationText
    </p>
    <ul class="gawela-configurator-seo-facts" aria-label="Funktionen des Farbkonfigurators">
        <li itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
            <span itemprop="name">Interaktiver RAL-Farbkonfigurator</span>:
            <strong itemprop="value">verfügbar</strong>
        </li>
        <li itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
            <span itemprop="name">RAL-Farben für die Bildschirmvorschau</span>:
            <strong itemprop="value">@Model.ColorCount</strong>
        </li>
        <li itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
            <span itemprop="name">Konfigurierbare Farbbereiche</span>:
            <strong itemprop="value">@string.Join(", ", Model.LayerNames)</strong>
        </li>
        @if (Model.CombinationCount > 0 && Model.LayerCount > 1)
        {
            <li itemprop="additionalProperty" itemscope itemtype="https://schema.org/PropertyValue">
                <span itemprop="name">Mögliche Farbkombinationen in der Vorschau</span>:
                <strong itemprop="value">@Model.CombinationCount</strong>
            </li>
        }
    </ul>
    <p class="gawela-configurator-seo-note">Die Bildschirmdarstellung dient der visuellen Orientierung. Farbwirkung, Proportionen, Details und tatsächliche Ausführung können von der dargestellten Vorschau abweichen.</p>
</section>
''', encoding='utf-8')

css_path = root / 'wwwroot' / 'gawela-color.css'
css = css_path.read_text(encoding='utf-8')
seo_css = r'''

/* 6.4.11: server-rendered SEO/AI feature text below the product gallery. */
.gawela-configurator-seo{margin-top:1rem;padding:.85rem 1rem;border:1px solid var(--gray-300,#dee2e6);border-radius:.5rem;background:var(--gray-100,#f8f9fa);font-size:.875rem;line-height:1.45}
.gawela-configurator-seo-title{margin:0 0 .35rem;font-size:1rem;font-weight:700}
.gawela-configurator-seo-text{margin:0 0 .45rem}
.gawela-configurator-seo-facts{display:flex;flex-wrap:wrap;gap:.25rem .9rem;margin:0;padding:0;list-style:none;color:var(--gray-700,#495057)}
.gawela-configurator-seo-facts li{margin:0}
.gawela-configurator-seo-note{margin:.45rem 0 0;color:var(--gray-600,#6c757d);font-size:.8rem}
@media(max-width:767.98px){.gawela-configurator-seo{padding:.75rem}.gawela-configurator-seo-facts{display:block}.gawela-configurator-seo-facts li+li{margin-top:.2rem}}
'''
if 'server-rendered SEO/AI feature text' not in css:
    css += seo_css
css_path.write_text(css, encoding='utf-8')

module_path = root / 'module.json'
module = module_path.read_text(encoding='utf-8')
module = module.replace('"Version": "6.4.10"', '"Version": "6.4.11"')
module_path.write_text(module, encoding='utf-8')
