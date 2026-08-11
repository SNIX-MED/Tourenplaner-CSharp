from pathlib import Path
import sys

root = Path(sys.argv[1])
js_path = root / 'wwwroot' / 'gawela-color.js'
text = js_path.read_text(encoding='utf-8')

old = "async function init(host){if(host.dataset.gawelaInit)return;host.dataset.gawelaInit='1';const id=pid();if(!id)return;const ep=host.dataset.assetEndpoint,cp=host.dataset.configEndpoint+(host.dataset.configEndpoint.includes('?')?'&':'?')+'productId='+id;try{const[cfg,colors]=await Promise.all([json(cp),palette(host.dataset.paletteUrl)]);if(!cfg.layers?.length)return;const baseUrl=url(ep,id,'base')"

new = "function pick(o,n){if(!o)return undefined;const p=n.charAt(0).toUpperCase()+n.slice(1);return o[n]!==undefined?o[n]:o[p]}function normalizeConfig(raw){const layers=(pick(raw,'layers')||[]).map(l=>({key:pick(l,'key'),name:pick(l,'name')||'Ebene',productVariantAttributeId:pick(l,'productVariantAttributeId'),attributeLabel:pick(l,'attributeLabel')||'',assetKind:pick(l,'assetKind')||'',baseRal:String(pick(l,'baseRal')||'7035'),defaultRal:String(pick(l,'defaultRal')||'7035')}));return{thumbnailLabel:pick(raw,'thumbnailLabel')||'Farbe konfigurieren',layers}}async function init(host){if(host.dataset.gawelaInit)return;host.dataset.gawelaInit='1';const id=pid();if(!id)return;const ep=host.dataset.assetEndpoint,cp=host.dataset.configEndpoint+(host.dataset.configEndpoint.includes('?')?'&':'?')+'productId='+id;try{const[rawCfg,colors]=await Promise.all([json(cp),palette(host.dataset.paletteUrl)]),cfg=normalizeConfig(rawCfg);if(!cfg.layers.length)throw new Error('Keine Visualisierungsebenen in der Produktkonfiguration gefunden.');const baseUrl=url(ep,id,'base')"

if old not in text:
    raise SystemExit('Expected 6.4.2 init block not found')
text = text.replace(old, new, 1)
text = text.replace("console.debug('GAWELA Farbkonfigurator:',e?.message||e)", "console.warn('GAWELA Farbkonfigurator:',e?.message||e)")
js_path.write_text(text, encoding='utf-8')

module_path = root / 'module.json'
module = module_path.read_text(encoding='utf-8')
module = module.replace('"Version": "6.4.2"', '"Version": "6.4.3"')
module_path.write_text(module, encoding='utf-8')
