// Run with node Tourenplaner.CSharp/tests/map-fleet-marker.test.cjs
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
assert(source.includes('.tt-popup .gawela-fleet-maps-link, .mapboxgl-popup .gawela-fleet-maps-link { pointer-events: auto !important;'),
  'Fleet Maps link must override the popup pointer-events block for both SDK popup classes');
const start = source.indexOf('window.gawelaSetFleetVehicles = function(vehicles)');
const end = source.indexOf('window.gawelaSetCompanyMarker =', start);
assert(start >= 0 && end > start, 'Production fleet marker function must exist');
let popup, created = 0, listener, messages = [], bindings = 0;
const link = { dataset: {}, addEventListener(type, handler) { assert.equal(type, 'click'); bindings++; listener = handler; } };
class Popup {
  constructor() { popup = this; this.opened = false; }
  setHTML(html) { this.html = html; return this; }
  getElement() { return this.opened ? { querySelector() { return link; } } : undefined; }
  on(type, handler) { assert.equal(type, 'open'); this.onOpen = handler; return this; }
}
class Marker {
  setLngLat(point) { assert.deepEqual(point, [8.2, 47.3]); return this; }
  setPopup(value) { assert.equal(value, popup); return this; }
  addTo() { created++; return this; }
}
const window = { chrome: { webview: { postMessage(message) { messages.push(message); } } } };
const markers = [];
new Function('window', 'document', 'ttSdk', 'clearMarkers', 'fleetMarkers', 'map', 'applyMarkerStackOrder', source.slice(start, end))(
  window, { createElement() { return { style: {} }; } }, { Popup, Marker }, list => list.splice(0), markers, {}, () => {});
window.gawelaSetFleetVehicles([{ lat: 47.3, lon: 8.2, name: 'Truck' }]);
assert.equal(created, 1, 'Vehicle marker must be created before popup opens');
assert.equal(markers.length, 1);
assert.equal(listener, undefined);
popup.opened = true;
popup.onOpen();
listener({ preventDefault() {}, stopPropagation() {} });
assert.deepEqual(messages, ['openFleetMaps:47.3:8.2']);
popup.onOpen();
assert.equal(link.dataset.mapsBound, 'true');
assert.equal(bindings, 1, 'Reopening must not duplicate click handlers');
window.gawelaSetFleetVehicles([
  { lat: NaN, lon: 8 }, { lat: 47, lon: Infinity },
  { lat: 91, lon: 8 }, { lat: 47, lon: -181 },
  { lat: null, lon: 8 }, { lat: '47', lon: 8 }, null,
  { lat: 47.3, lon: 8.2, name: '<Truck & Driver>' }
]);
assert.equal(created, 2, 'Bad coordinates must not prevent the following valid marker');
assert.equal(markers.length, 1, 'Refresh must replace previous markers');
assert(popup.html.includes('&lt;Truck &amp; Driver&gt;'));
assert(popup.html.includes('query=47.3,8.2'));
window.gawelaSetFleetVehicles([]);
assert.equal(markers.length, 0);
console.log('Fleet marker creation and Google Maps link checks passed');
