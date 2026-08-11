from pathlib import Path
import re, sys

root = Path(sys.argv[1])

# ---------------- Startup ----------------
p = root / 'Startup.cs'
s = p.read_text(encoding='utf-8')
if 'GawelaProductGroupStore' not in s:
    s = s.replace('services.AddSingleton<GawelaPaletteStore>();', 'services.AddSingleton<GawelaPaletteStore>();\n        services.AddSingleton<GawelaProductGroupStore>();')
p.write_text(s, encoding='utf-8')

# ---------------- Admin model ----------------
p = root / 'Models' / 'GawelaAssetAdminModel.cs'
s = p.read_text(encoding='utf-8')
needle = '    public string TemplateMessage { get; set; }\n    public string ThumbnailLabel'
if needle in s:
    s = s.replace(needle, '    public string TemplateMessage { get; set; }\n    public string GroupName { get; set; }\n    public string GroupMembersText { get; set; }\n    public int GroupMasterProductId { get; set; }\n    public string GroupMasterSku { get; set; }\n    public bool IsGroupMaster { get; set; }\n    public string ThumbnailLabel', 1)
elif 'public string GroupName' not in s:
    raise SystemExit('Admin model insertion point not found')
p.write_text(s, encoding='utf-8')

# ---------------- Public controller: shared assets/config ----------------
p = root / 'Controllers' / 'GawelaColorController.cs'
p.write_text(r'''using Microsoft.AspNetCore.Mvc;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorController : PublicController
{
    private readonly GawelaAssetStore _assetStore;
    private readonly GawelaProductGroupStore _groupStore;

    public GawelaColorController(GawelaAssetStore assetStore, GawelaProductGroupStore groupStore)
    {
        _assetStore = assetStore;
        _groupStore = groupStore;
    }

    public IActionResult Config(int productId)
    {
        var ownerProductId = _groupStore.ResolveOwnerProductId(productId);
        var config = _assetStore.LoadEffectiveConfig(ownerProductId);
        if (config == null || !_assetStore.IsComplete(ownerProductId)) return NotFound();
        // AttributeLabel is deliberately retained from the group master. The frontend resolves
        // the product-local Smartstore attribute by label, so differing attribute IDs are safe.
        config.ProductId = productId;
        Response.Headers["Cache-Control"] = "no-store";
        return Json(config);
    }

    public IActionResult Asset(int productId, string kind)
    {
        var ownerProductId = _groupStore.ResolveOwnerProductId(productId);
        var path = _assetStore.GetAssetPath(ownerProductId, kind);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();
        var contentType = kind?.Equals("base", StringComparison.OrdinalIgnoreCase) == true ? "image/webp" : "image/png";
        Response.Headers["Cache-Control"] = "public,max-age=300";
        return PhysicalFile(path, contentType);
    }
}
''', encoding='utf-8')

# ---------------- Admin controller ----------------
p = root / 'Controllers' / 'GawelaColorAdminController.cs'
s = p.read_text(encoding='utf-8')

s = s.replace(
    '    private readonly GawelaPaletteStore _paletteStore;\n\n    public GawelaColorAdminController(SmartDbContext db, GawelaAssetStore assetStore, GawelaPaletteStore paletteStore)',
    '    private readonly GawelaPaletteStore _paletteStore;\n    private readonly GawelaProductGroupStore _groupStore;\n\n    public GawelaColorAdminController(SmartDbContext db, GawelaAssetStore assetStore, GawelaPaletteStore paletteStore, GawelaProductGroupStore groupStore)'
)
s = s.replace(
    '        _paletteStore = paletteStore;\n    }',
    '        _paletteStore = paletteStore;\n        _groupStore = groupStore;\n    }', 1
)

# Add group actions before Delete action.
needle = '    [HttpPost]\n    public IActionResult Delete(int productId)'
insert = r'''    [HttpPost]
    public async Task<IActionResult> SaveGroup(int masterProductId, string groupName, string productReferences)
    {
        var master = await FindProductAsync(masterProductId.ToString());
        if (master == null)
        {
            TempData["GawelaColor.Error"] = "Leitprodukt der Produktgruppe wurde nicht gefunden.";
            return RedirectToAction(nameof(Configure));
        }

        var existingMembership = _groupStore.FindByProduct(masterProductId);
        if (existingMembership != null && existingMembership.MasterProductId != masterProductId)
        {
            TempData["GawelaColor.Error"] = $"Das gewählte Leitprodukt gehört bereits zur Produktgruppe „{existingMembership.Name}“.";
            return RedirectToAction(nameof(Configure), new { productId = masterProductId, tab = "products" });
        }

        var config = _assetStore.LoadEffectiveConfig(masterProductId);
        if (config?.Layers?.Count == 0 || !_assetStore.IsComplete(masterProductId))
        {
            TempData["GawelaColor.Error"] = "Das Leitprodukt muss zuerst vollständig mit Basisbild, Ebenen und Masken konfiguriert sein.";
            return RedirectToAction(nameof(Configure), new { productId = masterProductId, tab = "products" });
        }

        var refs = SplitProductReferences(productReferences).ToList();
        var products = new List<ProductLookupResult> { master };
        foreach (var reference in refs)
        {
            var product = await FindProductAsync(reference);
            if (product == null)
            {
                TempData["GawelaColor.Error"] = $"Produkt „{reference}“ wurde nicht gefunden.";
                return RedirectToAction(nameof(Configure), new { productId = masterProductId, tab = "products" });
            }
            if (products.All(x => x.Id != product.Id)) products.Add(product);
        }

        foreach (var product in products.Where(x => x.Id != masterProductId))
        {
            var membership = _groupStore.FindByProduct(product.Id);
            if (membership != null && membership.MasterProductId != masterProductId)
            {
                TempData["GawelaColor.Error"] = $"{product.Sku} gehört bereits zur Produktgruppe „{membership.Name}“.";
                return RedirectToAction(nameof(Configure), new { productId = masterProductId, tab = "products" });
            }

            var attrs = await GetAttributesAsync(product.Id);
            var missing = config.Layers
                .Where(layer => !attrs.Any(a => NamesMatch(a.Name, layer.AttributeLabel)))
                .Select(layer => layer.AttributeLabel ?? layer.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
            if (missing.Count > 0)
            {
                TempData["GawelaColor.Error"] = $"{product.Sku}: folgende Farbattribute des Leitprodukts wurden nicht gefunden: {string.Join(", ", missing)}.";
                return RedirectToAction(nameof(Configure), new { productId = masterProductId, tab = "products" });
            }
        }

        await _groupStore.SaveAsync(new GawelaProductGroup
        {
            Name = string.IsNullOrWhiteSpace(groupName) ? $"Produktgruppe {master.Sku}" : groupName.Trim(),
            MasterProductId = masterProductId,
            ProductIds = products.Select(x => x.Id).Distinct().ToList()
        });

        TempData["GawelaColor.Success"] = $"Produktgruppe gespeichert: {products.Count} Produkt(e) verwenden nun gemeinsam Basisbild, Masken, Ebenen und RAL-Vorgaben des Leitprodukts {master.Sku}.";
        return RedirectToAction(nameof(Configure), new { productId = masterProductId, tab = "products" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteGroup(int masterProductId)
    {
        await _groupStore.DeleteAsync(masterProductId);
        TempData["GawelaColor.Success"] = "Produktgruppe wurde aufgehoben. Die Konfiguration des bisherigen Leitprodukts bleibt erhalten.";
        return RedirectToAction(nameof(Configure), new { productId = masterProductId, tab = "products" });
    }

'''
if needle not in s:
    raise SystemExit('Delete action insertion point not found')
s = s.replace(needle, insert + needle, 1)

# Make Delete async and clean group assignment when deleting a product config.
s = s.replace('    public IActionResult Delete(int productId)\n    {\n        _assetStore.DeleteProductAssets(productId);',
              '    public async Task<IActionResult> Delete(int productId)\n    {\n        await _groupStore.DeleteAsync(productId);\n        await _groupStore.RemoveProductAsync(productId);\n        _assetStore.DeleteProductAssets(productId);', 1)

# Populate group state for currently loaded product.
needle = '            model.ProductName = product.Name;\n            model.AvailableAttributes = await GetAttributesAsync(product.Id);'
replacement = '''            model.ProductName = product.Name;
            model.AvailableAttributes = await GetAttributesAsync(product.Id);

            var membership = _groupStore.FindByProduct(product.Id);
            if (membership != null)
            {
                model.GroupMasterProductId = membership.MasterProductId;
                model.IsGroupMaster = membership.MasterProductId == product.Id;
                model.GroupName = membership.Name;
                var memberRows = await _db.Products.AsNoTracking()
                    .Where(x => membership.ProductIds.Contains(x.Id))
                    .Select(x => new { x.Id, x.Sku, x.Name })
                    .ToListAsync();
                var masterRow = memberRows.FirstOrDefault(x => x.Id == membership.MasterProductId);
                model.GroupMasterSku = masterRow?.Sku ?? membership.MasterProductId.ToString();
                model.GroupMembersText = string.Join(Environment.NewLine,
                    memberRows.Where(x => x.Id != membership.MasterProductId).OrderBy(x => x.Sku).Select(x => x.Sku));
            }
'''
if needle not in s:
    raise SystemExit('BuildModel group insertion point not found')
s = s.replace(needle, replacement, 1)

# If a group member (not master) is loaded, show the master's effective configuration read-only via UI.
# BuildModel itself should not create local layers for a member, therefore replace config source product ID.
s = s.replace('                var cfg = _assetStore.LoadEffectiveConfig(product.Id);',
              '                var configProductId = model.GroupMasterProductId > 0 ? model.GroupMasterProductId : product.Id;\n                var cfg = _assetStore.LoadEffectiveConfig(configProductId);', 1)
s = s.replace('                                HasExistingMask = _assetStore.Exists(product.Id, l.AssetKind)',
              '                                HasExistingMask = _assetStore.Exists(configProductId, l.AssetKind)', 1)

# Add reference parser before FindProductAsync.
needle = '    private async Task<ProductLookupResult> FindProductAsync(string reference)'
insert = '''    private static IEnumerable<string> SplitProductReferences(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { '\\r', '\\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

'''
if needle not in s:
    raise SystemExit('Reference parser insertion point not found')
s = s.replace(needle, insert + needle, 1)
p.write_text(s, encoding='utf-8')

# ---------------- Admin view ----------------
p = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
s = p.read_text(encoding='utf-8')

# Show TempData errors.
s = s.replace('@if (TempData["GawelaColor.Success"] is string success){<div class="alert alert-success">@success</div>}',
'''@if (TempData["GawelaColor.Success"] is string success){<div class="alert alert-success">@success</div>}
@if (TempData["GawelaColor.Error"] is string error){<div class="alert alert-danger">@error</div>}''', 1)

# Turn member products into a clear read-only group notice instead of an editable local config.
first_if = '  @if(Model.ProductId>0)\n  {\n  <form id="gawela-config-form"'
replacement = '''  @if(Model.ProductId>0 && Model.GroupMasterProductId>0 && !Model.IsGroupMaster)
  {
    <div class="alert alert-info">
      <strong>Gemeinsame Produktgruppe:</strong> Dieses Produkt verwendet die Konfiguration „@Model.GroupName“ des Leitprodukts <strong>@Model.GroupMasterSku</strong> (Product-ID @Model.GroupMasterProductId). Basisbild, Masken, Ebenen und RAL-Vorgaben werden zentral dort gepflegt.
      <div class="mt-2"><a asp-action="Configure" asp-route-productId="@Model.GroupMasterProductId" asp-route-tab="products" class="btn btn-sm btn-primary">Leitprodukt / Gruppe bearbeiten</a></div>
    </div>
  }
  else if(Model.ProductId>0)
  {
  <form id="gawela-config-form"'''
if first_if not in s:
    raise SystemExit('Main product form condition not found')
s = s.replace(first_if, replacement, 1)

# Add group management card before configured product list.
needle = '  <hr/><h3 class="h5 mb-3">Bereits konfigurierte Produkte</h3>'
group_ui = r'''  @if(Model.ProductId>0 && (Model.GroupMasterProductId==0 || Model.IsGroupMaster))
  {
    <hr/>
    <div class="card mb-4"><div class="card-body">
      <h3 class="h5">Produktgruppe – gemeinsame Bilder und Masken</h3>
      <p class="text-muted">Verwenden mehrere Artikel exakt dieselbe Bildgeometrie und dieselben Masken, können Sie sie hier zu einer Gruppe zusammenfassen. Das aktuell geladene Produkt ist das <strong>Leitprodukt</strong>. Basisbild, Masken, Ebenen sowie Basis-/Fallback-RAL werden nur beim Leitprodukt gespeichert und von allen Gruppenmitgliedern gemeinsam verwendet.</p>
      <div class="alert alert-warning py-2"><strong>Wichtig:</strong> Nur Produkte gruppieren, deren Konfiguratorbild pixelgenau dieselbe Geometrie/Perspektive besitzt. Die Zielprodukte müssen dieselben Farbattribute besitzen; unterschiedliche produktbezogene Attribut-IDs sind erlaubt.</div>
      <form asp-action="SaveGroup" method="post">
        <input type="hidden" name="masterProductId" value="@Model.ProductId"/>
        <div class="form-group"><label>Gruppenname</label><input name="groupName" value="@Model.GroupName" class="form-control" style="max-width:640px" placeholder="z.B. Garderobenschrank 2-türig – gemeinsame Maske"/></div>
        <div class="form-group"><label>Weitere Artikelnummern oder Product-IDs</label><textarea name="productReferences" class="form-control" rows="6" placeholder="Eine Artikelnummer oder Product-ID pro Zeile">@Model.GroupMembersText</textarea><small class="text-muted">Das Leitprodukt selbst muss nicht eingetragen werden. Sie können Artikelnummern/SKUs oder numerische Product-IDs verwenden.</small></div>
        <button type="submit" class="btn btn-warning"><i class="fa fa-users"></i> Produktgruppe speichern</button>
      </form>
      @if(Model.IsGroupMaster)
      {
        <form asp-action="DeleteGroup" method="post" class="mt-3" onsubmit="return confirm('Produktgruppe wirklich aufheben? Die Konfiguration des Leitprodukts bleibt bestehen.');">
          <input type="hidden" name="masterProductId" value="@Model.ProductId"/>
          <button type="submit" class="btn btn-outline-danger"><i class="fa fa-unlink"></i> Produktgruppe aufheben</button>
        </form>
      }
    </div></div>
  }

  <hr/><h3 class="h5 mb-3">Bereits konfigurierte Produkte</h3>'''
if needle not in s:
    raise SystemExit('Group UI insertion point not found')
s = s.replace(needle, group_ui, 1)
p.write_text(s, encoding='utf-8')

# ---------------- Version ----------------
p = root / 'module.json'
s = p.read_text(encoding='utf-8')
s = s.replace('"Version": "6.4.6"', '"Version": "6.4.7"')
p.write_text(s, encoding='utf-8')
