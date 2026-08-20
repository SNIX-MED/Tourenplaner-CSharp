from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
model = root / 'Models' / 'GawelaAssetAdminModel.cs'
controller = root / 'Controllers' / 'GawelaColorAdminController.cs'
view = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
host = root / 'Views' / 'Shared' / 'Components' / 'GawelaColorHost' / 'Default.cshtml'
module = root / 'module.json'

# 6.4.21 is intentionally narrow: only restore/improve assignment of additional products.
# Everything else remains equivalent to the verified 6.4.20 source before compilation.

text = model.read_text(encoding='utf-8')
anchor = '    public string AdditionalProductIds { get; set; }\n'
if anchor not in text:
    raise SystemExit('6.4.21: AdditionalProductIds model anchor missing')
text = text.replace(anchor, anchor + '    public string AdditionalProductSkus { get; set; }\n', 1)
model.write_text(text, encoding='utf-8')

text = controller.read_text(encoding='utf-8')
old = '''        var additionalIds = ParseProductIds(model.AdditionalProductIds)\n            .Where(x => x != master.Id)\n            .Distinct()\n            .ToList();\n\n        var memberRows = additionalIds.Count == 0\n            ? new List<ProductLookupResult>()\n            : await _db.Products.AsNoTracking()\n                .Where(x => additionalIds.Contains(x.Id))\n                .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })\n                .ToListAsync();\n\n        var missingIds = additionalIds.Except(memberRows.Select(x => x.Id)).ToList();\n        if (missingIds.Count > 0)\n            ModelState.AddModelError(nameof(model.AdditionalProductIds), "Mindestens ein ausgewählter weiterer Artikel wurde nicht gefunden.");\n'''
new = '''        List<int> additionalIds;\n        List<ProductLookupResult> memberRows;\n\n        // The pasted article-number field is authoritative when it is present in the form.\n        // Users can therefore paste whole Excel columns (newline/tab/comma/semicolon separated)\n        // and can also remove all additional products by submitting an empty textarea.\n        if (model.AdditionalProductSkus != null)\n        {\n            var submittedSkus = ParseProductSkus(model.AdditionalProductSkus)\n                .Distinct(StringComparer.OrdinalIgnoreCase)\n                .ToList();\n            var normalizedSkus = submittedSkus\n                .Select(NormalizeSkuLookup)\n                .Where(x => x.Length > 0)\n                .Distinct(StringComparer.Ordinal)\n                .ToList();\n\n            var skuRows = normalizedSkus.Count == 0\n                ? new List<ProductLookupResult>()\n                : await _db.Products.AsNoTracking()\n                    .Where(x => x.Sku != null && normalizedSkus.Contains(x.Sku.ToUpper()))\n                    .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })\n                    .ToListAsync();\n\n            var rowsBySku = skuRows\n                .GroupBy(x => NormalizeSkuLookup(x.Sku), StringComparer.Ordinal)\n                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);\n\n            var missingSkus = submittedSkus\n                .Where(x => !rowsBySku.ContainsKey(NormalizeSkuLookup(x)))\n                .Distinct(StringComparer.OrdinalIgnoreCase)\n                .ToList();\n            if (missingSkus.Count > 0)\n                ModelState.AddModelError(nameof(model.AdditionalProductSkus), $"Folgende Artikelnummern wurden nicht gefunden: {string.Join(", ", missingSkus.Take(20))}{(missingSkus.Count > 20 ? " …" : string.Empty)}");\n\n            var duplicateSkus = rowsBySku\n                .Where(x => x.Value.Count > 1)\n                .Select(x => x.Value.First().Sku)\n                .Where(x => !string.IsNullOrWhiteSpace(x))\n                .ToList();\n            if (duplicateSkus.Count > 0)\n                ModelState.AddModelError(nameof(model.AdditionalProductSkus), $"Folgende Artikelnummern sind im Produktkatalog mehrfach vorhanden und können nicht eindeutig zugeordnet werden: {string.Join(", ", duplicateSkus.Take(20))}{(duplicateSkus.Count > 20 ? " …" : string.Empty)}");\n\n            memberRows = submittedSkus\n                .Select(x => rowsBySku.TryGetValue(NormalizeSkuLookup(x), out var rows) && rows.Count == 1 ? rows[0] : null)\n                .Where(x => x != null && x.Id != master.Id)\n                .GroupBy(x => x.Id)\n                .Select(x => x.First())\n                .ToList();\n            additionalIds = memberRows.Select(x => x.Id).ToList();\n\n            // Keep the hidden Smartstore picker value in sync for redisplay/backward compatibility.\n            model.AdditionalProductIds = string.Join(',', additionalIds);\n        }\n        else\n        {\n            additionalIds = ParseProductIds(model.AdditionalProductIds)\n                .Where(x => x != master.Id)\n                .Distinct()\n                .ToList();\n\n            memberRows = additionalIds.Count == 0\n                ? new List<ProductLookupResult>()\n                : await _db.Products.AsNoTracking()\n                    .Where(x => additionalIds.Contains(x.Id))\n                    .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })\n                    .ToListAsync();\n\n            var missingIds = additionalIds.Except(memberRows.Select(x => x.Id)).ToList();\n            if (missingIds.Count > 0)\n                ModelState.AddModelError(nameof(model.AdditionalProductIds), "Mindestens ein ausgewählter weiterer Artikel wurde nicht gefunden.");\n        }\n'''
if old not in text:
    raise SystemExit('6.4.21: additional-product save block anchor missing')
text = text.replace(old, new, 1)

old = '''        var additionalIds = (group?.ProductIds ?? new List<int> { masterId })\n            .Where(x => x != masterId)\n            .Distinct()\n            .ToList();\n\n        return new GawelaAssetAdminModel\n        {'''
new = '''        var additionalIds = (group?.ProductIds ?? new List<int> { masterId })\n            .Where(x => x != masterId)\n            .Distinct()\n            .ToList();\n        var assignedProducts = await LoadAssignedProductsAsync(additionalIds);\n\n        return new GawelaAssetAdminModel\n        {'''
if old not in text:
    raise SystemExit('6.4.21: existing-editor additionalIds anchor missing')
text = text.replace(old, new, 1)

old = '''            AdditionalProductIds = string.Join(',', additionalIds),\n            AdditionalProductIdStrings = additionalIds.Select(x => x.ToString()).ToArray(),\n            AssignedProducts = await LoadAssignedProductsAsync(additionalIds),\n'''
new = '''            AdditionalProductIds = string.Join(',', additionalIds),\n            AdditionalProductSkus = string.Join(Environment.NewLine, assignedProducts.Select(x => x.Sku).Where(x => !string.IsNullOrWhiteSpace(x))),\n            AdditionalProductIdStrings = additionalIds.Select(x => x.ToString()).ToArray(),\n            AssignedProducts = assignedProducts,\n'''
if old not in text:
    raise SystemExit('6.4.21: existing-editor model assignment anchor missing')
text = text.replace(old, new, 1)

# Preserve entity-picker compatibility for larger selections too.
text = text.replace('var productIds = ParseProductIds(ids).Distinct().Take(100).ToArray();',
                    'var productIds = ParseProductIds(ids).Distinct().Take(500).ToArray();', 1)

anchor = '''    private static IEnumerable<int> ParseProductIds(string value)\n    {\n'''
helper = '''    private static IEnumerable<string> ParseProductSkus(string value)\n    {\n        return (value ?? string.Empty)\n            .Split(new[] { '\\r', '\\n', '\\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)\n            .Select(x => x.Trim().Trim('"', '\\''))\n            .Where(x => x.Length > 0);\n    }\n\n    private static string NormalizeSkuLookup(string value)\n        => (value ?? string.Empty).Trim().ToUpperInvariant();\n\n'''
if anchor not in text:
    raise SystemExit('6.4.21: ParseProductIds anchor missing')
text = text.replace(anchor, helper + anchor, 1)
controller.write_text(text, encoding='utf-8')

text = view.read_text(encoding='utf-8')
old = '''      <p class="text-muted">Wählen Sie alle Produkte, die denselben Farbkonfigurator verwenden sollen. Breite und Tiefe dürfen bei einer gemeinsamen Höhen-/Bildvorlage abweichen; die benötigten Farbattribute müssen vorhanden sein.</p>\n      <input asp-for="AdditionalProductIds" type="hidden"/>\n      <entity-picker entity-type="product"\n                     target-input-selector="#AdditionalProductIds"\n                     selected="@Model.AdditionalProductIdStrings"\n                     append-mode="false"\n                     disabled-entity-ids="@(new[]{Model.ProductId})"\n                     caption="Artikel auswählen"\n                     icon-css-class="fa fa-plus"\n                     dialog-title="Weitere Artikel für diesen Farbkonfigurator auswählen"\n                     onselectioncompleted="GawelaMembers_Completed" />\n      <div id="gawela-member-list" class="mt-3">\n'''
new = '''      <p class="text-muted">Fügen Sie die Artikelnummern der Produkte ein, die denselben Farbkonfigurator verwenden sollen. Mehrere Artikelnummern können direkt aus Excel oder einer Liste übernommen werden.</p>\n      <div class="form-group">\n        <label asp-for="AdditionalProductSkus"><strong>Artikelnummern</strong></label>\n        <textarea asp-for="AdditionalProductSkus" id="AdditionalProductSkus" class="form-control" rows="7" placeholder="z.B.&#10;ART-1001&#10;ART-1002&#10;ART-1003"></textarea>\n        <span asp-validation-for="AdditionalProductSkus" class="text-danger"></span>\n        <small class="text-muted d-block mt-2">Eine Artikelnummer pro Zeile. Beim Einfügen aus Excel werden auch Tabulatoren erkannt; Komma, Semikolon und | sind ebenfalls erlaubt. Leer lassen und speichern entfernt alle weiteren Artikel.</small>\n        <div id="gawela-sku-paste-info" class="small text-muted mt-1"></div>\n      </div>\n      <input asp-for="AdditionalProductIds" type="hidden"/>\n      <div class="d-flex flex-wrap align-items-center gap-2">\n        <entity-picker entity-type="product"\n                       target-input-selector="#AdditionalProductIds"\n                       selected="@Model.AdditionalProductIdStrings"\n                       append-mode="false"\n                       disabled-entity-ids="@(new[]{Model.ProductId})"\n                       caption="Alternativ im Produktkatalog auswählen"\n                       icon-css-class="fa fa-plus"\n                       dialog-title="Weitere Artikel für diesen Farbkonfigurator auswählen"\n                       onselectioncompleted="GawelaMembers_Completed" />\n      </div>\n      <div id="gawela-member-list" class="mt-3">\n'''
if old not in text:
    raise SystemExit('6.4.21: additional-product UI anchor missing')
text = text.replace(old, new, 1)

old = """    const membersInput=document.getElementById('AdditionalProductIds');\n    const membersList=document.getElementById('gawela-member-list');\n"""
new = """    const membersInput=document.getElementById('AdditionalProductIds');\n    const memberSkusInput=document.getElementById('AdditionalProductSkus');\n    const skuPasteInfo=document.getElementById('gawela-sku-paste-info');\n    const membersList=document.getElementById('gawela-member-list');\n"""
if old not in text:
    raise SystemExit('6.4.21: members JS variables anchor missing')
text = text.replace(old, new, 1)

old = '''    function renderMembers(value){\n      const ids=(value||'').split(',').map(x=>x.trim()).filter(Boolean);\n      if(!ids.length){membersList.innerHTML='<div class="text-muted gawela-no-members">Keine weiteren Artikel zugeordnet.</div>';return;}\n      fetch(summariesUrl+'?ids='+encodeURIComponent(ids.join(',')),{credentials:'same-origin'}).then(r=>r.json()).then(rows=>{\n        if(!rows||!rows.length){membersList.innerHTML='<div class="text-muted">Keine gültigen Artikel ausgewählt.</div>';return;}\n        membersList.innerHTML=rows.map(p=>'<div class="border rounded px-3 py-2 mb-2 gawela-member-row" data-product-id="'+p.id+'"><strong>'+escapeHtml(p.sku||('ID '+p.id))+'</strong> – '+escapeHtml(p.name||'')+' <span class="text-muted small">(ID '+p.id+')</span></div>').join('');\n      });\n    }\n    window.GawelaMembers_Completed=function(){renderMembers(membersInput.value||'');return true;};\n'''
new = '''    function parseSkuTokens(value){\n      return (value||'').split(/[\\r\\n\\t,;|]+/).map(x=>x.trim().replace(/^["']+|["']+$/g,'')).filter(Boolean);\n    }\n    function updateSkuPasteInfo(){\n      if(!skuPasteInfo)return;\n      const count=[...new Set(parseSkuTokens(memberSkusInput?.value||'').map(x=>x.toLocaleUpperCase()))].length;\n      skuPasteInfo.textContent=count ? count+' Artikelnummer'+(count===1?'':'n')+' eingetragen.' : 'Keine weiteren Artikelnummern eingetragen.';\n    }\n    function renderMembers(value,syncSkus){\n      const ids=(value||'').split(',').map(x=>x.trim()).filter(Boolean);\n      if(!ids.length){\n        membersList.innerHTML='<div class="text-muted gawela-no-members">Keine weiteren Artikel zugeordnet.</div>';\n        if(syncSkus && memberSkusInput)memberSkusInput.value='';\n        updateSkuPasteInfo();\n        return;\n      }\n      fetch(summariesUrl+'?ids='+encodeURIComponent(ids.join(',')),{credentials:'same-origin'}).then(r=>r.json()).then(rows=>{\n        if(!rows||!rows.length){membersList.innerHTML='<div class="text-muted">Keine gültigen Artikel ausgewählt.</div>';return;}\n        membersList.innerHTML=rows.map(p=>'<div class="border rounded px-3 py-2 mb-2 gawela-member-row" data-product-id="'+p.id+'"><strong>'+escapeHtml(p.sku||('ID '+p.id))+'</strong> – '+escapeHtml(p.name||'')+' <span class="text-muted small">(ID '+p.id+')</span></div>').join('');\n        if(syncSkus && memberSkusInput){\n          memberSkusInput.value=rows.map(p=>p.sku||'').filter(Boolean).join('\\n');\n          updateSkuPasteInfo();\n        }\n      });\n    }\n    window.GawelaMembers_Completed=function(){renderMembers(membersInput.value||'',true);return true;};\n    memberSkusInput?.addEventListener('input',updateSkuPasteInfo);\n    memberSkusInput?.addEventListener('paste',function(){setTimeout(updateSkuPasteInfo,0);});\n    updateSkuPasteInfo();\n'''
if old not in text:
    raise SystemExit('6.4.21: renderMembers JS anchor missing')
text = text.replace(old, new, 1)
view.write_text(text, encoding='utf-8')

host_text = host.read_text(encoding='utf-8')
if 'v=6.4.20' not in host_text:
    raise SystemExit('Expected 6.4.20 asset version before applying 6.4.21 patch.')
host.write_text(host_text.replace('v=6.4.20', 'v=6.4.21'), encoding='utf-8')

module_text = module.read_text(encoding='utf-8')
if '"Version": "6.4.20"' not in module_text:
    raise SystemExit('Expected 6.4.20 module version before applying 6.4.21 patch.')
module.write_text(module_text.replace('"Version": "6.4.20"', '"Version": "6.4.21"', 1), encoding='utf-8')

checks = {
    'Models/GawelaAssetAdminModel.cs': ['AdditionalProductSkus'],
    'Controllers/GawelaColorAdminController.cs': [
        'ParseProductSkus', 'NormalizeSkuLookup', 'Folgende Artikelnummern wurden nicht gefunden',
        'model.AdditionalProductIds = string.Join', 'Take(500)'
    ],
    'Views/GawelaColorAdmin/Configure.cshtml': [
        'asp-for="AdditionalProductSkus"', 'direkt aus Excel', 'parseSkuTokens',
        'Alternativ im Produktkatalog auswählen'
    ],
    'Views/Shared/Components/GawelaColorHost/Default.cshtml': ['v=6.4.21'],
    'module.json': ['"Version": "6.4.21"'],
}
for rel, needles in checks.items():
    data = (root / rel).read_text(encoding='utf-8')
    for needle in needles:
        if needle not in data:
            raise SystemExit(f'6.4.21 verification failed: {needle!r} missing in {rel}')
