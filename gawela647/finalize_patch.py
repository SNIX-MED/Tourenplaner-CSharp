from pathlib import Path
import sys

root = Path(sys.argv[1])
view = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
module = root / 'module.json'

s = view.read_text(encoding='utf-8')

if 'TempData["GawelaColor.Error"]' not in s:
    s = s.replace('@if (TempData["GawelaColor.Success"] is string success){<div class="alert alert-success">@success</div>}',
'''@if (TempData["GawelaColor.Success"] is string success){<div class="alert alert-success">@success</div>}
@if (TempData["GawelaColor.Error"] is string error){<div class="alert alert-danger">@error</div>}''', 1)

outer = '  @if(Model.ProductId>0)\n  {\n  @if(Model.ConfiguredProducts.Any(x => x.ProductId != Model.ProductId))'
replacement = '''  @if(Model.ProductId>0 && Model.GroupMasterProductId>0 && !Model.IsGroupMaster)
  {
    <div class="alert alert-info">
      <strong>Gemeinsame Produktgruppe:</strong> Dieses Produkt verwendet die Konfiguration „@Model.GroupName“ des Leitprodukts <strong>@Model.GroupMasterSku</strong> (Product-ID @Model.GroupMasterProductId). Basisbild, Masken, Ebenen und RAL-Vorgaben werden zentral dort gepflegt.
      <div class="mt-2"><a asp-action="Configure" asp-route-productId="@Model.GroupMasterProductId" asp-route-tab="products" class="btn btn-sm btn-primary">Leitprodukt / Gruppe bearbeiten</a></div>
    </div>
  }
  else if(Model.ProductId>0)
  {
  @if(Model.ConfiguredProducts.Any(x => x.ProductId != Model.ProductId))'''
if 'Gemeinsame Produktgruppe:' not in s:
    if outer not in s:
        raise SystemExit('Outer product form condition not found')
    s = s.replace(outer, replacement, 1)

needle = '  <hr/><h3 class="h5 mb-3">Bereits konfigurierte Produkte</h3>'
if 'Produktgruppe – gemeinsame Bilder und Masken' not in s:
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
        raise SystemExit('Configured products insertion point not found')
    s = s.replace(needle, group_ui, 1)

view.write_text(s, encoding='utf-8')

m = module.read_text(encoding='utf-8')
m = m.replace('"Version": "6.4.6"', '"Version": "6.4.7"')
module.write_text(m, encoding='utf-8')
