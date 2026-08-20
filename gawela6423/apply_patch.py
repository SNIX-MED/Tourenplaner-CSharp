from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
controller = root / 'Controllers' / 'GawelaColorAdminController.cs'
view = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
host = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorHost' / 'Default.cshtml'
module = root / 'module.json'

# 6.4.23: the lower member list is the authoritative assignment.
# The SKU textarea is only a staging field used to add products to that list.

text = controller.read_text(encoding='utf-8')
start = text.index('        var existingAdditionalIds = currentMemberIds\n')
end = text.index('\n        var otherGroups = _groupStore.Load().Where(x => x.MasterProductId != master.Id).ToList();', start)
new = r'''        var existingAdditionalIds = currentMemberIds
            .Where(x => x != master.Id)
            .Distinct()
            .ToList();

        // 6.4.23: AdditionalProductIds is the exact, authoritative assignment list.
        // The SKU textarea is only used client-side to resolve and append products before saving.
        var additionalIds = ParseProductIds(model.AdditionalProductIds)
            .Where(x => x != master.Id)
            .Distinct()
            .ToList();
        var memberRows = additionalIds.Count == 0
            ? new List<ProductLookupResult>()
            : await _db.Products.AsNoTracking()
                .Where(x => additionalIds.Contains(x.Id))
                .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
                .ToListAsync();
        var missingMemberIds = additionalIds.Except(memberRows.Select(x => x.Id)).ToList();
        if (missingMemberIds.Count > 0)
            ModelState.AddModelError(nameof(model.AdditionalProductIds), "Mindestens ein zugeordneter Artikel wurde im Produktkatalog nicht gefunden.");

        model.AdditionalProductIds = string.Join(',', additionalIds);
        model.AdditionalProductSkus = string.Empty;
'''
text = text[:start] + new + text[end:]
text = text.replace(
    '        var newMemberIds = additionalIds.Except(existingAdditionalIds).ToHashSet();\n        foreach (var member in memberRows.Where(x => newMemberIds.Contains(x.Id)))\n',
    '        var newMemberIds = additionalIds.Except(existingAdditionalIds).ToHashSet();\n        foreach (var member in memberRows.Where(x => newMemberIds.Contains(x.Id)))\n',
    1)

anchor = '''    public async Task<IActionResult> ProductSummaries(string ids)\n    {\n        var productIds = ParseProductIds(ids).Distinct().Take(500).ToArray();\n        if (productIds.Length == 0) return Json(Array.Empty<object>());\n\n        var rows = await _db.Products.AsNoTracking()\n            .Where(x => productIds.Contains(x.Id))\n            .Select(x => new { id = x.Id, sku = x.Sku, name = x.Name })\n            .ToListAsync();\n\n        var order = productIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);\n        return Json(rows.OrderBy(x => order.TryGetValue(x.id, out var i) ? i : int.MaxValue));\n    }\n'''
if anchor not in text:
    raise SystemExit('6.4.23: ProductSummaries anchor missing')
addition = anchor + r'''

    public async Task<IActionResult> ProductSummariesBySkus(string skus)
    {
        var submitted = ParseProductSkus(skus)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
        var normalized = submitted
            .Select(NormalizeSkuLookup)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
            return Json(new { rows = Array.Empty<object>(), missingSkus = Array.Empty<string>(), duplicateSkus = Array.Empty<string>() });

        var matches = await _db.Products.AsNoTracking()
            .Where(x => x.Sku != null && normalized.Contains(x.Sku.ToUpper()))
            .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
            .ToListAsync();
        var grouped = matches
            .GroupBy(x => NormalizeSkuLookup(x.Sku), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        var missing = submitted
            .Where(x => !grouped.ContainsKey(NormalizeSkuLookup(x)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var duplicates = submitted
            .Where(x => grouped.TryGetValue(NormalizeSkuLookup(x), out var rows) && rows.Count > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var valid = submitted
            .Select(x => grouped.TryGetValue(NormalizeSkuLookup(x), out var rows) && rows.Count == 1 ? rows[0] : null)
            .Where(x => x != null)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .Select(x => new { id = x.Id, sku = x.Sku, name = x.Name })
            .ToList();

        return Json(new { rows = valid, missingSkus = missing, duplicateSkus = duplicates });
    }
'''
text = text.replace(anchor, addition, 1)
text = text.replace(
    '            AdditionalProductSkus = string.Join(Environment.NewLine, assignedProducts.Select(x => x.Sku).Where(x => !string.IsNullOrWhiteSpace(x))),',
    '            AdditionalProductSkus = string.Empty,',
    1)
controller.write_text(text, encoding='utf-8')

text = view.read_text(encoding='utf-8')
old_block = '''      <p class="text-muted">Bestehende Zuordnungen bleiben erhalten. Fügen Sie hier weitere Artikelnummern ein; mehrere Artikelnummern können direkt aus Excel oder einer Liste übernommen werden.</p>\n      <div class="form-group">\n        <label asp-for="AdditionalProductSkus"><strong>Artikelnummern</strong></label>\n        <textarea asp-for="AdditionalProductSkus" id="AdditionalProductSkus" class="form-control" rows="7" placeholder="z.B.&#10;ART-1001&#10;ART-1002&#10;ART-1003"></textarea>\n        <span asp-validation-for="AdditionalProductSkus" class="text-danger"></span>\n        <small class="text-muted d-block mt-2">Eine Artikelnummer pro Zeile. Beim Einfügen aus Excel werden auch Tabulatoren erkannt; Komma, Semikolon und | sind ebenfalls erlaubt. Beim Speichern werden neue Artikel ergänzt; bereits hinterlegte Artikel bleiben erhalten.</small>\n        <div id="gawela-sku-paste-info" class="small text-muted mt-1"></div>\n      </div>\n'''
new_block = '''      <p class="text-muted">Neue Artikelnummern werden zunächst in die untenstehende Zuordnungsliste übernommen. Nur diese Liste bestimmt beim Speichern, welche Produkte dem Farbkonfigurator zugeordnet sind.</p>\n      <div class="form-group">\n        <label asp-for="AdditionalProductSkus"><strong>Neue Artikelnummern hinzufügen</strong></label>\n        <textarea asp-for="AdditionalProductSkus" id="AdditionalProductSkus" class="form-control" rows="5" placeholder="z.B.&#10;ART-1001&#10;ART-1002&#10;ART-1003"></textarea>\n        <span asp-validation-for="AdditionalProductSkus" class="text-danger"></span>\n        <small class="text-muted d-block mt-2">Das Feld ist beim Öffnen leer. Eine Artikelnummer pro Zeile; beim Einfügen aus Excel werden auch Tabulatoren erkannt, Komma, Semikolon und | sind ebenfalls erlaubt.</small>\n        <div class="d-flex flex-wrap align-items-center mt-2" style="gap:.5rem">\n          <button type="button" id="gawela-add-skus" class="btn btn-primary"><i class="fa fa-plus"></i> Artikel zur Zuordnungsliste hinzufügen</button>\n          <div id="gawela-sku-paste-info" class="small text-muted"></div>\n        </div>\n      </div>\n'''
if old_block not in text:
    raise SystemExit('6.4.23: textarea block anchor missing')
text = text.replace(old_block, new_block, 1)
text = text.replace(
    '      <div id="gawela-member-list" class="mt-3">',
    '      <h4 class="h6 mt-4 mb-2">Zugeordnete Artikel</h4>\n      <div class="small text-muted mb-2">Einträge können einzeln entfernt werden. Diese Liste wird beim Speichern als Produktzuordnung verwendet.</div>\n      <div id="gawela-member-list" class="mt-2">',
    1)
text = text.replace(
    '<div class="border rounded px-3 py-2 mb-2 gawela-member-row" data-product-id="@p.ProductId"><strong>@p.Sku</strong> – @p.ProductName <span class="text-muted small">(ID @p.ProductId)</span></div>',
    '<div class="border rounded px-3 py-2 mb-2 gawela-member-row d-flex align-items-center justify-content-between" data-product-id="@p.ProductId"><div><strong>@p.Sku</strong> – @p.ProductName <span class="text-muted small">(ID @p.ProductId)</span></div><button type="button" class="btn btn-sm btn-outline-danger gawela-remove-member" title="Artikel aus Zuordnung entfernen"><i class="fa fa-times"></i> Entfernen</button></div>',
    1)
text = text.replace("    const summariesUrl='@Url.Action(\"ProductSummaries\")';", "    const summariesUrl='@Url.Action(\"ProductSummaries\")';\n    const summariesBySkusUrl='@Url.Action(\"ProductSummariesBySkus\")';", 1)
text = text.replace("    const skuPasteInfo=document.getElementById('gawela-sku-paste-info');", "    const skuPasteInfo=document.getElementById('gawela-sku-paste-info');\n    const addSkusButton=document.getElementById('gawela-add-skus');", 1)

js_start = text.index('    function mergeSkuValues(values){')
js_end = text.index('\n    memberSkusInput?.addEventListener(\'input\',updateSkuPasteInfo);', js_start)
new_js = r'''    function memberIds(){
      return (membersInput?.value||'').split(',').map(x=>x.trim()).filter(Boolean);
    }
    function setMemberIds(ids){
      const unique=[]; const seen=new Set();
      (ids||[]).forEach(id=>{const clean=String(id||'').trim();if(!clean||seen.has(clean))return;seen.add(clean);unique.push(clean);});
      if(membersInput)membersInput.value=unique.join(',');
    }
    function memberRowHtml(p){
      return '<div class="border rounded px-3 py-2 mb-2 gawela-member-row d-flex align-items-center justify-content-between" data-product-id="'+p.id+'"><div><strong>'+escapeHtml(p.sku||('ID '+p.id))+'</strong> – '+escapeHtml(p.name||'')+' <span class="text-muted small">(ID '+p.id+')</span></div><button type="button" class="btn btn-sm btn-outline-danger gawela-remove-member" title="Artikel aus Zuordnung entfernen"><i class="fa fa-times"></i> Entfernen</button></div>';
    }
    function renderMembers(value){
      const ids=(value||'').split(',').map(x=>x.trim()).filter(Boolean);
      if(!ids.length){
        membersList.innerHTML='<div class="text-muted gawela-no-members">Keine weiteren Artikel zugeordnet.</div>';
        return;
      }
      fetch(summariesUrl+'?ids='+encodeURIComponent(ids.join(',')),{credentials:'same-origin'}).then(r=>r.json()).then(rows=>{
        if(!rows||!rows.length){membersList.innerHTML='<div class="text-muted">Keine gültigen Artikel ausgewählt.</div>';return;}
        membersList.innerHTML=rows.map(memberRowHtml).join('');
      });
    }
    window.GawelaMembers_Completed=function(){renderMembers(membersInput.value||'');return true;};
    addSkusButton?.addEventListener('click',function(){
      const tokens=parseSkuTokens(memberSkusInput?.value||'');
      if(!tokens.length){updateSkuPasteInfo();return;}
      addSkusButton.disabled=true;
      fetch(summariesBySkusUrl+'?skus='+encodeURIComponent(tokens.join('\n')),{credentials:'same-origin'}).then(r=>r.json()).then(result=>{
        const rows=result?.rows||[];
        const existing=memberIds();
        setMemberIds(existing.concat(rows.map(x=>x.id)));
        renderMembers(membersInput.value||'');
        const invalid=[...(result?.missingSkus||[]),...(result?.duplicateSkus||[])];
        if(memberSkusInput)memberSkusInput.value=invalid.join('\n');
        const messages=[];
        if(result?.missingSkus?.length)messages.push('Nicht gefunden: '+result.missingSkus.join(', '));
        if(result?.duplicateSkus?.length)messages.push('Mehrfach im Katalog vorhanden: '+result.duplicateSkus.join(', '));
        skuPasteInfo.textContent=messages.length?messages.join(' · '):(rows.length+' Artikel zur Zuordnungsliste hinzugefügt.');
        if(!messages.length && memberSkusInput)memberSkusInput.value='';
      }).catch(()=>{skuPasteInfo.textContent='Artikel konnten nicht geprüft werden. Bitte erneut versuchen.';}).finally(()=>{addSkusButton.disabled=false;});
    });
    membersList?.addEventListener('click',function(e){
      const btn=e.target.closest('.gawela-remove-member'); if(!btn)return;
      const row=btn.closest('.gawela-member-row'); if(!row)return;
      const id=row.getAttribute('data-product-id');
      setMemberIds(memberIds().filter(x=>x!==id));
      renderMembers(membersInput.value||'');
    });
'''
text = text[:js_start] + new_js + text[js_end:]
text = text.replace('    updateSkuPasteInfo();\n\n    function activeSlots()', '    if(memberSkusInput)memberSkusInput.value=\'\';\n    updateSkuPasteInfo();\n\n    function activeSlots()', 1)
view.write_text(text, encoding='utf-8')

host_text = host.read_text(encoding='utf-8')
if 'v=6.4.22' not in host_text:
    raise SystemExit('Expected 6.4.22 asset version before applying 6.4.23 patch.')
host.write_text(host_text.replace('v=6.4.22', 'v=6.4.23'), encoding='utf-8')

module_text = module.read_text(encoding='utf-8')
if '"Version": "6.4.22"' not in module_text:
    raise SystemExit('Expected 6.4.22 module version before applying 6.4.23 patch.')
module.write_text(module_text.replace('"Version": "6.4.22"', '"Version": "6.4.23"', 1), encoding='utf-8')

checks = {
    'Controllers/GawelaColorAdminController.cs': ['ProductSummariesBySkus', 'AdditionalProductIds is the exact, authoritative assignment list', 'AdditionalProductSkus = string.Empty'],
    'Views/GawelaColorAdmin/Configure.cshtml': ['gawela-add-skus', 'gawela-remove-member', 'Zugeordnete Artikel', 'Nur diese Liste bestimmt beim Speichern'],
    'Views/Shared/Components/GawelaColorHost/Default.cshtml': ['v=6.4.23'],
    'module.json': ['"Version": "6.4.23"']
}
for rel, needles in checks.items():
    data=(root/rel).read_text(encoding='utf-8')
    for needle in needles:
        if needle not in data:
            raise SystemExit(f'6.4.23 verification failed: {needle!r} missing in {rel}')
