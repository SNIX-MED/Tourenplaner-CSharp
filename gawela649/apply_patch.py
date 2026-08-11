from pathlib import Path
import sys

root = Path(sys.argv[1])
controller = root / 'Controllers' / 'GawelaColorController.cs'
module = root / 'module.json'

# Restore the palette endpoint that was accidentally dropped when the
# product-group controller was introduced in 6.4.7. Keep group-aware
# config/asset resolution intact.
controller.write_text(r'''using Microsoft.AspNetCore.Mvc;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorController : PublicController
{
    private readonly GawelaAssetStore _assetStore;
    private readonly GawelaPaletteStore _paletteStore;
    private readonly GawelaProductGroupStore _groupStore;

    public GawelaColorController(
        GawelaAssetStore assetStore,
        GawelaPaletteStore paletteStore,
        GawelaProductGroupStore groupStore)
    {
        _assetStore = assetStore;
        _paletteStore = paletteStore;
        _groupStore = groupStore;
    }

    public IActionResult Config(int productId)
    {
        var ownerProductId = _groupStore.ResolveOwnerProductId(productId);
        var config = _assetStore.LoadEffectiveConfig(ownerProductId);
        if (config == null || !_assetStore.IsComplete(ownerProductId)) return NotFound();

        // Keep the master layer labels, but expose the currently viewed product ID.
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

    public IActionResult Asset(int productId, string kind)
    {
        var ownerProductId = _groupStore.ResolveOwnerProductId(productId);
        var path = _assetStore.GetAssetPath(ownerProductId, kind);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();

        var contentType = kind?.Equals("base", StringComparison.OrdinalIgnoreCase) == true
            ? "image/webp"
            : "image/png";

        Response.Headers["Cache-Control"] = "public,max-age=300";
        return PhysicalFile(path, contentType);
    }
}
''', encoding='utf-8')

m = module.read_text(encoding='utf-8')
m = m.replace('"Version": "6.4.8"', '"Version": "6.4.9"')
module.write_text(m, encoding='utf-8')
