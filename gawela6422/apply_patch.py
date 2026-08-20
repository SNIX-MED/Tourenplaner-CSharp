from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
controller = root / 'Controllers' / 'GawelaColorAdminController.cs'
view = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
host = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorHost' / 'Default.cshtml'
module = root / 'module.json'

# 6.4.22 is deliberately narrow: additional-product assignment is additive.
# Existing stored members must never be dropped or revalidated merely because new SKUs are added.

text = controller.read_text(encoding='utf-8')
start = text.index('        List<int> additionalIds;\n        List<ProductLookupResult> memberRows;\n')
end = text.index('\n        var otherGroups = _groupStore.Load().Where(x => x.MasterProductId != master.Id).ToList();', start)
new = r'''        var existingAdditionalIds = currentMemberIds
            .Where(x => x != master.Id)
            .Distinct()
            .ToList();
        var existingMemberRows = existingAdditionalIds.Count == 0
            ? new List<ProductLookupResult>()
            : await _db.Products.AsNoTracking()
                .Where(x => existingAdditionalIds.Contains(x.Id))
                .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
                .ToListAsync();
        var existingBySku = existingMemberRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .GroupBy(x => NormalizeSkuLookup(x.Sku), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        // 6.4.22: "Weitere Artikel" is additive. Existing assignments are always retained.
        // The textarea may contain the already displayed SKUs plus any newly pasted Excel/list values.
        // Current members are resolved from the stored group first so they can never trigger false
        // duplicate-SKU errors from unrelated/legacy catalog rows.
        var submittedSkus = ParseProductSkus(model.AdditionalProductSkus)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var newSubmittedSkus = submittedSkus
            .Where(x => !existingBySku.ContainsKey(NormalizeSkuLookup(x)))
            .ToList();
        var normalizedNewSkus = newSubmittedSkus
            .Select(NormalizeSkuLookup)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var newSkuRows = normalizedNewSkus.Count == 0
            ? new List<ProductLookupResult>()
            : await _db.Products.AsNoTracking()
                .Where(x => x.Sku != null && normalizedNewSkus.Contains(x.Sku.ToUpper()))
                .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
                .ToListAsync();

        var newRowsBySku = newSkuRows
            .GroupBy(x => NormalizeSkuLookup(x.Sku), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        var missingSkus = newSubmittedSkus
            .Where(x => !newRowsBySku.ContainsKey(NormalizeSkuLookup(x)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missingSkus.Count > 0)
            ModelState.AddModelError(nameof(model.AdditionalProductSkus), $"Folgende neue Artikelnummern wurden nicht gefunden: {string.Join(", ", missingSkus.Take(20))}{(missingSkus.Count > 20 ? " …" : string.Empty)}");

        var duplicateSkus = newRowsBySku
            .Where(x => x.Value.Count > 1)
            .Select(x => x.Value.First().Sku)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (duplicateSkus.Count > 0)
            ModelState.AddModelError(nameof(model.AdditionalProductSkus), $"Folgende neue Artikelnummern sind im Produktkatalog mehrfach vorhanden und können nicht eindeutig zugeordnet werden: {string.Join(", ", duplicateSkus.Take(20))}{(duplicateSkus.Count > 20 ? " …" : string.Empty)}");

        var pastedNewRows = newSubmittedSkus
            .Select(x => newRowsBySku.TryGetValue(NormalizeSkuLookup(x), out var rows) && rows.Count == 1 ? rows[0] : null)
            .Where(x => x != null && x.Id != master.Id)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        // Keep the Smartstore picker as an additional input source. It is additive as well.
        var pickerIds = ParseProductIds(model.AdditionalProductIds)
            .Where(x => x != master.Id)
            .Distinct()
            .ToList();
        var pickerRows = pickerIds.Count == 0
            ? new List<ProductLookupResult>()
            : await _db.Products.AsNoTracking()
                .Where(x => pickerIds.Contains(x.Id))
                .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
                .ToListAsync();
        var missingPickerIds = pickerIds.Except(pickerRows.Select(x => x.Id)).ToList();
        if (missingPickerIds.Count > 0)
            ModelState.AddModelError(nameof(model.AdditionalProductIds), "Mindestens ein im Produktkatalog ausgewählter weiterer Artikel wurde nicht gefunden.");

        var additionalIds = existingAdditionalIds
            .Concat(pastedNewRows.Select(x => x.Id))
            .Concat(pickerRows.Select(x => x.Id))
            .Where(x => x != master.Id)
            .Distinct()
            .ToList();
        var memberRows = existingMemberRows
            .Concat(pastedNewRows)
            .Concat(pickerRows)
            .Where(x => x != null && x.Id != master.Id)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        // Synchronize both controls without changing the additive semantics.
        model.AdditionalProductIds = string.Join(',', additionalIds);
        model.AdditionalProductSkus = string.Join(Environment.NewLine,
            memberRows.Select(x => x.Sku).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
'''
text = text[:start] + new + text[end:]

old_loop = '''        var otherGroups = _groupStore.Load().Where(x => x.MasterProductId != master.Id).ToList();\n        foreach (var member in memberRows)\n        {\n'''
new_loop = '''        var otherGroups = _groupStore.Load().Where(x => x.MasterProductId != master.Id).ToList();\n        var newMemberIds = additionalIds.Except(existingAdditionalIds).ToHashSet();\n        foreach (var member in memberRows.Where(x => newMemberIds.Contains(x.Id)))\n        {\n'''
if old_loop not in text:
    raise SystemExit('6.4.22: validation loop anchor missing')
text = text.replace(old_loop, new_loop, 1)
controller.write_text(text, encoding='utf-8')

text = view.read_text(encoding='utf-8')
text = text.replace(
    'Fügen Sie die Artikelnummern der Produkte ein, die denselben Farbkonfigurator verwenden sollen. Mehrere Artikelnummern können direkt aus Excel oder einer Liste übernommen werden.',
    'Bestehende Zuordnungen bleiben erhalten. Fügen Sie hier weitere Artikelnummern ein; mehrere Artikelnummern können direkt aus Excel oder einer Liste übernommen werden.',
    1)
text = text.replace(
    'Eine Artikelnummer pro Zeile. Beim Einfügen aus Excel werden auch Tabulatoren erkannt; Komma, Semikolon und | sind ebenfalls erlaubt. Leer lassen und speichern entfernt alle weiteren Artikel.',
    'Eine Artikelnummer pro Zeile. Beim Einfügen aus Excel werden auch Tabulatoren erkannt; Komma, Semikolon und | sind ebenfalls erlaubt. Beim Speichern werden neue Artikel ergänzt; bereits hinterlegte Artikel bleiben erhalten.',
    1)
text = text.replace('append-mode="false"\n                       disabled-entity-ids=', 'append-mode="true"\n                       disabled-entity-ids=', 1)

old_js = '''    function renderMembers(value,syncSkus){\n      const ids=(value||'').split(',').map(x=>x.trim()).filter(Boolean);\n      if(!ids.length){\n        membersList.innerHTML='<div class="text-muted gawela-no-members">Keine weiteren Artikel zugeordnet.</div>';\n        if(syncSkus && memberSkusInput)memberSkusInput.value='';\n        updateSkuPasteInfo();\n        return;\n      }\n      fetch(summariesUrl+'?ids='+encodeURIComponent(ids.join(',')),{credentials:'same-origin'}).then(r=>r.json()).then(rows=>{\n        if(!rows||!rows.length){membersList.innerHTML='<div class="text-muted">Keine gültigen Artikel ausgewählt.</div>';return;}\n        membersList.innerHTML=rows.map(p=>'<div class="border rounded px-3 py-2 mb-2 gawela-member-row" data-product-id="'+p.id+'"><strong>'+escapeHtml(p.sku||('ID '+p.id))+'</strong> – '+escapeHtml(p.name||'')+' <span class="text-muted small">(ID '+p.id+')</span></div>').join('');\n        if(syncSkus && memberSkusInput){\n          memberSkusInput.value=rows.map(p=>p.sku||'').filter(Boolean).join('\\n');\n          updateSkuPasteInfo();\n        }\n      });\n    }\n'''
new_js = '''    function mergeSkuValues(values){\n      const merged=[], seen=new Set();\n      parseSkuTokens((memberSkusInput?.value||'')).concat(values||[]).forEach(value=>{\n        const clean=(value||'').trim(); if(!clean)return;\n        const key=clean.toLocaleUpperCase(); if(seen.has(key))return;\n        seen.add(key); merged.push(clean);\n      });\n      return merged;\n    }\n    function renderMembers(value,syncSkus){\n      const ids=(value||'').split(',').map(x=>x.trim()).filter(Boolean);\n      if(!ids.length){\n        updateSkuPasteInfo();\n        return;\n      }\n      fetch(summariesUrl+'?ids='+encodeURIComponent(ids.join(',')),{credentials:'same-origin'}).then(r=>r.json()).then(rows=>{\n        if(!rows||!rows.length)return;\n        membersList.innerHTML=rows.map(p=>'<div class="border rounded px-3 py-2 mb-2 gawela-member-row" data-product-id="'+p.id+'"><strong>'+escapeHtml(p.sku||('ID '+p.id))+'</strong> – '+escapeHtml(p.name||'')+' <span class="text-muted small">(ID '+p.id+')</span></div>').join('');\n        if(syncSkus && memberSkusInput){\n          memberSkusInput.value=mergeSkuValues(rows.map(p=>p.sku||'')).join('\\n');\n          updateSkuPasteInfo();\n        }\n      });\n    }\n'''
if old_js not in text:
    raise SystemExit('6.4.22: renderMembers JS anchor missing')
text = text.replace(old_js, new_js, 1)
view.write_text(text, encoding='utf-8')

host_text = host.read_text(encoding='utf-8')
if 'v=6.4.21' not in host_text:
    raise SystemExit('Expected 6.4.21 asset version before applying 6.4.22 patch.')
host.write_text(host_text.replace('v=6.4.21', 'v=6.4.22'), encoding='utf-8')

module_text = module.read_text(encoding='utf-8')
if '"Version": "6.4.21"' not in module_text:
    raise SystemExit('Expected 6.4.21 module version before applying 6.4.22 patch.')
module.write_text(module_text.replace('"Version": "6.4.21"', '"Version": "6.4.22"', 1), encoding='utf-8')

checks = {
    'Controllers/GawelaColorAdminController.cs': [
        'existingAdditionalIds', 'newMemberIds', 'additionalIds.Except(existingAdditionalIds)',
        'Folgende neue Artikelnummern wurden nicht gefunden', 'pickerRows',
        'model.AdditionalProductSkus = string.Join(Environment.NewLine'
    ],
    'Views/GawelaColorAdmin/Configure.cshtml': [
        'Bestehende Zuordnungen bleiben erhalten', 'bereits hinterlegte Artikel bleiben erhalten',
        'append-mode="true"', 'function mergeSkuValues(values)'
    ],
    'Views/Shared/Components/GawelaColorHost/Default.cshtml': ['v=6.4.22'],
    'module.json': ['"Version": "6.4.22"']
}
for rel, needles in checks.items():
    data = (root / rel).read_text(encoding='utf-8')
    for needle in needles:
        if needle not in data:
            raise SystemExit(f'6.4.22 verification failed: {needle!r} missing in {rel}')
