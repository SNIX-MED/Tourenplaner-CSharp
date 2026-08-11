using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Models;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorAdminController : AdminController
{
    private readonly SmartDbContext _db;
    private readonly GawelaAssetStore _assetStore;
    public GawelaColorAdminController(SmartDbContext db,GawelaAssetStore assetStore){_db=db;_assetStore=assetStore;}

    public async Task<IActionResult> Configure(string productReference=null,int? productId=null)
    {
        ProductLookupResult product=null;
        if(productId.GetValueOrDefault()>0) product=await FindProductAsync(productId.Value.ToString());
        else if(!string.IsNullOrWhiteSpace(productReference)) product=await FindProductAsync(productReference);
        return View(await BuildModelAsync(new GawelaAssetAdminModel(),product));
    }

    [HttpPost]
    public async Task<IActionResult> Configure(GawelaAssetAdminModel model)
    {
        var product=await FindProductAsync(model.ProductId>0?model.ProductId.ToString():model.ProductReference);
        if(product==null){ModelState.AddModelError(nameof(model.ProductReference),"Produkt nicht gefunden.");return View(await BuildModelAsync(model,null));}
        var attrs=await GetAttributesAsync(product.Id);
        if(model.BaseImage==null&&!_assetStore.Exists(product.Id,"base")) ModelState.AddModelError(nameof(model.BaseImage),"Beim ersten Speichern ist das Basisbild erforderlich.");
        model.Layers=model.Layers?.Where(x=>x.ProductVariantAttributeId>0).ToList()??new();
        if(model.Layers.Count==0) ModelState.AddModelError(nameof(model.Layers),"Mindestens ein visualisierbares Smartstore-Attribut auswählen.");
        if(model.Layers.Select(x=>x.ProductVariantAttributeId).Distinct().Count()!=model.Layers.Count) ModelState.AddModelError(nameof(model.Layers),"Jedes Smartstore-Attribut darf nur einmal verwendet werden.");
        foreach(var layer in model.Layers)
        {
            var a=attrs.FirstOrDefault(x=>x.Id==layer.ProductVariantAttributeId);
            if(a==null){ModelState.AddModelError(nameof(model.Layers),"Ein ausgewähltes Attribut gehört nicht zu diesem Produkt.");continue;}
            layer.Key="a"+layer.ProductVariantAttributeId;
            layer.Name=string.IsNullOrWhiteSpace(layer.Name)?a.Name:layer.Name.Trim();
            layer.BaseRal=NormalizeRal(layer.BaseRal,"7035"); layer.DefaultRal=NormalizeRal(layer.DefaultRal,layer.BaseRal);
            var kind="layer-"+layer.Key;
            if(layer.Mask==null&&!_assetStore.Exists(product.Id,kind))
            {
                var legacy=LegacyKind(a.Name);
                if(legacy!=null&&_assetStore.Exists(product.Id,legacy)) _assetStore.CopyLegacyMaskIfNeeded(product.Id,legacy,layer.Key);
            }
            if(layer.Mask==null&&!_assetStore.Exists(product.Id,kind)) ModelState.AddModelError(nameof(model.Layers),$"Für Ebene „{layer.Name}“ ist eine PNG-Maske erforderlich.");
            try{if(layer.Mask!=null)_assetStore.ValidateMask(layer.Mask);}catch(InvalidOperationException ex){ModelState.AddModelError(nameof(model.Layers),ex.Message);}
        }
        try{if(model.BaseImage!=null)_assetStore.ValidateBase(model.BaseImage);}catch(InvalidOperationException ex){ModelState.AddModelError(nameof(model.BaseImage),ex.Message);}
        if(!ModelState.IsValid) return View(await BuildModelAsync(model,product));
        if(model.BaseImage!=null) await _assetStore.SaveBaseAsync(product.Id,model.BaseImage);
        foreach(var l in model.Layers) if(l.Mask!=null) await _assetStore.SaveLayerMaskAsync(product.Id,l.Key,l.Mask);
        var config=new GawelaProductConfig{ProductId=product.Id,ThumbnailLabel=string.IsNullOrWhiteSpace(model.ThumbnailLabel)?"Farbe konfigurieren":model.ThumbnailLabel.Trim()};
        foreach(var l in model.Layers)
        {
            var a=attrs.First(x=>x.Id==l.ProductVariantAttributeId);
            config.Layers.Add(new GawelaLayerConfig{Key=l.Key,Name=l.Name,ProductVariantAttributeId=a.Id,AttributeLabel=a.Name,AssetKind="layer-"+l.Key,BaseRal=l.BaseRal,DefaultRal=l.DefaultRal});
        }
        await _assetStore.SaveConfigAsync(config); _assetStore.DeleteUnusedLayerMasks(product.Id,config.Layers.Select(x=>x.Key));
        TempData["GawelaColor.Success"]=$"Konfigurator für {product.Sku} – {product.Name} wurde gespeichert ({config.Layers.Count} Ebene(n)).";
        return RedirectToAction(nameof(Configure),new{productId=product.Id});
    }

    [HttpPost] public IActionResult Delete(int productId){_assetStore.DeleteProductAssets(productId);TempData["GawelaColor.Success"]=$"Konfigurator für Produkt-ID {productId} wurde gelöscht.";return RedirectToAction(nameof(Configure));}

    private async Task<GawelaAssetAdminModel> BuildModelAsync(GawelaAssetAdminModel model,ProductLookupResult product)
    {
        if(product!=null)
        {
            model.ProductId=product.Id;model.ProductReference=product.Sku;model.ProductSku=product.Sku;model.ProductName=product.Name;
            model.AvailableAttributes=await GetAttributesAsync(product.Id);
            if(model.Layers.Count==0)
            {
                var cfg=_assetStore.LoadEffectiveConfig(product.Id);
                if(cfg?.Layers?.Count>0)
                {
                    foreach(var l in cfg.Layers)
                    {
                        var match=l.ProductVariantAttributeId>0?model.AvailableAttributes.FirstOrDefault(x=>x.Id==l.ProductVariantAttributeId):model.AvailableAttributes.FirstOrDefault(x=>NamesMatch(x.Name,l.AttributeLabel));
                        if(match!=null) model.Layers.Add(new GawelaLayerAdminModel{Key="a"+match.Id,Name=l.Name,ProductVariantAttributeId=match.Id,BaseRal=l.BaseRal,DefaultRal=l.DefaultRal,HasExistingMask=_assetStore.Exists(product.Id,l.AssetKind)});
                    }
                    model.ThumbnailLabel=cfg.ThumbnailLabel;
                }
            }
        }
        var ids=_assetStore.GetConfiguredProductIds().Distinct().OrderBy(x=>x).ToArray();
        if(ids.Length>0)
        {
            var ps=await _db.Products.AsNoTracking().Where(x=>ids.Contains(x.Id)).Select(x=>new{x.Id,x.Sku,x.Name}).ToListAsync();var map=ps.ToDictionary(x=>x.Id);
            foreach(var id in ids){map.TryGetValue(id,out var p);var cfg=_assetStore.LoadEffectiveConfig(id);model.ConfiguredProducts.Add(new ConfiguredProductModel{ProductId=id,Sku=p?.Sku??"(Produkt fehlt)",ProductName=p?.Name??"",HasBaseImage=_assetStore.Exists(id,"base"),LayerCount=cfg?.Layers?.Count??0,IsLegacy=_assetStore.LoadConfig(id)==null,IsComplete=_assetStore.IsComplete(id)});}
        }
        return model;
    }

    private async Task<List<GawelaAttributeOptionModel>> GetAttributesAsync(int productId)
    {
        var rows=await _db.ProductVariantAttributes.AsNoTracking().Include(x=>x.ProductAttribute).Where(x=>x.ProductId==productId).OrderBy(x=>x.DisplayOrder).ToListAsync();
        return rows.Select(x=>new GawelaAttributeOptionModel{Id=x.Id,Name=(!string.IsNullOrWhiteSpace(x.TextPrompt)?x.TextPrompt:x.ProductAttribute?.Name)??($"Attribut {x.Id}")}).ToList();
    }
    private static string NormalizeRal(string value,string fallback){var s=(value??"").Trim();if(s.StartsWith("RAL ",StringComparison.OrdinalIgnoreCase))s=s[4..].Trim();return s.Length==4&&s.All(char.IsDigit)?s:fallback;}
    private static string LegacyKind(string name){var n=(name??"").ToLowerInvariant();if(n.Contains("korpus")||n.Contains("gestell"))return"corpus";if(n.Contains("tür")||n.Contains("tuer")||n.Contains("schubl"))return"doors";return null;}
    private static bool NamesMatch(string a,string b){var x=(a??"").Trim().ToLowerInvariant();var y=(b??"").Trim().ToLowerInvariant();return x==y||x.Contains(y)||y.Contains(x);}
    private async Task<ProductLookupResult> FindProductAsync(string reference)
    {
        var r=reference?.Trim();if(string.IsNullOrWhiteSpace(r))return null;
        if(int.TryParse(r,out var id))return await _db.Products.AsNoTracking().Where(x=>x.Id==id).Select(x=>new ProductLookupResult{Id=x.Id,Sku=x.Sku,Name=x.Name}).FirstOrDefaultAsync();
        return await _db.Products.AsNoTracking().Where(x=>x.Sku==r).Select(x=>new ProductLookupResult{Id=x.Id,Sku=x.Sku,Name=x.Name}).FirstOrDefaultAsync();
    }
    private sealed class ProductLookupResult{public int Id{get;set;}public string Sku{get;set;}public string Name{get;set;}}
}
