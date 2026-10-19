const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
const layersCode = source.slice(source.indexOf('const blockedTrafficLayers ='), source.indexOf('const ensureRouteLayersOnTop ='));
const errorCode = source.slice(source.indexOf('const describeMapError ='), source.indexOf('window.gawelaSetMarkers = function(markers)'));
const statusCode = source.slice(source.indexOf('const statusEl ='), source.indexOf('const postDiag ='));
let checks = 0;
for (const size of [256, 512]) {
  const sources = new Map(), layers = new Map(), messages = [], logs = [], timers = [];
  const statusEl = { textContent: '', visible: false, classList: { toggle: (_, visible) => { statusEl.visible = visible; } } };
  let handler;
  let api;
  const styleData = () => { if (api) api.applyTrafficLayers(); };
  const map = {
    getSource: id => sources.get(id), getLayer: id => layers.get(id),
    addSource: (id, data) => { assert.ok(!sources.has(id)); sources.set(id, data); styleData(); },
    addLayer: data => { assert.ok(!layers.has(data.id)); assert.ok(sources.has(data.source)); layers.set(data.id, data); styleData(); },
    removeLayer: id => { layers.delete(id); styleData(); },
    removeSource: id => { assert.ok(![...layers.values()].some(l => l.source === id)); sources.delete(id); styleData(); },
    setLayoutProperty: () => {}, on: (_, cb) => { handler = cb; }
  };
  const state = { trafficFlow: false, trafficIncidents: false };
  const create = new Function('map', 'mapState', 'preferredTileSize', 'apiKey', 'document', 'postDiag', 'window', 'console', 'setTimeout',
    'const ensureRouteLayersOnTop = () => {}; const setLayerVisible = () => {}; ' + statusCode + layersCode + errorCode +
    '; return { applyTrafficLayers, retry: retryTrafficLayer, setStatus };');
  api = create(map, state, size, 'test-key', { getElementById: () => statusEl }, (...args) => messages.push(args),
    { chrome: { webview: { postMessage: msg => logs.push(msg) } } }, { error: () => {} }, cb => timers.push(cb));
  api.applyTrafficLayers(); assert.equal(sources.size, 0); checks++;
  state.trafficFlow = true; api.applyTrafficLayers();
  assert.equal(sources.size, 1);
  assert.ok(sources.get('gawela-traffic-flow-source').tiles[0].includes(`tileSize=${size}`));
  assert.ok(!sources.get('gawela-traffic-flow-source').tiles[0].includes('nocache')); checks++;
  state.trafficIncidents = true; api.applyTrafficLayers(); assert.equal(sources.size, 2); checks++;
  state.trafficFlow = false; api.applyTrafficLayers(); assert.equal(sources.size, 1); checks++;
  for (const status of [401, 403, 429]) {
    handler({ sourceId: 'gawela-traffic-incidents-source', error: { status } });
    while (timers.length) timers.shift()();
    api.applyTrafficLayers(); assert.equal(sources.size, 0);
    assert.equal(messages.length, 0, 'Optional overlay must not mark entire map unavailable');
    assert.match(statusEl.textContent, /pausiert/); checks++;
    api.retry('incidents'); api.applyTrafficLayers(); assert.equal(sources.size, 1); checks++;
  }
  handler({ error: { status: 403, url: 'https://api.tomtom.com/traffic/map/4/tile/incidents/s1/1/1/1.png' } });
  while (timers.length) timers.shift()();
  api.applyTrafficLayers(); assert.equal(sources.size, 0); checks++;
  api.setStatus('TomTom Karte aktiv');
  assert.match(statusEl.textContent, /pausiert/, 'Initial load must not hide an earlier traffic error'); checks++;
  state.trafficFlow = true; api.applyTrafficLayers();
  handler({ sourceId: 'gawela-traffic-flow-source', error: { status: 403 } });
  api.retry('incidents');
  assert.match(statusEl.textContent, /Verkehrsfluss pausiert/, 'Retry must retain errors of other overlays'); checks++;
  api.retry('flow'); api.applyTrafficLayers();
  while (timers.length) timers.shift()();
  assert.equal(sources.size, 2, 'Queued removal must not remove a source re-enabled in the meantime'); checks++;
  const repeated = { sourceId: 'gawela-traffic-flow-source', error: { status: 429 } };
  const beforeLogs = logs.length;
  handler(repeated); handler(repeated); handler(repeated);
  assert.equal(logs.length, beforeLogs + 1, 'Identical tile errors must not flood the log'); checks++;
  handler({ sourceId: 'gawela-traffic-incidents-source', error: { status: 500, message: 'Temporary tile failure' } });
  assert.equal(messages.length, 0, 'A temporary overlay failure must not mark the whole map unavailable'); checks++;
  handler({ error: { message: 'Base map failed' } }); assert.equal(messages.length, 1); checks++;
  api.retry('flow');
  assert.match(statusEl.textContent, /Base map failed/, 'Overlay toggles must not clear unrelated map failures'); checks++;
}
console.log(`Traffic request budget: ${checks} checks passed`);
