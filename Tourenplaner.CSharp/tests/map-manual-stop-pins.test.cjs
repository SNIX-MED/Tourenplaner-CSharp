const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
const start = source.indexOf('const normalizeOverlapAddressPart =');
const end = source.indexOf('const scheduleMarkerFanOutRecompute =', start);
assert(start >= 0 && end > start);
const create = new Function('map', 'window', 'markerScale',
  "let hoveredOverlapGroupKey = ''; let overlapHoverResetHandle = 0; " +
  source.slice(start, end) + '; return resolveVisibleMarkersForLayer;');
let checks = 0;
for (const zoom of [6, 10, 15]) {
  const factor = 2 ** zoom;
  const map = {
    getZoom: () => zoom,
    project: ([lon, lat]) => ({ x: lon * factor, y: lat * factor }),
    unproject: ([x, y]) => ({ lng: x / factor, lat: y / factor })
  };
  for (const scale of [0.35, 1, 2.4]) {
    const manual = { id: 'manual:disposal', lat: 47.59, lon: 7.65, street: '', postalCodeCity: '', color: '#374151', label: 'H' };
    const order = { id: 'order', lat: 37, lon: 17, street: 'Hauptstrasse 1', postalCodeCity: 'Basel' };
    const state = { __gawelaLastMarkers: [], __gawelaLastRoutePayload: { routeStops: [order, manual] } };
    const resolve = create(map, state, scale);
    const pins = resolve('route');
    assert.equal(pins.length, 2);
    const pin = pins.find(p => p.id === manual.id);
    assert.equal(pin.displayLat, manual.lat);
    assert.equal(pin.displayLon, manual.lon);
    assert.equal(pin.color, '#374151');
    assert.equal(pin.label, 'H');
    state.__gawelaLastRoutePayload.routeStops.push({ ...manual, id: 'manual:second' });
    const coincident = resolve('route').filter(p => p.id.startsWith('manual:'));
    assert.equal(coincident.length, 2);
    assert.notEqual(coincident[0].displayLon, coincident[1].displayLon);
    assert(coincident.every(p => p.lat === manual.lat && p.lon === manual.lon));
    checks += 8;
  }
}
console.log(`${checks} manual stop pin checks passed`);
