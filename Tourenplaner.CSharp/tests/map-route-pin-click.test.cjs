function runRoutePinClickTests(source) {
 const begin=source.indexOf('let routePointerGesture = null;'), end=source.indexOf('const normalizeShape =',begin);
 const sb=source.indexOf('const selectRouteStopFromMarker = () =>'), se=source.indexOf('if (!dismissedOrderPopupIds.has(String(stop.id',sb);
 if ([begin,end,sb,se].some(i=>i<0)) throw Error('Route selection helpers missing');
 const listeners=new Map(), routeMarkerMap=new Map(), messages=[];
 const document={addEventListener(type,handler,capture){if(!capture)throw Error('Must precede SDK');listeners.set(type,handler);}};
 let visible=false,popupFails=false,cluster=false,zoomCalls=0;
 const popup={addTo(){if(popupFails)throw Error('Popup failed');visible=true;}};
 const window={chrome:{webview:{postMessage(m){messages.push(m);}}}};
 const make=new Function('marker','popup','window','zoomToScreenCluster','stop',
  "const markerEl={dataset:{}};const map={};const dismissedOrderPopupIds=new Set();let draggedDuringInteraction=false;let stickyPopupOrderId='';"+
  'const scalePopupElement=()=>{};const enableOrderPopupInteraction=()=>{};const bringOrderPopupToFront=()=>{};'+
  source.slice(sb,se)+';return marker;');
 const install=id=>{const m=make({__gawelaOrderId:id},popup,window,()=>{if(cluster)zoomCalls++;return cluster;},{id});routeMarkerMap.set(id,m);};
 new Function('document','routeMarkerMap','map','selectRouteStopNearPoint','let routeStopCanvasClickBound=false;'+
  source.slice(begin,end)+';ensureRouteStopCanvasClickBinding();')(document,routeMarkerMap,{on(){}},()=>{});
 const assert=(ok,text)=>{if(!ok)throw Error(text);};
 const dispatch=(type,id,extra={})=>{const e={button:0,detail:1,pointerId:1,clientX:100,clientY:100,
  target:{closest(selector){return selector==='[data-gawela-route-stop-id]' && id?{dataset:{gawelaRouteStopId:id}}:null;}},
  stopImmediatePropagation(){this.stopped=true;},preventDefault(){},...extra};listeners.get(type)(e);return e;};
 let checks=0;
 for(const id of ['222025','222018::delivery::D1']) {
  install(id);let before=messages.length;visible=true;
  const popupTarget={closest(selector){return selector==='.gawela-info-card'?{}:selector==='[data-gawela-route-stop-id]'?{dataset:{gawelaRouteStopId:id}}:null;}};
  dispatch('pointerdown',id,{target:popupTarget});dispatch('pointerup',id,{target:popupTarget});
  const popupClick=dispatch('click',id,{target:popupTarget});
  assert(messages.length===before&&!popupClick.stopped,'Popup controls inside SDK marker DOM must reach their own click handlers without route selection');checks++;
  dispatch('pointerdown',id);dispatch('pointerup',id);
  assert(messages.length===before+1&&messages.at(-1)==='routeSelect:'+id,'Pointerup must select exact order before SDK click');checks++;
  assert(dispatch('click',id).stopped&&visible&&messages.length===before+1,'Click must not toggle or duplicate');checks++;
  dispatch('pointerdown',id);install(id);dispatch('pointerup',null);
  assert(messages.length===before+2,'Selection must survive marker replacement and release retargeting');checks++;
  dispatch('pointerdown',null);dispatch('pointerup',id);
  assert(messages.length===before+2,'Gesture starting outside marker must not select');checks++;
  dispatch('pointerdown',id);dispatch('pointerup',id,{clientX:120});dispatch('click',id);
  assert(messages.length===before+2,'Drag must not select');checks++;
  dispatch('pointerdown',id);dispatch('pointercancel',id);dispatch('pointerup',id);dispatch('click',id);
  assert(messages.length===before+2,'Cancelled gesture must not select');checks++;
  dispatch('pointerdown',id,{button:2});dispatch('pointerup',id,{button:2});dispatch('click',id,{button:2});
  assert(messages.length===before+2,'Right-click must not select');checks++;
  dispatch('click',id,{detail:0});assert(messages.length===before+3,'Keyboard selection must work');checks++;
  cluster=true;const oldZoom=zoomCalls;dispatch('pointerdown',id);dispatch('pointerup',id);dispatch('click',id);
  assert(zoomCalls===oldZoom+1&&messages.length===before+3,'Cluster must zoom once without selecting member');checks++;
  cluster=false;popupFails=true;dispatch('pointerdown',id);
  try{dispatch('pointerup',id);}catch(e){if(e.message!=='Popup failed')throw e;}
  assert(messages.length===before+4,'Popup failure must not block host selection');checks++;popupFails=false;
 }
 return checks;
}
if(typeof module!=='undefined'){
 const fs=require('node:fs'),path=require('node:path');
 console.log(runRoutePinClickTests(fs.readFileSync(path.join(__dirname,'../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'),'utf8'))+' route pin checks passed');
}
