using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Models;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorController : PublicController
{
    private readonly SmartDbContext _db;
    private readonly IMediaService _mediaService;
    private readonly GawelaAssetStore _assetStore;
    private readonly GawelaPaletteStore _paletteStore;
    private readonly GawelaProductGroupStore _groupStore;

    public GawelaColorController(
        SmartDbContext db,
        IMediaService mediaService,
        GawelaAssetStore assetStore,
        GawelaPaletteStore paletteStore,
        GawelaProductGroupStore groupStore)
    {
        _db = db;
        _mediaService = mediaService;
        _assetStore = assetStore;
        _paletteStore = paletteStore;
        _groupStore = groupStore;
    }

    public async Task<IActionResult> Config(int productId)
    {
        var resolution = await ResolveOwnerProductIdAsync(productId);
        var ownerProductId = resolution.OwnerProductId;
        var config = _assetStore.LoadEffectiveConfig(ownerProductId);

        AddDiagnostics(productId, ownerProductId, resolution.Mode);
        if (config == null || !_assetStore.IsComplete(ownerProductId)) return NotFound();

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
        var config = _assetStore.LoadEffectiveConfig(ownerProductId);

        AddDiagnostics(productId, ownerProductId, resolution.Mode);

        var mediaFileId = GetMediaFileId(config, kind);
        if (mediaFileId.GetValueOrDefault() > 0)
        {
            var mediaFile = await _mediaService.GetFileByIdAsync(mediaFileId.Value);
            if (mediaFile != null)
            {
                var stream = await mediaFile.OpenReadAsync();
                Response.Headers["Cache-Control"] = "public,max-age=300";
                return File(stream, mediaFile.MimeType ?? GuessContentType(kind));
            }
        }

        // Backwards-compatible fallback for all configurations created before 6.4.15.
        var path = _assetStore.GetAssetPath(ownerProductId, kind);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();

        Response.Headers["Cache-Control"] = "public,max-age=300";
        return PhysicalFile(path, GuessContentType(kind));
    }

    private static int? GetMediaFileId(GawelaProductConfig config, string kind)
    {
        if (config == null || string.IsNullOrWhiteSpace(kind)) return null;
        if (kind.Equals("base", StringComparison.OrdinalIgnoreCase)) return config.BaseMediaFileId;

        return config.Layers?
            .FirstOrDefault(x => string.Equals(x.AssetKind, kind, StringComparison.OrdinalIgnoreCase))?
            .MaskMediaFileId;
    }

    private static string GuessContentType(string kind)
        => kind?.Equals("base", StringComparison.OrdinalIgnoreCase) == true ? "image/webp" : "image/png";

    private void AddDiagnostics(int requestedProductId, int ownerProductId, string mode)
    {
        Response.Headers["X-Gawela-Requested-ProductId"] = requestedProductId.ToString();
        Response.Headers["X-Gawela-Owner-ProductId"] = ownerProductId.ToString();
        Response.Headers["X-Gawela-Resolution"] = mode;
    }

    private async Task<(int OwnerProductId, string Mode)> ResolveOwnerProductIdAsync(int productId)
    {
        if (productId <= 0) return (productId, "invalid");

        var directGroup = _groupStore.FindByProduct(productId);
        if (directGroup != null)
            return (directGroup.MasterProductId, "product-id");

        if (_assetStore.IsComplete(productId))
            return (productId, "local");

        var currentSku = await _db.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => x.Sku)
            .FirstOrDefaultAsync();

        var normalizedCurrentSku = NormalizeSku(currentSku);
        if (normalizedCurrentSku.Length == 0)
            return (productId, "none-no-sku");

        var groups = _groupStore.Load();
        if (groups.Count == 0)
            return (productId, "none-no-groups");

        var memberIds = groups
            .SelectMany(x => x.ProductIds ?? new List<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (memberIds.Length == 0)
            return (productId, "none-no-members");

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
                return (skuGroup.MasterProductId, "sku-fallback");
        }

        return (productId, "none");
    }

    private static string NormalizeSku(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }
}
