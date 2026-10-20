// Run with: node Tourenplaner.CSharp/tests/map-pin-popup.test.cjs
// Executes the production popup builder embedded in the map document.
function runMapPinPopupTests(source) {
  const start = source.indexOf('const escapeHtml =');
  const end = source.indexOf('const applyBaseStyle =', start);
  if (start < 0 || end < 0) throw new Error('Map popup builder not found');

  const create = new Function(source.slice(start, end) + '; return buildPinPopupHtml;');
  const buildPopup = create();
  const assert = (condition, message) => { if (!condition) throw new Error(message); };

  const marked = buildPopup({ customer: 'Kunde', isAlternativeLiefertour: true });
  assert(marked.includes('Evtl. Liefertour'), 'Alternative Liefertour must be shown in the popup');

  const regular = buildPopup({ customer: 'Kunde', isAlternativeLiefertour: false });
  assert(!regular.includes('Evtl. Liefertour'), 'Regular orders must not show the alternative Liefertour line');

  const missing = buildPopup({ customer: 'Kunde' });
  assert(!missing.includes('Evtl. Liefertour'), 'Missing flag must not show the alternative Liefertour line');

  const withNotes = buildPopup({
    customer: 'Kunde',
    notes: 'Dringender Hinweis',
    showNotes: true,
    showStreet: true,
    street: 'Teststrasse 1'
  });
  assert(withNotes.includes("class='gawela-info-card-notes'>Dringender Hinweis</p>"), 'Notes must use the dedicated header style');
  assert(withNotes.indexOf('Dringender Hinweis') < withNotes.indexOf('Teststrasse 1'), 'Notes must be rendered before the address');
  assert(!withNotes.includes('>Notizen<'), 'Notes must not have a label');

  const hiddenNotes = buildPopup({ customer: 'Kunde', notes: 'Unsichtbar', showNotes: false });
  assert(!hiddenNotes.includes('Unsichtbar'), 'Hidden notes must not be rendered');

  assert(source.includes("popupEl.classList.add('gawela-order-popup', 'gawela-popup-front')"), 'Activated popup must receive the foreground class');
  assert(source.includes("popupEl.addEventListener('pointerdown', activate)"), 'Popup pointer interaction must activate the popup');
  assert(source.includes('.gawela-order-popup.gawela-popup-front { z-index: 1500 !important; }'), 'Foreground popup must have a higher stack level');
  assert(!source.includes('.gawela-order-popup * { pointer-events: auto !important; }'), 'Transparent popup areas must not intercept marker clicks');
  assert(source.includes('.gawela-order-popup .gawela-info-card * { pointer-events: auto !important; }'), 'Only visible info-card content must accept pointer input');

  const withManyProducts = buildPopup({
    customer: 'Kunde',
    showProducts: true,
    products: ['Produkt 1', 'Produkt 2', 'Produkt 3', 'Produkt 4']
  });
  assert((withManyProducts.match(/gawela-info-card-product-extra/g) || []).length === 2, 'Only products after the first two must initially be hidden');
  assert(withManyProducts.includes("aria-expanded='false'>Mehr anzeigen</button>"), 'More than two products must render the expand button');

  const withTwoProducts = buildPopup({ customer: 'Kunde', showProducts: true, products: ['Produkt 1', 'Produkt 2'] });
  assert(!withTwoProducts.includes('gawela-info-card-products-toggle'), 'Two products must not render the expand button');
  assert(source.includes("toggle.textContent = expanded ? 'Mehr anzeigen' : 'Weniger anzeigen'"), 'Product toggle must support expanding and collapsing');
  assert(!source.includes('gawelaPointerToggleAt'), 'Product toggle must not suppress clicks with a pointer timestamp');
  assert(source.includes("target.closest('.gawela-info-card-products-toggle')"), 'Delegated popup handling must resolve the product toggle at interaction time');
  assert(source.includes('popup.setLngLat(popup.getLngLat())'), 'Popup must be repositioned after its height changes');
  assert(source.includes("popupEl.addEventListener('wheel'"), 'Mouse wheel input over an info card must be forwarded to map zoom');
  assert(source.includes("changeMapZoom(evt.deltaY < 0 ? 0.75 : -0.75, around)"), 'Info-card wheel zoom must preserve the pointer position');

  const stickyStart = source.indexOf('window.gawelaSetStickyPopupOrderId = function(orderId) {');
  const stickyEnd = source.indexOf('window.gawelaClearTempSearchMarker', stickyStart);
  const states = { a: true, b: true, c: true };
  const pins = Object.keys(states).map(id => ({ __gawelaOrderId: id,
    getPopup: () => ({ remove() { states[id] = false; } }) }));
  const stickyApi = new Function('window', 'mapMarkers', 'routeMarkers', 'markerMap', 'routeMarkerMap',
    "let stickyPopupOrderId = ''; let routePopupVisible = false;" + source.slice(stickyStart, stickyEnd) +
    ';return {select: window.gawelaSetStickyPopupOrderId, all: () => {routePopupVisible = true;}};')({}, pins.slice(0, 1), pins.slice(1),
      new Map(pins.slice(0, 1).map(p => [p.__gawelaOrderId, p])), new Map(pins.slice(1).map(p => [p.__gawelaOrderId, p])));
  stickyApi.select('b');
  assert(!states.a && states.b && !states.c, 'Changing selection must close previous order and route cards');
  stickyApi.select('');
  assert(!states.b, 'Deselection must close the selected card');
  stickyApi.all();
  states.a = true;
  stickyApi.select('');
  assert(states.a, 'Explicit show-all mode must remain available');
  stickyApi.select('b');
  states.b = true;
  stickyApi.select('');
  assert(!states.b, 'Explicitly closing the selected order must dismiss its card even in show-all mode');
  assert(regular.includes("class='gawela-info-card-close'"), 'Each card must have a manual close button');
  assert(regular.includes("aria-label='Infokarte schließen'"), 'Close button must have an accessible label');
  const closeStart = source.indexOf('const closeCard = evt => {');
  const closeEnd = source.indexOf('popupEl.__gawelaCloseCard = closeCard;', closeStart);
  let removed = false;
  const popupEl = {};
  const dismissed = new Set();
  let underlyingRemoved = false;
  const overlappingMarkers = new Map([['route-a', {getPopup:()=>({remove(){underlyingRemoved=true;}})}]]);
  const closeApi = new Function('popup', 'popupEl', 'dismissedOrderPopupIds', 'markerMap','routeMarkerMap', 'let stickyPopupOrderId = "route-a"; let activeOrderPopupElement = popupEl;' + source.slice(closeStart, closeEnd) +
    ';return {close:closeCard,active:()=>activeOrderPopupElement,sticky:()=>stickyPopupOrderId};')({__gawelaOrderId:'route-a',remove(){removed = true;}}, popupEl, dismissed, overlappingMarkers, new Map());
  const close = closeApi.close;
  const event = {target:{closest:()=>({})},preventDefault(){this.prevented=true;},stopImmediatePropagation(){this.stopped=true;}};
  close(event);
  assert(removed && event.prevented && event.stopped, 'Close must remove popup and stop marker selection');
  assert(closeApi.active() === null, 'Closing must clear the active popup reference');
  const delegationStart = source.indexOf('const findOrderCardClosePopup =');
  const delegationEnd = source.indexOf('const enableOrderPopupInteraction =', delegationStart);
  const captureHandlers = {};
  new Function('window', source.slice(delegationStart,delegationEnd))({addEventListener(type,handler,capture){
    assert(capture===true,'Close delegation must precede SDK handlers');captureHandlers[type]=handler;
  }});
  const container = {__gawelaCloseCard:close};
  const button = {closest(selector){return selector==='.gawela-order-popup'?container:null;}};
  const makeCloseEvent = () => ({button:0,target:{closest(selector){return selector==='.gawela-info-card-close'?button:null;}},
    preventDefault(){this.prevented=true;},stopImmediatePropagation(){this.stopped=true;}});
  removed=false;
  const press=makeCloseEvent();captureHandlers.pointerdown(press);
  assert(removed&&press.stopped&&press.prevented,'Card must close on press even if SDK never delivers a click');
  for (const type of ['mousedown','pointerup','mouseup','click']) {
    const gesture={...makeCloseEvent(),target:{closest:()=>null}};
    captureHandlers[type](gesture);
    assert(gesture.stopped&&gesture.prevented,'Release retargeted to map must not select an underlying pin');
  }
  let currentHandlerCalled=false;
  container.__gawelaCloseCard=()=>{currentHandlerCalled=true;};
  captureHandlers.click(makeCloseEvent());
  assert(currentHandlerCalled,'Delegation must use the latest popup binding after DOM reuse');
  const unrelated={target:{closest:()=>null},stopImmediatePropagation(){throw Error('Unrelated click blocked');}};
  captureHandlers.click(unrelated);
  assert(true,'Unrelated controls must not be intercepted');
  container.__gawelaCloseCard=close;
  removed=false;
  captureHandlers.pointerdown({...makeCloseEvent(),button:2});
  assert(!removed,'Right-click must leave the card open');
  captureHandlers.pointerdown({...makeCloseEvent(),isPrimary:false});
  assert(!removed,'Secondary touch must leave the card open');
  for(const reset of ['pointercancel','blur']) {
    captureHandlers.pointerdown(makeCloseEvent());
    captureHandlers[reset]();
    captureHandlers.click(unrelated);
    assert(true,'Cancelled close gesture must not swallow later keyboard clicks');
  }
  captureHandlers.pointerdown(makeCloseEvent());
  captureHandlers.pointerdown(unrelated);
  captureHandlers.click(unrelated);
  assert(true,'A new map gesture must clear close suppression when the previous click was never delivered');
  assert(dismissed.has('route-a') && closeApi.sticky() === '', 'Closing a selected route card must clear sticky opening and retain manual dismissal');
  assert(underlyingRemoved, 'Closing must also remove an overlapping popup of the same order');
  const orderHighlightStart = source.indexOf('window.gawelaHighlightMarker = function(orderId) {');
  const orderHighlightEnd = source.indexOf('window.gawelaHighlightRouteStop =',orderHighlightStart);
  let orderOpened = false;
  const orderWindow = {};
  new Function('window','dismissedOrderPopupIds','markerMap','map','scalePopupElement','enableOrderPopupInteraction','bringOrderPopupToFront',
    source.slice(orderHighlightStart,orderHighlightEnd))(orderWindow,dismissed,
      new Map([['route-a',{getLngLat:()=>[8,47],getPopup:()=>({addTo(){orderOpened=true;}})}]]),{easeTo(){}},()=>{},()=>{},()=>{});
  orderWindow.gawelaHighlightMarker('route-a');
  assert(!orderOpened, 'Host order highlight must respect manual dismissal as well as route highlight');
  const routeHighlightStart = source.indexOf('window.gawelaHighlightRouteStop = function(orderId) {');
  const routeHighlightEnd = source.indexOf('window.gawelaSetRouteInfo', routeHighlightStart);
  let opened = 0;
  const routeWindow = {};
  const routePin = {getLngLat:()=>[8,47],getPopup:()=>({addTo(){opened++;},remove(){}})};
  new Function('window','routeMarkerMap','dismissedOrderPopupIds','map','scalePopupElement','enableOrderPopupInteraction','bringOrderPopupToFront','applyRouteStopHighlight',
    'let highlightedRouteStopId="route-a"; let stickyPopupOrderId="";' + source.slice(routeHighlightStart,routeHighlightEnd))(
      routeWindow,new Map([['route-a',routePin],['route-b',routePin]]),dismissed,{easeTo(){}},()=>{},()=>{},()=>{},()=>{});
  routeWindow.gawelaHighlightRouteStop('route-a');
  assert(opened===0, 'Repeated host highlight must not reopen a manually closed selected route card');
  routeWindow.gawelaHighlightRouteStop('route-b');
  assert(opened===1, 'Selecting another route pin must still open its card');
  routeWindow.gawelaHighlightRouteStop('route-a');
  assert(opened===2 && !dismissed.has('route-a'), 'Selecting the dismissed route pin again must allow its card');
  let sdkAdds=0, sdkRemoves=0, sdkOpen=false;
  const sdkPopup={isOpen:()=>sdkOpen,addTo(){if(sdkOpen)sdkRemoves++;sdkOpen=true;sdkAdds++;},remove(){sdkOpen=false;sdkRemoves++;}};
  const stableWindow={};
  const stablePin={getLngLat:()=>[8,47],getPopup:()=>sdkPopup};
  new Function('window','routeMarkerMap','dismissedOrderPopupIds','map','scalePopupElement','enableOrderPopupInteraction','bringOrderPopupToFront','applyRouteStopHighlight',
    'let highlightedRouteStopId=""; let stickyPopupOrderId="";' + source.slice(routeHighlightStart,routeHighlightEnd))(
      stableWindow,new Map([['a',stablePin]]),new Set(),{easeTo(){}},()=>{},()=>{},()=>{},()=>{});
  stableWindow.gawelaHighlightRouteStop('a');
  stableWindow.gawelaHighlightRouteStop('a');
  stableWindow.gawelaHighlightRouteStop('a');
  assert(sdkAdds===1&&sdkRemoves===0,'Repeated route highlights must not re-add an already open SDK popup');
  removed = false;
  close({...event, button:2});
  assert(!removed, 'Right-click must not close the card');
  close({...event, button:0, detail:0});
  assert(removed, 'Keyboard activation must close the card');
  assert(!source.includes("popupEl.addEventListener('pointerup', closeCard"), 'Closing must not remove DOM before click finishes');
  const productsStart = source.indexOf("if (popupEl.dataset.gawelaProductsToggleBound !== 'true')");
  const productsEnd = source.indexOf("if (popupEl.dataset.gawelaBringToFrontBound === 'true')", productsStart);
  const listeners = {};
  const extraLines = [{hidden:true}, {hidden:true}];
  const toggle = {expanded:'false', textContent:'Mehr anzeigen',
    getAttribute(){return this.expanded;}, setAttribute(name,value){this.expanded=value;}};
  const productsElement = {dataset:{}, querySelectorAll:()=>extraLines,
    addEventListener(type,handler){listeners[type]=handler;}};
  new Function('popupEl','popup','window','bringOrderPopupToFront','scalePopupElement', source.slice(productsStart,productsEnd))(
    productsElement, {}, {requestAnimationFrame:fn=>fn()}, ()=>{}, ()=>{});
  const productClick = {target:{closest:()=>toggle},preventDefault(){},stopPropagation(){}};
  listeners.click(productClick);
  assert(extraLines.every(line=>!line.hidden) && toggle.textContent==='Weniger anzeigen', 'Click must reveal all additional products');
  listeners.click(productClick);
  assert(extraLines.every(line=>line.hidden) && toggle.textContent==='Mehr anzeigen', 'Next click must collapse products again');
  assert(!listeners.pointerup, 'Expansion must not move the button before the click event');
  const updateStart=source.indexOf("let lastHostRoutePayload = '';");
  const updateEnd=source.indexOf('window.gawelaSetRoute = function',updateStart);
  let routeRebuilds=0;
  const updateWindow={gawelaSetRoute(){routeRebuilds++;}};
  new Function('window',source.slice(updateStart,updateEnd))(updateWindow);
  const routeData=[{id:'a',products:['Produkt'],color:'#00ff00'}];
  const geometry=[{lat:47,lon:8},{lat:48,lon:9}];
  updateWindow.gawelaUpdateRoute(routeData,geometry,'blue',[],false);
  updateWindow.gawelaUpdateRoute(JSON.parse(JSON.stringify(routeData)),geometry,'blue',[],false);
  assert(routeRebuilds===1,'Identical host refresh after selection must preserve the popup DOM');
  updateWindow.gawelaUpdateRoute([{...routeData[0],products:['Neu']}],geometry,'blue',[],false);
  assert(routeRebuilds===2,'Changed card content must still rebuild the route');
  updateWindow.gawelaUpdateRoute(routeData,[{lat:49,lon:9}],'blue',[],false);
  assert(routeRebuilds===3,'Changed geometry must still update');
  updateWindow.gawelaUpdateRoute(routeData,[{lat:49,lon:9}],'blue',[],true);
  assert(routeRebuilds===4,'Explicit centering must not be suppressed');
  return 58;
}

if (typeof module !== 'undefined') {
  const fs = require('node:fs');
  const path = require('node:path');
  const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
  console.log(runMapPinPopupTests(source) + ' map popup checks passed');
}
