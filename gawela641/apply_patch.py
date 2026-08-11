from pathlib import Path
import json, sys

root = Path(sys.argv[1])

def write(rel, text):
    path = root / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')

write('Startup.cs', r'''using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Filters;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator;

internal class Startup : StarterBase
{
    public override bool Matches(IApplicationContext appContext)
        => appContext.IsInstalled;

    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.Configure<MvcOptions>(o =>
        {
            o.Filters.AddEndpointFilter<GawelaProductDetailFilter, ProductController>()
                .ForAction(x => x.ProductDetails(0, null))
                .WhenNonAjax();
        });

        services.AddSingleton<GawelaAssetStore>();
    }
}
''')

write('Filters/GawelaProductDetailFilter.cs', r'''using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core.Widgets;
using Gawela.ColorConfigurator.Components;

namespace Gawela.ColorConfigurator.Filters;

public sealed class GawelaProductDetailFilter : IAsyncResultFilter
{
    private readonly IWidgetProvider _widgetProvider;

    public GawelaProductDetailFilter(IWidgetProvider widgetProvider)
    {
        _widgetProvider = widgetProvider;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        _widgetProvider.RegisterViewComponent<GawelaColorHostViewComponent>(
            "productdetails_pictures_top",
            order: -1000);
        await next();
    }
}
''')

write('Components/GawelaColorHostViewComponent.cs', r'''using Microsoft.AspNetCore.Mvc;

namespace Gawela.ColorConfigurator.Components;

public sealed class GawelaColorHostViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}
''')

write('Views/Shared/Components/GawelaColorHost/Default.cshtml', r'''<link rel="stylesheet"
      href="@Url.Content("~/Modules/Gawela.ColorConfigurator/gawela-color.css")"
      sm-target-zone="stylesheets"
      sm-key="gawela-color-css" />
<script src="@Url.Content("~/Modules/Gawela.ColorConfigurator/gawela-color.js")"
        sm-target-zone="scripts"
        sm-key="gawela-color-js"></script>
<div class="gawela-color-host" hidden aria-hidden="true"
     data-asset-endpoint="@Url.Action("Asset", "GawelaColor")"
     data-palette-url="@Url.Content("~/Modules/Gawela.ColorConfigurator/colors.json")"
     data-corpus-label="Farben Korpus/Gestell ML"
     data-doors-label="Farben Türen/Schubladen ML"
     data-base-corpus-ral="7035" data-base-doors-ral="7035"
     data-default-corpus-ral="7035" data-default-doors-ral="7035"
     data-thumbnail-label="Farbe konfigurieren"></div>
''')

write('Views/Shared/BlockTemplates/gawelacolor/Public.cshtml', r'''@model GawelaColorBlock
@* Since 6.4.1 the optimized configurator is injected directly on product detail pages.
   The old Page Builder block stays silent to avoid duplicate gallery entries after an upgrade. *@
<div class="gawela-color-legacy-marker" hidden aria-hidden="true"></div>
''')

write('wwwroot/gawela-color.css', r'''.gawela-color-host,.gawela-color-legacy-marker{display:none!important}.gawela-gallery-slide .gawela-gallery-viewport{cursor:default;background:transparent}.gawela-color-stage{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;padding:.35rem}.gawela-color-canvas{display:block;width:auto;height:auto;max-width:100%;max-height:100%}.gawela-config-badge{position:absolute;z-index:4;top:.65rem;right:.65rem;display:inline-flex;gap:.35rem;align-items:center;padding:.35rem .55rem;border-radius:999px;background:rgba(255,255,255,.92);border:1px solid rgba(0,0,0,.12);box-shadow:0 .1rem .35rem rgba(0,0,0,.08);font-size:.78rem;font-weight:600;pointer-events:none}.gawela-color-meta{position:absolute;z-index:4;left:.65rem;right:.65rem;bottom:2rem;padding:.4rem .55rem;border-radius:.4rem;background:rgba(255,255,255,.9);border:1px solid rgba(0,0,0,.1);font-size:.75rem;text-align:left;pointer-events:none}.gawela-color-disclaimer{position:absolute;z-index:4;left:.65rem;right:.65rem;bottom:.4rem;font-size:.62rem;color:rgba(0,0,0,.6);text-align:left;pointer-events:none}.gawela-gallery-thumb-button{padding:0;background:#fff;color:inherit;cursor:pointer;appearance:none;-webkit-appearance:none}.gawela-gallery-thumb-image{object-fit:contain}.gawela-gallery-thumb-icon{position:absolute;z-index:3;right:.2rem;bottom:.2rem;display:inline-flex;align-items:center;justify-content:center;width:1.45rem;height:1.45rem;border-radius:50%;background:rgba(255,255,255,.95);border:1px solid rgba(0,0,0,.18);box-shadow:0 .05rem .2rem rgba(0,0,0,.12);font-size:.72rem;pointer-events:none}@media(max-width:767.98px){.gawela-config-badge span{display:none}.gawela-config-badge{width:2rem;height:2rem;justify-content:center;padding:0}.gawela-color-meta{left:.4rem;right:.4rem;bottom:1.8rem;font-size:.66rem}.gawela-color-disclaimer{left:.4rem;right:.4rem;font-size:.56rem}}
''')

write('wwwroot/gawela-color.js', r'''(function(){'use strict';const sleep=ms=>new Promise(r=>setTimeout(r,ms)),norm=s=>(s||'').replace(/\s+/g,' ').trim().toLowerCase(),ralFrom=s=>{const m=(s||'').match(/RAL\s*[-:]?\s*(\d{4})/i)||(s||'').match(/\b(\d{4})\b/);return m?m[1]:null};function productId(){const e=document.querySelector('#main-update-container[data-id]'),id=parseInt(e&&e.dataset.id,10);return Number.isFinite(id)&&id>0?id:null}function choice(label){const w=norm(label);for(const e of document.querySelectorAll('.pd-variants .choice')){const a=norm(e.querySelector('.choice-label')?.textContent);if(a&&(a===w||a.includes(w)||w.includes(a)))return e}return null}function choiceText(e){if(!e)return'';const s=e.querySelector('select');if(s)return[s.selectedOptions?.[0]?.textContent,s.value].join(' ');const i=e.querySelector('input[type=radio]:checked,input[type=checkbox]:checked');if(!i)return'';const l=i.id?e.querySelector('label[for="'+CSS.escape(i.id)+'"]'):null;return[i.getAttribute('aria-label'),i.title,i.value,l?.textContent,l?.getAttribute('title'),l?.querySelector('[title]')?.getAttribute('title')].join(' ')}function selectedRal(label,fallback){return ralFrom(choiceText(choice(label)))||fallback}function assetUrl(endpoint,p,k){return endpoint+(endpoint.includes('?')?'&':'?')+'productId='+encodeURIComponent(p)+'&kind='+encodeURIComponent(k)+'&v='+Date.now()}function image(src){return new Promise((res,rej)=>{const i=new Image;i.onload=()=>res(i);i.onerror=()=>rej(new Error('Bild konnte nicht geladen werden: '+src));i.src=src})}async function palette(src){const r=await fetch(src,{credentials:'same-origin'});if(!r.ok)throw new Error('Farbpalette konnte nicht geladen werden.');const j=await r.json();return j.colors||j}function pixels(img,w,h){const c=document.createElement('canvas');c.width=w;c.height=h;const x=c.getContext('2d',{willReadFrequently:true});x.drawImage(img,0,0,w,h);return x.getImageData(0,0,w,h)}const luma=(r,g,b)=>.2126*r+.7152*g+.0722*b,clamp=v=>Math.max(0,Math.min(255,Math.round(v)));function strength(m,i){return((m[i]+m[i+1]+m[i+2])/765)*(m[i+3]/255)}function avgLuma(b,m){let s=0,w=0;for(let i=0;i<b.length;i+=4){const x=strength(m,i);if(x>.01){s+=luma(b[i],b[i+1],b[i+2])*x;w+=x}}return w?s/w:180}function tint(b,rgb,ref){const f=Math.max(.35,Math.min(1.65,luma(b[0],b[1],b[2])/Math.max(ref,1)));return[clamp(rgb[0]*f),clamp(rgb[1]*f),clamp(rgb[2]*f)]}function draw(s){if(!s.ctx)return;let cr=selectedRal(s.corpusLabel,s.defaultCorpusRal),dr=selectedRal(s.doorsLabel,s.defaultDoorsRal);if(!s.colors[cr])cr=s.defaultCorpusRal;if(!s.colors[dr])dr=s.defaultDoorsRal;const cc=s.colors[cr],dc=s.colors[dr];if(!cc||!dc)return;const b=s.base.data,cm=s.corpus.data,dm=s.doors.data,o=new ImageData(new Uint8ClampedArray(b),s.w,s.h),d=o.data;for(let i=0;i<b.length;i+=4){let r=b[i],g=b[i+1],bl=b[i+2];const cs=strength(cm,i),ds=strength(dm,i);if(cs>.01&&cr!==s.baseCorpusRal){const t=tint([b[i],b[i+1],b[i+2]],cc.rgb,s.corpusLuma);r=r*(1-cs)+t[0]*cs;g=g*(1-cs)+t[1]*cs;bl=bl*(1-cs)+t[2]*cs}if(ds>.01&&dr!==s.baseDoorsRal){const t=tint([b[i],b[i+1],b[i+2]],dc.rgb,s.doorsLuma);r=r*(1-ds)+t[0]*ds;g=g*(1-ds)+t[1]*ds;bl=bl*(1-ds)+t[2]*ds}d[i]=clamp(r);d[i+1]=clamp(g);d[i+2]=clamp(bl);d[i+3]=b[i+3]}s.ctx.putImageData(o,0,0);if(s.info){const lab=(r,c)=>'RAL '+r+(c.name?' '+c.name:'');s.info.textContent='Korpus: '+lab(cr,cc)+' · Türen: '+lab(dr,dc)}}function gallery(){const root=document.querySelector('#pd-gallery'),$=window.jQuery;if(!root||!$)return null;const instance=$(root).data('smartGallery');if(!instance||!instance.gallery)return null;const main=root.querySelector('.gal'),nav=root.querySelector('.gal-nav .gal-track');return main&&nav?{root,instance,main,nav}:null}function makeSlide(s){const item=document.createElement('div');item.className='gal-item gawela-gallery-slide';item.setAttribute('role','listitem');const v=document.createElement('div');v.className='gal-item-viewport gawela-gallery-viewport';const stage=document.createElement('div');stage.className='gawela-color-stage';const c=document.createElement('canvas');c.className='gawela-color-canvas';c.width=s.w;c.height=s.h;c.setAttribute('aria-label','Dynamische Farbvorschau');stage.appendChild(c);const b=document.createElement('div');b.className='gawela-config-badge';b.innerHTML='<i class="fa fa-palette" aria-hidden="true"></i><span>'+s.thumbLabel+'</span>';stage.appendChild(b);v.appendChild(stage);const info=document.createElement('div');info.className='gawela-color-meta';v.appendChild(info);const n=document.createElement('small');n.className='gawela-color-disclaimer';n.textContent='Bildschirmdarstellung unverbindlich. Farbabweichungen sind möglich.';v.appendChild(n);item.appendChild(v);s.ctx=c.getContext('2d',{willReadFrequently:true});s.info=info;return item}function makeThumb(s){const item=document.createElement('div');item.className='gal-item gawela-gallery-thumb';const b=document.createElement('button');b.type='button';b.className='gal-item-viewport gawela-gallery-thumb-button';b.title=s.thumbLabel;b.setAttribute('aria-label',s.thumbLabel);b.setAttribute('role','option');const i=document.createElement('img');i.className='gal-item-content gawela-gallery-thumb-image';i.src=s.baseUrl;i.alt=s.thumbLabel;b.appendChild(i);const icon=document.createElement('span');icon.className='gawela-gallery-thumb-icon';icon.innerHTML='<i class="fa fa-palette" aria-hidden="true"></i>';b.appendChild(icon);item.appendChild(b);return item}function installSlide(s){let g=gallery();if(!g)return false;const ex=g.root.querySelector('.gawela-gallery-slide:not(.slick-cloned)');if(ex){const idx=parseInt(ex.getAttribute('data-slick-index'),10);if(Number.isFinite(idx))s.index=idx;return true}const current=Number.isFinite(g.instance.currentIndex)?g.instance.currentIndex:0;g.instance.reset();const root=document.querySelector('#pd-gallery');if(!root)return false;const main=root.querySelector('.gal'),nav=root.querySelector('.gal-nav .gal-track');if(!main||!nav)return false;main.querySelectorAll(':scope > .gawela-gallery-slide').forEach(x=>x.remove());nav.querySelectorAll(':scope > .gawela-gallery-thumb').forEach(x=>x.remove());const count=main.querySelectorAll(':scope > .gal-item').length;main.appendChild(makeSlide(s));nav.appendChild(makeThumb(s));s.index=count;g.instance.options.startIndex=Math.min(current,Math.max(count-1,0));g.instance.init();root.querySelector('.gal-nav-cell')?.classList.remove('gal-nav-hidden');draw(s);return true}function jump(s){const g=gallery();if(!g||!Number.isFinite(s.index))return;try{g.instance.goTo(s.index)}catch(e){console.debug('GAWELA Farbkonfigurator:',e)}}async function waitGallery(s,n=60){for(let i=0;i<n;i++){if(installSlide(s))return true;await sleep(50)}return false}function bind(s){document.addEventListener('change',e=>{const c=e.target.closest?.('.pd-variants .choice');if(!c)return;const label=norm(c.querySelector('.choice-label')?.textContent);if(!(label.includes(norm(s.corpusLabel))||label.includes(norm(s.doorsLabel))))return;s.pending=true;setTimeout(()=>draw(s),25);setTimeout(()=>{if(s.pending){installSlide(s);draw(s);jump(s)}},250)},true);if(window.jQuery)window.jQuery('#main-update-container').off('updated.gawela641').on('updated.gawela641',async function(){await waitGallery(s,30);draw(s);if(s.pending){jump(s);s.pending=false}})}async function init(host){if(host.dataset.gawelaInit)return;host.dataset.gawelaInit='1';const id=productId();if(!id)return;const endpoint=host.dataset.assetEndpoint,baseUrl=assetUrl(endpoint,id,'base'),corpusUrl=assetUrl(endpoint,id,'corpus'),doorsUrl=assetUrl(endpoint,id,'doors');try{const[colors,bi,ci,di]=await Promise.all([palette(host.dataset.paletteUrl),image(baseUrl),image(corpusUrl),image(doorsUrl)]),w=bi.naturalWidth,h=bi.naturalHeight;if(!w||!h||ci.naturalWidth!==w||ci.naturalHeight!==h||di.naturalWidth!==w||di.naturalHeight!==h)throw new Error('Basisbild und beide Masken müssen exakt gleich gross sein.');const base=pixels(bi,w,h),corpus=pixels(ci,w,h),doors=pixels(di,w,h),s={id,w,h,base,corpus,doors,colors,baseUrl,ctx:null,info:null,index:null,pending:false,corpusLabel:host.dataset.corpusLabel,doorsLabel:host.dataset.doorsLabel,baseCorpusRal:host.dataset.baseCorpusRal||'7035',baseDoorsRal:host.dataset.baseDoorsRal||'7035',defaultCorpusRal:host.dataset.defaultCorpusRal||'7035',defaultDoorsRal:host.dataset.defaultDoorsRal||'7035',thumbLabel:host.dataset.thumbnailLabel||'Farbe konfigurieren',corpusLuma:avgLuma(base.data,corpus.data),doorsLuma:avgLuma(base.data,doors.data)};if(!await waitGallery(s))throw new Error('Smartstore-Produktgalerie nicht verfügbar.');draw(s);bind(s)}catch(e){console.debug('GAWELA Farbkonfigurator:',e?.message||e)}}const boot=()=>document.querySelectorAll('.gawela-color-host').forEach(init);document.readyState==='loading'?document.addEventListener('DOMContentLoaded',boot):boot()})();
''')

# Version bump and implicit usings.
module_path = root / 'module.json'
module = json.loads(module_path.read_text(encoding='utf-8'))
module['Version'] = '6.4.1'
module['Description'] = 'Dynamische RAL-Farbvorschau als zusätzlicher Smartstore-Galerie-Slide mit automatischem Wechsel bei Farbauswahl.'
module_path.write_text(json.dumps(module, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

proj = root / 'Gawela.ColorConfigurator.csproj'
s = proj.read_text(encoding='utf-8')
if '<ImplicitUsings>' not in s:
    s = s.replace('<Product>GAWELA Farbkonfigurator</Product>', '<Product>GAWELA Farbkonfigurator</Product>\n    <ImplicitUsings>enable</ImplicitUsings>')
proj.write_text(s, encoding='utf-8')
