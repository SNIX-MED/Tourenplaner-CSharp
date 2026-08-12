from pathlib import Path
import sys

root = Path(sys.argv[1])
controller = root / 'Controllers' / 'GawelaColorController.cs'
module = root / 'module.json'

# 6.4.10: make height-template/group resolution robust when Smartstore has
# more than one product row for the same SKU or the storefront uses a
# different product ID than the one resolved while the group was saved.
# First resolve by exact Product-ID. If no direct membership/config exists,
# resolve the currently viewed SKU and compare it to the SKUs of all stored
# group-member IDs. This keeps existing product-groups.json fully compatible.
controller.write_text(r'''using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorController : PublicController
{
    private readonly SmartDbContext _db;
    private readonly GawelaAssetStore _assetStore;
    private readonly GawelaPaletteStore _paletteStore;
    private readonly GawelaProductGroupStore _groupStore;

    public GawelaColorController(
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

    public async Task<IActionResult> Config(int productId)
    {
        var resolution = await ResolveOwnerProductIdAsync(productId);
        var ownerProductId = resolution.OwnerProductId;
        var config = _assetStore.LoadEffectiveConfig(ownerProductId);

        Response.Headers["X-Gawela-Requested-ProductId"] = productId.ToString();
        Response.Headers["X-Gawela-Owner-ProductId"] = ownerProductId.ToString();
        Response.Headers["X-Gawela-Resolution"] = resolution.Mode;

        if (config == null || !_assetStore.IsComplete(ownerProductId)) return NotFound();

        // Keep the master's layer labels, but expose the currently viewed product ID.
        // The browser resolves the current product's Smartstore attributes by label,
        // therefore product-local attribute IDs may differ between group members.
        config.ProductId = productId;
        Response.Headers["Cache-Control"] = "no-store";
        return Json(config);
    }

    public IActionResult Palette()
    {
        var colors = _paletteStore.Load().ToDictionary(
            x => x.Ral,
            x => new
            {
                name = x.Name,
                hex = x.Hex,
                rgb = new[] { x.R, x.G, x.B },
                ncs = ""
            });

        Response.Headers["Cache-Control"] = "no-store";
        return Json(new
        {
            notice = "Die RGB/HEX-Werte dienen der Bildschirmdarstellung und können in der Plugin-Konfiguration gepflegt werden.",
            colors
        });
    }

    public async Task<IActionResult> Asset(int productId, string kind)
    {
        var resolution = await ResolveOwnerProductIdAsync(productId);
        var ownerProductId = resolution.OwnerProductId;
        var path = _assetStore.GetAssetPath(ownerProductId, kind);

        Response.Headers["X-Gawela-Requested-ProductId"] = productId.ToString();
        Response.Headers["X-Gawela-Owner-ProductId"] = ownerProductId.ToString();
        Response.Headers["X-Gawela-Resolution"] = resolution.Mode;

        if (path == null || !System.IO.File.Exists(path)) return NotFound();

        var contentType = kind?.Equals("base", StringComparison.OrdinalIgnoreCase) == true
            ? "image/webp"
            : "image/png";

        Response.Headers["Cache-Control"] = "public,max-age=300";
        return PhysicalFile(path, contentType);
    }

    private async Task<(int OwnerProductId, string Mode)> ResolveOwnerProductIdAsync(int productId)
    {
        if (productId <= 0) return (productId, "invalid");

        // 1) Fast path: exact numeric Product-ID is already in a height template.
        var directGroup = _groupStore.FindByProduct(productId);
        if (directGroup != null)
        {
            return (directGroup.MasterProductId, "product-id");
        }

        // 2) A product with its own complete configuration must keep using it.
        if (_assetStore.IsComplete(productId))
        {
            return (productId, "local");
        }

        // 3) Fallback for Smartstore installations where the same SKU exists on
        //    more than one product row or the storefront exposes another row ID.
        var currentSku = await _db.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => x.Sku)
            .FirstOrDefaultAsync();

        var normalizedCurrentSku = NormalizeSku(currentSku);
        if (normalizedCurrentSku.Length == 0)
        {
            return (productId, "none-no-sku");
        }

        var groups = _groupStore.Load();
        if (groups.Count == 0)
        {
            return (productId, "none-no-groups");
        }

        var memberIds = groups
            .SelectMany(x => x.ProductIds ?? new List<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (memberIds.Length == 0)
        {
            return (productId, "none-no-members");
        }

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

            if (skuGroup != null)
            {
                return (skuGroup.MasterProductId, "sku-fallback");
            }
        }

        return (productId, "none");
    }

    private static string NormalizeSku(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Ignore whitespace differences including non-breaking spaces, but preserve
        // all meaningful SKU punctuation and characters.
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }
}
''', encoding='utf-8')

m = module.read_text(encoding='utf-8')
m = m.replace('"Version": "6.4.9"', '"Version": "6.4.10"')
module.write_text(m, encoding='utf-8')
