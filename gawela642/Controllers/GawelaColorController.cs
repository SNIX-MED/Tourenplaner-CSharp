using Microsoft.AspNetCore.Mvc;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorController : PublicController
{
    private readonly GawelaAssetStore _assetStore;
    public GawelaColorController(GawelaAssetStore assetStore) => _assetStore = assetStore;

    public IActionResult Config(int productId)
    {
        var config = _assetStore.LoadEffectiveConfig(productId);
        if (config == null || !_assetStore.IsComplete(productId)) return NotFound();
        Response.Headers["Cache-Control"] = "no-store";
        return Json(config);
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
