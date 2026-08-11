from pathlib import Path
import sys

root = Path(sys.argv[1])
js_path = root / 'wwwroot' / 'gawela-color.js'
css_path = root / 'wwwroot' / 'gawela-color.css'
module_path = root / 'module.json'

js = js_path.read_text(encoding='utf-8')

old_slide = "function slide(s){const item=document.createElement('div');item.className='gal-item gawela-gallery-slide';item.setAttribute('role','listitem');const v=document.createElement('div');v.className='gal-item-viewport gawela-gallery-viewport';const st=document.createElement('div');st.className='gawela-color-stage';const c=document.createElement('canvas');c.className='gawela-color-canvas';c.width=s.w;c.height=s.h;st.appendChild(c);const b=document.createElement('div');b.className='gawela-config-badge';b.innerHTML='<i class=\"fa fa-palette\"></i><span>'+s.thumb+'</span>';st.appendChild(b);v.appendChild(st);const info=document.createElement('div');info.className='gawela-color-meta';v.appendChild(info);const n=document.createElement('small');n.className='gawela-color-disclaimer';n.textContent='Bildschirmdarstellung unverbindlich. Farbabweichungen sind möglich.';v.appendChild(n);item.appendChild(v);s.ctx=c.getContext('2d',{willReadFrequently:true});s.info=info;return item}"
new_slide = "function slide(s){const item=document.createElement('div');item.className='gal-item gawela-gallery-slide';item.setAttribute('role','listitem');const v=document.createElement('div');v.className='gal-item-viewport gawela-gallery-viewport';const st=document.createElement('div');st.className='gawela-color-stage';const c=document.createElement('canvas');c.className='gawela-color-canvas';c.width=s.w;c.height=s.h;st.appendChild(c);const b=document.createElement('div');b.className='gawela-config-badge';b.innerHTML='<i class=\"fa fa-palette\"></i><span>'+s.thumb+'</span>';st.appendChild(b);v.appendChild(st);const f=document.createElement('div');f.className='gawela-color-footer';const info=document.createElement('div');info.className='gawela-color-meta';f.appendChild(info);const n=document.createElement('small');n.className='gawela-color-disclaimer';n.textContent='Bildschirmdarstellung unverbindlich. Farbabweichungen sind möglich. Die tatsächliche Ausführung kann von der dargestellten Abbildung abweichen.';f.appendChild(n);v.appendChild(f);item.appendChild(v);s.ctx=c.getContext('2d',{willReadFrequently:true});s.info=info;return item}"
if old_slide not in js:
    raise SystemExit('Expected slide() block not found')
js = js.replace(old_slide, new_slide, 1)

old_start = "g.instance.options.startIndex=Math.min(current,Math.max(count-1,0));g.instance.init();"
new_start = "g.instance.options.startIndex=s.pending?count:Math.min(current,Math.max(count-1,0));g.instance.init();"
if old_start not in js:
    raise SystemExit('Expected gallery startIndex block not found')
js = js.replace(old_start, new_start, 1)

old_bind = "function bind(s){document.addEventListener('change',e=>{const c=e.target.closest?.('.pd-variants .choice');if(!c||!relevant(s,c))return;s.pending=true;setTimeout(()=>draw(s),20);setTimeout(()=>{install(s);draw(s);jump(s)},250)},true);if(window.jQuery)window.jQuery('#main-update-container').off('updated.gawela642').on('updated.gawela642',async()=>{await wait(s,30);draw(s);if(s.pending){jump(s);s.pending=false}})}"
new_bind = "function unfreeze(s){if(s.freeze){s.freeze.remove();s.freeze=null}}function freeze(s){unfreeze(s);const g=gallery();if(!g||!Number.isFinite(s.index)||g.instance.currentIndex!==s.index)return;const src=g.root.querySelector('.gawela-gallery-slide.slick-current:not(.slick-cloned) .gawela-gallery-viewport')||g.root.querySelector('.gawela-gallery-slide:not(.slick-cloned) .gawela-gallery-viewport');if(!src)return;const rect=g.main.getBoundingClientRect();if(!rect.width||!rect.height)return;const o=document.createElement('div');o.className='gawela-color-freeze';o.style.left=rect.left+'px';o.style.top=rect.top+'px';o.style.width=rect.width+'px';o.style.height=rect.height+'px';const clone=src.cloneNode(true),sc=src.querySelector('canvas'),dc=clone.querySelector('canvas');if(sc&&dc){const im=document.createElement('img');im.className='gawela-color-canvas';try{im.src=sc.toDataURL('image/png')}catch{}dc.replaceWith(im)}o.appendChild(clone);document.body.appendChild(o);s.freeze=o}function finish(s){jump(s);requestAnimationFrame(()=>requestAnimationFrame(()=>unfreeze(s)));s.pending=false}function bind(s){document.addEventListener('change',e=>{const c=e.target.closest?.('.pd-variants .choice');if(!c||!relevant(s,c))return;s.pending=true;jump(s);draw(s);requestAnimationFrame(()=>freeze(s));setTimeout(()=>{install(s);draw(s);jump(s)},250);setTimeout(async()=>{if(!s.pending)return;await wait(s,30);draw(s);finish(s)},3000)},true);if(window.jQuery)window.jQuery('#main-update-container').off('updated.gawela644').on('updated.gawela644',async()=>{await wait(s,30);draw(s);if(s.pending)finish(s)})}"
if old_bind not in js:
    raise SystemExit('Expected bind() block not found')
js = js.replace(old_bind, new_bind, 1)

old_state = "ctx:null,info:null,index:null,pending:false}"
new_state = "ctx:null,info:null,index:null,pending:false,freeze:null}"
if old_state not in js:
    raise SystemExit('Expected state block not found')
js = js.replace(old_state, new_state, 1)

js_path.write_text(js, encoding='utf-8')

css = """.gawela-color-host,.gawela-color-legacy-marker{display:none!important}
.gawela-gallery-slide .gawela-gallery-viewport{cursor:default;background:#fff;display:flex;flex-direction:column;height:100%;min-height:0;box-sizing:border-box}
.gawela-color-stage{position:relative;display:flex;align-items:center;justify-content:center;flex:1 1 auto;min-height:0;padding:.35rem;overflow:hidden}
.gawela-color-canvas{display:block;width:auto;height:auto;max-width:100%;max-height:100%;object-fit:contain}
.gawela-config-badge{position:absolute;z-index:4;top:.65rem;right:.65rem;display:inline-flex;gap:.35rem;align-items:center;padding:.35rem .55rem;border-radius:999px;background:rgba(255,255,255,.92);border:1px solid rgba(0,0,0,.12);box-shadow:0 .1rem .35rem rgba(0,0,0,.08);font-size:.78rem;font-weight:600;pointer-events:none}
.gawela-color-footer{flex:0 0 auto;width:100%;box-sizing:border-box;padding:.35rem .65rem .55rem;background:#fff}
.gawela-color-meta{position:static;padding:.4rem .55rem;border-radius:.4rem;background:#fff;border:1px solid rgba(0,0,0,.1);font-size:.75rem;text-align:left;line-height:1.35;pointer-events:none}
.gawela-color-disclaimer{position:static;display:block;margin-top:.4rem;font-size:.62rem;line-height:1.4;color:rgba(0,0,0,.62);text-align:left;pointer-events:none}
.gawela-color-freeze{position:fixed;z-index:1065;background:#fff;pointer-events:none;overflow:hidden;box-sizing:border-box}
.gawela-color-freeze .gawela-gallery-viewport{display:flex;flex-direction:column;width:100%;height:100%;min-height:0;background:#fff}
.gawela-gallery-thumb-button{padding:0;background:#fff;color:inherit;cursor:pointer;appearance:none;-webkit-appearance:none}
.gawela-gallery-thumb-image{object-fit:contain}
.gawela-gallery-thumb-icon{position:absolute;z-index:3;right:.2rem;bottom:.2rem;display:inline-flex;align-items:center;justify-content:center;width:1.45rem;height:1.45rem;border-radius:50%;background:rgba(255,255,255,.95);border:1px solid rgba(0,0,0,.18);box-shadow:0 .05rem .2rem rgba(0,0,0,.12);font-size:.72rem;pointer-events:none}
@media(max-width:767.98px){.gawela-config-badge span{display:none}.gawela-config-badge{width:2rem;height:2rem;justify-content:center;padding:0}.gawela-color-footer{padding:.3rem .4rem .45rem}.gawela-color-meta{font-size:.66rem}.gawela-color-disclaimer{font-size:.56rem}}
"""
css_path.write_text(css, encoding='utf-8')

module = module_path.read_text(encoding='utf-8')
module = module.replace('"Version": "6.4.3"', '"Version": "6.4.4"')
module_path.write_text(module, encoding='utf-8')
