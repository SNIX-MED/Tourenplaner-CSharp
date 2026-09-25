// Run with: node Tourenplaner.CSharp/tests/map-marker-fanout.test.cjs
// Executes the production JavaScript embedded in the map document.
function runMarkerFanOutTests(source) {
  const start = source.indexOf('const normalizeOverlapAddressPart =');
  const end = source.indexOf('const scheduleMarkerFanOutRecompute =', start);
  if (start < 0 || end < 0) throw new Error('Map fan-out helpers not found');
  const create = new Function('map', 'window', 'markerScale', 'mapMarkers', 'markerMap', 'routeMarkerMap = new Map()',
    "let hoveredOverlapGroupKey = ''; let overlapHoverResetHandle = 0; let routeStopHitTargets = []; const scheduleMarkerFanOutRecompute = () => {}; " +
    source.slice(start, end) +
    '; return { spread: spreadOverlappingMarkers, recompute: recomputeVisibleMarkerFanOut, hover: setHoveredOverlapGroupKey, move: handleOverlapPointerMove, leave: scheduleOverlapHoverReset, group: () => hoveredOverlapGroupKey, hitTargets: () => routeStopHitTargets };');
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  let checks = 0;
  for (const zoom of [6, 10, 15]) {
    for (const scale of [0.35, 1, 2.4]) {
      const factor = 2 ** zoom;
      const map = {
        getZoom: () => zoom,
        project: ([lon, lat]) => ({ x: lon * factor, y: lat * factor }),
        unproject: ([x, y]) => ({ lng: x / factor, lat: y / factor })
      };
      const a = { id: 'a', lat: 47.4, lon: 8.4 };
      const b = { ...a, id: 'b' };
      const state = { __gawelaLastMarkers: [b], __gawelaLastRoutePayload: { routeStops: [] } };
      let position;
      const pin = { setLngLat: p => { position = p; } };
      const api = create(map, state, scale, [pin], new Map([['b', pin]]));
      const distance = p => Math.hypot((p.displayLon - a.lon) * factor, (p.displayLat - a.lat) * factor);
      assert(distance(api.spread([b], '')[0]) === 0, 'Single free order must stay at address');
      const pair = api.spread([a, b], '');
      assert(pair[0].displayLon !== pair[1].displayLon, 'Free orders must fan out');
      state.__gawelaLastRoutePayload.routeStops = [a];
      const remaining = api.spread([b], '')[0];
      assert(distance(remaining) >= 32 * Math.max(1, scale) - 1e-7, 'Route pin must not cover remaining order');
      assert(remaining.id === 'b' && remaining.lat === b.lat && remaining.lon === b.lon, 'Preserve order identity and coordinates');
      const hovered = api.spread([b], remaining.__overlapGroupKey)[0];
      assert(distance(hovered) >= 32 - 1e-7, 'Hover must preserve separation');
      api.recompute();
      assert(position && position[1] === remaining.displayLat, 'Existing marker must move when route changes');
      const many = api.spread([b, { ...b, id: 'c' }, { ...b, id: 'd' }], '');
      assert(new Set(many.map(p => p.displayLat + '|' + p.displayLon)).size === 3, 'All remaining orders must have separate positions');
      state.__gawelaLastRoutePayload.routeStops = [{ ...a, lon: 9 }];
      assert(distance(api.spread([b], '')[0]) === 0, 'Unrelated route must not displace order');
      state.__gawelaLastRoutePayload.routeStops = [];
      api.recompute();
      assert(position[0] === b.lon && position[1] === b.lat, 'Clearing route must restore location');
      checks += 9;

      const routePins = new Map([a, b].map(stop => [stop.id, {
        position: { lng: stop.lon, lat: stop.lat },
        setLngLat(p) { this.position = { lng: p[0], lat: p[1] }; },
        getLngLat() { return this.position; }
      }]));
      state.__gawelaLastMarkers = [];
      state.__gawelaLastRoutePayload.routeStops = [a, b];
      const routeApi = create(map, state, scale, [], new Map(), routePins);
      routeApi.recompute();
      const compactGap = Math.abs(routePins.get('a').position.lng - routePins.get('b').position.lng);
      assert(compactGap > 0, 'Tour-only view must fan out coincident stops');
      const groupKey = routeApi.spread([a, b], '', false)[0].__overlapGroupKey;
      routeApi.hover(groupKey);
      routeApi.recompute();
      const expandedGap = Math.abs(routePins.get('a').position.lng - routePins.get('b').position.lng);
      assert(expandedGap > compactGap, 'Hover must expand tour stops');
      assert(routeApi.hitTargets().every(hit => hit.lon === routePins.get(hit.id).position.lng), 'Hit targets must follow displayed route pins');
      const mixed = routeApi.spread([{ ...a, id: 'free' }], groupKey)[0];
      assert(distance(mixed) > distance(routeApi.spread([a, b], groupKey, false)[0]) + 20 * scale, 'Free order must stay outside expanded tour pins');
      routeApi.hover('');
      routeApi.recompute();
      assert(Math.abs(routePins.get('a').position.lng - routePins.get('b').position.lng) === compactGap, 'Tour stops must collapse after hover');
      const draggingPin = routePins.get('a');
      draggingPin.__gawelaDragging = true;
      draggingPin.setLngLat([9, 48]);
      routeApi.recompute();
      assert(draggingPin.position.lng === 9 && draggingPin.position.lat === 48, 'Fan-out must not move a pin being dragged');
      assert(a.lon === 8.4 && b.lon === 8.4, 'Fan-out must preserve real tour coordinates');
      checks += 7;

      let nextTimer = 1;
      const timers = new Map();
      state.setTimeout = callback => { const id = nextTimer++; timers.set(id, callback); return id; };
      state.clearTimeout = id => timers.delete(id);
      const overGroup = { target: { closest: () => ({ dataset: { overlapGroupKey: groupKey } }) } };
      const overMap = { target: { closest: () => null } };
      routeApi.hover(groupKey);
      routeApi.move(overGroup);
      assert(timers.size === 0, 'Hover within group must keep pins expanded');
      routeApi.move(overMap);
      const timerId = [...timers.keys()][0];
      routeApi.move(overMap);
      assert(timers.size === 1 && timers.has(timerId), 'Movement outside pins must not postpone collapse');
      timers.get(timerId)();
      timers.clear();
      assert(routeApi.group() === '', 'Moving away within map must clear hover');
      routeApi.hover(groupKey);
      routeApi.leave();
      routeApi.move(overGroup);
      assert(timers.size === 0 && routeApi.group() === groupKey, 'Moving between group pins must cancel collapse');
      routeApi.move(overMap);
      [...timers.values()][0]();
      timers.clear();
      assert(routeApi.group() === '', 'Leaving group again must collapse');
      checks += 5;
    }
  }
  return checks;
}
if (typeof module !== 'undefined') {
  const fs = require('node:fs');
  const path = require('node:path');
  const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
  console.log(runMarkerFanOutTests(source) + ' map fan-out checks passed');
}
