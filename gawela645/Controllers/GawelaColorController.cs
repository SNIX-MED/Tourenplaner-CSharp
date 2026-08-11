using Microsoft.AspNetCore.Mvc;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorController : PublicController
{
    private readonly GawelaAssetStore _assetStore;
    private readonly GawelaPaletteStore _paletteStore;

    public GawelaColorController(GawelaAssetStore assetStore, GawelaPaletteStore paletteStore)
    {
        _assetStore = assetStore;
        _paletteStore = paletteStore;
    }

    public IActionResult Config(int productId)
    {
        var config = _assetStore.LoadEffectiveConfig(productId);
        if (config == null || !_assetStore.IsComplete(productId)) return NotFound();
        Response.Headers["Cache-Control"] = "no-store";
        return Json(config);
    }

    public IActionResult Palette()
    {
        var colors = _paletteStore.Load().ToDictionary(
            x => x.Ral,
            x => new { name = x.Name, hex = x.Hex, rgb = new[] { x.R, x.G, x.B }, ncs = "" });
        Response.Headers["Cache-Control"] = "no-store";
        return Json(new
        {
            notice = "Die RGB/HEX-Werte dienen der Bildschirmdarstellung und können in der Plugin-Konfiguration gepflegt werden.",
            colors
        });
    }

    public IActionResult Asset(int productId, string kind)
    {
        var path = _assetStore.GetAssetPath(productId, kind);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();
        var contentType = kind?.Equals("base", StringComparison.OrdinalIgnoreCase) == true ? "image/webp" : "image/png";
        Response.Headers["Cache-Control"] = "public,max-age=300";
        return PhysicalFile(path, contentType);
    }
}
