from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
view = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
admin_js = root / 'wwwroot' / 'gawela-admin-members.js'
source_js = Path(__file__).resolve().parent / 'gawela-admin-members.js'

text = view.read_text(encoding='utf-8')

# Keep the proven 6.4.22 inline Razor/JS block intact. The new member-list behavior lives
# in a normal external JS file, avoiding Razor parsing ambiguities from HTML-in-JS strings.
text = text.replace("    const summariesBySkusUrl='@Url.Action(\"ProductSummariesBySkus\")';\n", '', 1)
text = text.replace("    const addSkusButton=document.getElementById('gawela-add-skus');\n", '', 1)

start = text.index('    function memberIds(){')
end = text.index("\n    memberSkusInput?.addEventListener('input',updateSkuPasteInfo);", start)
legacy = r'''    function mergeSkuValues(values){
      const merged=[], seen=new Set();
      parseSkuTokens((memberSkusInput?.value||'')).concat(values||[]).forEach(value=>{
        const clean=(value||'').trim(); if(!clean)return;
        const key=clean.toLocaleUpperCase(); if(seen.has(key))return;
        seen.add(key); merged.push(clean);
      });
      return merged;
    }
    function renderMembers(value,syncSkus){
      const ids=(value||'').split(',').map(x=>x.trim()).filter(Boolean);
      if(!ids.length){
        updateSkuPasteInfo();
        return;
      }
      fetch(summariesUrl+'?ids='+encodeURIComponent(ids.join(',')),{credentials:'same-origin'}).then(r=>r.json()).then(rows=>{
        if(!rows||!rows.length)return;
        membersList.innerHTML=rows.map(p=>'<div class="border rounded px-3 py-2 mb-2 gawela-member-row" data-product-id="'+p.id+'"><strong>'+escapeHtml(p.sku||('ID '+p.id))+'</strong> – '+escapeHtml(p.name||'')+' <span class="text-muted small">(ID '+p.id+')</span></div>').join('');
        if(syncSkus && memberSkusInput){
          memberSkusInput.value=mergeSkuValues(rows.map(p=>p.sku||'')).join('\n');
          updateSkuPasteInfo();
        }
      });
    }
    window.GawelaMembers_Completed=function(){renderMembers(membersInput.value||'',true);return true;};
'''
text = text[:start] + legacy + text[end:]
text = text.replace("    if(memberSkusInput)memberSkusInput.value='';\n", '', 1)

# Give the external behavior its two endpoint URLs without adding executable Razor logic.
text = text.replace(
    '          <button type="button" id="gawela-add-skus" class="btn btn-primary">',
    '          <button type="button" id="gawela-add-skus" class="btn btn-primary" data-resolve-url="@Url.Action(\"ProductSummariesBySkus\")">',
    1)
text = text.replace(
    '      <div id="gawela-member-list" class="mt-2">',
    '      <div id="gawela-member-list" class="mt-2" data-summaries-url="@Url.Action(\"ProductSummaries\")">',
    1)

external_tag = '  <script src="@Url.Content(\"~/Modules/Gawela.ColorConfigurator/gawela-admin-members.js?v=6.4.23\")"></script>\n'
marker = '  </script>\n}\n'
pos = text.rfind(marker)
if pos < 0:
    raise SystemExit('6.4.23 fix: closing admin script marker missing')
insert_at = pos + len('  </script>\n')
text = text[:insert_at] + external_tag + text[insert_at:]

view.write_text(text, encoding='utf-8')
shutil.copyfile(source_js, admin_js)

checks = [
    'data-resolve-url="@Url.Action(\"ProductSummariesBySkus\")"',
    'data-summaries-url="@Url.Action(\"ProductSummaries\")"',
    'gawela-admin-members.js?v=6.4.23',
]
for needle in checks:
    if needle not in text:
        raise SystemExit(f'6.4.23 fix verification failed: {needle}')
if 'function memberIds(){' in text:
    raise SystemExit('6.4.23 fix verification failed: complex inline member JS still present')
