namespace Tourenplaner.CSharp.App.Views.Sections;

internal static class MapHtmlDocumentBuilder
{
    public static string Build(
        string? tomTomApiKey,
        string? tomTomStyle,
        bool tomTomShowTrafficFlow,
        bool tomTomEnableTileCache,
        string? mapOverlayStyle,
        bool mapOverlayShowTrafficIncidents,
        bool mapOverlayShowRoadLabels,
        bool mapOverlayShowPoi)
    {
        var template = """
               <!doctype html>
               <html>
               <head>
                 <meta charset="utf-8" />
                 <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                 <style>
                   html, body, #map { height: 100%; margin: 0; padding: 0; }
                   body { overflow: hidden; font-family: Segoe UI, sans-serif; background: transparent; }
                   #map { background: #f8fafc; }
                   .status { position: absolute; left: 10px; top: 10px; z-index: 1000; background: rgba(255,255,255,.9); border: 1px solid #cbd5e1; border-radius: 8px; padding: 6px 8px; font-size: 12px; color: #334155; }
                   .gawela-pin-wrap { position: relative; width: 28px; height: 28px; transform-origin: center bottom; }
                   .gawela-pin { width: 20px; height: 20px; position: absolute; left: 4px; top: 4px; border: 2px solid #ffffff; box-shadow: 0 1px 4px rgba(0,0,0,.35); }
                   .gawela-pin-circle { border-radius: 50%; }
                   .gawela-pin-square { border-radius: 4px; }
                   .gawela-pin-triangle { width: 0; height: 0; left: 2px; top: 2px; border-left: 12px solid transparent; border-right: 12px solid transparent; border-bottom: 22px solid #2563EB; border-top: 0; background: transparent; border-radius: 0; box-shadow: none; }
                   .gawela-pin-triangle-outline { position: absolute; width: 0; height: 0; left: 0; top: 0; border-left: 14px solid transparent; border-right: 14px solid transparent; border-bottom: 26px solid #ffffff; }
                   .gawela-pin-badges { position: absolute; right: -2px; top: -2px; display: flex; }
                   .gawela-pin-badge { width: 9px; height: 9px; border-radius: 50%; border: 2px solid #ffffff; box-shadow: 0 0 0 1px rgba(30,41,59,0.55); }
                   .gawela-pin-badge-aviso-none { background: #64748b; color: #ffffff; }
                   .gawela-pin-badge-aviso-partial { background: #f59e0b; color: #ffffff; }
                   .gawela-pin-badge-aviso-full { background: #16a34a; color: #ffffff; }
                   .gawela-pin-badge-assigned { background: #64748b; color: #ffffff; }
                   .gawela-pin-dimmed { opacity: .35; }
                   .gawela-pin-selected { outline: 2px solid #111827; outline-offset: 2px; }
                   .gawela-company-marker { width: 22px; height: 22px; border-radius: 50%; background: #0f766e; border: 2px solid #ffffff; box-shadow: 0 1px 4px rgba(0,0,0,.28); display: flex; align-items: center; justify-content: center; }
                   .gawela-company-marker svg { width: 12px; height: 12px; fill: #ffffff; display: block; }
                   .map-options-toggle { position: absolute; right: 12px; top: 12px; z-index: 1100; border: 1px solid #cbd5e1; background: rgba(255,255,255,.96); border-radius: 10px; padding: 8px 10px; font-size: 12px; color: #0f172a; cursor: pointer; font-weight: 600; box-shadow: 0 4px 14px rgba(15,23,42,.18); }
                   .map-options-overlay { position: absolute; right: 0; top: 0; height: 100%; width: min(340px, 84vw); z-index: 1200; background: rgba(255,255,255,.98); border-left: 1px solid #dbe3ee; transform: translateX(100%); transition: transform .22s ease; box-shadow: -10px 0 28px rgba(15,23,42,.16); display: flex; flex-direction: column; }
                   .map-options-overlay.open { transform: translateX(0); }
                   .map-options-header { display: flex; align-items: center; justify-content: space-between; padding: 16px 16px 10px; border-bottom: 1px solid #e2e8f0; }
                   .map-options-title { font-size: 25px; font-weight: 700; color: #0f172a; margin: 0; }
                   .map-options-close { border: 0; background: transparent; font-size: 24px; line-height: 1; cursor: pointer; color: #475569; padding: 2px; }
                   .map-options-content { padding: 12px 16px 16px; overflow: auto; }
                   .map-option-section { margin-top: 14px; }
                   .map-option-section h4 { margin: 0 0 10px; font-size: 12px; letter-spacing: .04em; text-transform: uppercase; color: #64748b; }
                   .style-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
                   .style-btn { border: 1px solid #d6deea; border-radius: 8px; background: #fff; padding: 10px 12px; cursor: pointer; text-align: left; min-height: 46px; }
                   .style-btn.active { border-color: #2563eb; box-shadow: 0 0 0 2px rgba(37,99,235,.14); }
                   .style-label { font-size: 16px; font-weight: 500; color: #0f172a; display: block; }
                   .switch-row { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; font-size: 14px; color: #1e293b; }
                   .switch-row input { width: 20px; height: 20px; }
                 </style>
               </head>
               <body>
                 <div id="map"></div>
                 <div id="status" class="status">Karte wird initialisiert...</div>
                 <button id="mapOptionsToggle" class="map-options-toggle" type="button">Map options</button>
                 <aside id="mapOptionsOverlay" class="map-options-overlay" aria-hidden="true">
                   <div class="map-options-header">
                     <h3 class="map-options-title">Map options</h3>
                     <button id="mapOptionsClose" class="map-options-close" type="button" aria-label="Close">&times;</button>
                   </div>
                   <div class="map-options-content">
                     <div class="map-option-section">
                       <h4>Map styles</h4>
                       <div class="style-grid">
                         <button type="button" class="style-btn" data-style="standard"><span class="style-label">Standard</span></button>
                         <button type="button" class="style-btn" data-style="light"><span class="style-label">Light</span></button>
                         <button type="button" class="style-btn" data-style="dark"><span class="style-label">Dark</span></button>
                         <button type="button" class="style-btn" data-style="satellite"><span class="style-label">Satellite</span></button>
                       </div>
                     </div>
                     <div class="map-option-section">
                       <h4>Traffic</h4>
                       <label class="switch-row"><input type="checkbox" id="toggleTrafficIncidents" /> Traffic incidents</label>
                       <label class="switch-row"><input type="checkbox" id="toggleTrafficFlow" /> Traffic flow</label>
                     </div>
                     <div class="map-option-section">
                       <h4>Labels</h4>
                       <label class="switch-row"><input type="checkbox" id="toggleRoadLabels" checked /> Strassen / Ortsnamen</label>
                       <label class="switch-row"><input type="checkbox" id="togglePoi" checked /> POIs</label>
                     </div>
                   </div>
                 </aside>
                 <script>
                   const apiKey = '__TT_KEY__';
                   const initialTomTomStyle = '__TT_STYLE__';
                   const initialOverlayStyle = '__TT_OVERLAY_STYLE__';
                   const showTraffic = __TT_TRAFFIC__;
                   const showTrafficIncidents = __TT_TRAFFIC_INCIDENTS__;
                   const showRoadLabels = __TT_ROAD_LABELS__;
                   const showPoi = __TT_POI__;
                   const useTileCache = __TT_TILE_CACHE__;

                   const statusEl = document.getElementById('status');
                   const setStatus = (t) => { if (statusEl) statusEl.textContent = t; };
                   const postDiag = (ok, reason) => {
                     if (window.chrome && window.chrome.webview) {
                       window.chrome.webview.postMessage(`mapdiag:${ok ? 'ok' : 'error'}:${reason || ''}`);
                     }
                   };

                   window.gawelaSetMarkers = function() {};
                   window.gawelaSetRoute = function() {};
                   window.gawelaSetCompanyMarker = function() {};
                   window.gawelaSetPlannedTourOverlays = function() {};
                   window.gawelaHighlightPlannedTourOverlay = function() {};
                   window.gawelaHighlightMarker = function() {};
                   window.gawelaHighlightRouteStop = function() {};
                   window.gawelaSetRouteInfo = function() {};
                   window.gawelaSetAllMarkerPopupsVisible = function() {};
                   window.gawelaSetPopupSizeMultiplier = function() {};
                   window.gawelaSetDetailsToggle = function() {};
                   window.gawelaAddToRoute = function() {};

                   if (!apiKey || !apiKey.trim()) {
                     setStatus('TomTom API-Key fehlt.');
                     postDiag(false, 'TomTom API-Key fehlt.');
                   } else {
                     const loadCss = (href) => new Promise((resolve, reject) => {
                       const l = document.createElement('link');
                       l.rel = 'stylesheet';
                       l.href = href;
                       l.onload = () => resolve();
                       l.onerror = () => reject(new Error(`CSS konnte nicht geladen werden: ${href}`));
                       document.head.appendChild(l);
                     });

                     const loadScript = (src) => new Promise((resolve, reject) => {
                       const s = document.createElement('script');
                       s.src = src;
                       s.async = false;
                       s.onload = () => resolve();
                       s.onerror = () => reject(new Error(`Script konnte nicht geladen werden: ${src}`));
                       document.head.appendChild(s);
                     });

                     const tomTomCssCandidates = [
                       'https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.64.0/maps/maps.css',
                       'https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.51.0/maps/maps.css'
                     ];

                     const tomTomJsCandidates = [
                       'https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.64.0/maps/maps-web.min.js',
                       'https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.51.0/maps/maps-web.min.js'
                     ];

                     const tryLoadFromCandidates = async (candidates, loader) => {
                       let lastError = null;
                       for (const candidate of candidates) {
                         try {
                           await loader(candidate);
                           return candidate;
                         } catch (err) {
                           lastError = err;
                         }
                       }

                       throw lastError || new Error('Keine Quelle erfolgreich geladen.');
                     };

                     (async function init() {
                       try {
                         const loadedCss = await tryLoadFromCandidates(tomTomCssCandidates, loadCss);
                         const loadedJs = await tryLoadFromCandidates(tomTomJsCandidates, loadScript);

                         if (!(window.tt && typeof window.tt.map === 'function')) {
                           throw new Error('TomTom Maps SDK konnte nicht geladen werden.');
                         }

                         const ttSdk = window.tt;
                         const preferredTileSize = (window.devicePixelRatio || 1) > 1 ? 512 : 256;
                         const mapState = {
                           style: ['standard', 'light', 'dark', 'satellite'].includes((initialOverlayStyle || '').toLowerCase())
                             ? (initialOverlayStyle || '').toLowerCase()
                             : ((initialTomTomStyle || '').toLowerCase() === 'night' ? 'dark' : 'standard'),
                           trafficFlow: !!showTraffic,
                           trafficIncidents: !!showTrafficIncidents,
                           showRoadLabels: !!showRoadLabels,
                           showPoi: !!showPoi
                         };
                         const cacheSuffix = useTileCache ? '' : `&nocache=${Date.now()}`;

                         const applyStyleThumbPreviews = () => {};

                        const resolveVectorStyleUri = (styleKey) => {
                          const normalized = (styleKey || '').toLowerCase();
                          if (normalized === 'dark') return 'tomtom://vector/1/basic-night';
                          if (normalized === 'light') return 'tomtom://vector/1/basic-light';
                          return 'tomtom://vector/1/basic-main';
                        };

                        const resolveSatelliteStyleUrl = () => {
                          return `https://api.tomtom.com/style/1/style/*?map=2/basic_street-satellite&poi=2/poi_satellite&key=${encodeURIComponent(apiKey)}${cacheSuffix}`;
                        };

                        const resolveInitialStyleUri = () => {
                          const normalized = (mapState.style || '').toLowerCase();
                          if (normalized === 'satellite') {
                            return resolveSatelliteStyleUrl();
                          }

                          return resolveVectorStyleUri(normalized);
                        };

                         const map = ttSdk.map({
                           key: apiKey,
                           container: 'map',
                           style: resolveInitialStyleUri(),
                           center: [8.5417, 47.3769],
                           zoom: 10
                         });

                         let markerMap = new Map();
                         let routeMarkerMap = new Map();
                         let mapMarkers = [];
                         let companyMarkers = [];
                         let routeMarkers = [];
                         let routePopupVisible = false;
                         let markerScale = 1.0;

                         applyStyleThumbPreviews();

                         const clearMarkers = (arr) => {
                           arr.forEach(x => x.remove());
                           arr.length = 0;
                         };

                         const normalizeShape = (shape) => {
                           const s = (shape || '').toString().toLowerCase();
                           if (s === 'triangle' || s === 'square' || s === 'circle') return s;
                           return 'circle';
                         };

                         const avisoBadgeClass = (avisoStatus) => {
                           const s = (avisoStatus || '').toString().trim().toLowerCase();
                           if (s.includes('voll') || s.includes('komplett')) return 'gawela-pin-badge-aviso-full';
                           if (s.includes('teil')) return 'gawela-pin-badge-aviso-partial';
                           return 'gawela-pin-badge-aviso-none';
                         };

                         const buildMarkerElement = (m, isRouteMarker) => {
                           const wrap = document.createElement('div');
                           wrap.className = 'gawela-pin-wrap';
                           wrap.style.transform = `scale(${markerScale})`;
                           if (m && m.isDimmed) wrap.classList.add('gawela-pin-dimmed');
                           if (m && m.isBatchSelected) wrap.classList.add('gawela-pin-selected');

                           const shape = normalizeShape(m ? m.shape : null);
                           const color = (m && m.color) ? m.color : '#2563EB';

                           if (shape === 'triangle') {
                             const triangleOutline = document.createElement('div');
                             triangleOutline.className = 'gawela-pin-triangle-outline';
                             wrap.appendChild(triangleOutline);
                           }

                           const pin = document.createElement('div');
                           pin.className = `gawela-pin gawela-pin-${shape}`;
                           if (shape === 'triangle') {
                             pin.style.borderBottomColor = color;
                           } else {
                             pin.style.background = color;
                           }
                           wrap.appendChild(pin);

                           if (!isRouteMarker) {
                             const badges = document.createElement('div');
                             badges.className = 'gawela-pin-badges';

                             if (m && m.isAssigned) {
                               const aviso = document.createElement('div');
                               aviso.className = `gawela-pin-badge ${avisoBadgeClass(m.avisoStatus)}`;
                               badges.appendChild(aviso);
                             }

                             if (badges.children.length > 0) {
                               wrap.appendChild(badges);
                             }
                           }

                           return wrap;
                         };

                         const buildCompanyMarkerElement = () => {
                           const el = document.createElement('div');
                           el.className = 'gawela-company-marker';
                           el.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3.2L3 10.6h2v9.2h5.6v-5.4h2.8v5.4H19v-9.2h2L12 3.2z"/></svg>';
                           return el;
                         };

                         const applyBaseStyle = () => {
                           const normalized = (mapState.style || '').toLowerCase();
                           if (normalized === 'satellite') {
                            map.setStyle(resolveSatelliteStyleUrl());
                             return;
                           }

                           const styleUri = resolveVectorStyleUri(normalized);
                           map.setStyle(styleUri);
                         };

                         const ensureRouteLayer = (coordinates, colorHex) => {
                           const sourceId = 'gawela-route-source';
                           const layerId = 'gawela-route-layer';

                           if (!Array.isArray(coordinates) || coordinates.length < 2) {
                             if (map.getLayer(layerId)) map.removeLayer(layerId);
                             if (map.getSource(sourceId)) map.removeSource(sourceId);
                             return;
                           }

                           const data = {
                             type: 'Feature',
                             geometry: {
                               type: 'LineString',
                               coordinates
                             }
                           };

                           const existingSource = map.getSource(sourceId);
                           if (existingSource && typeof existingSource.setData === 'function') {
                             existingSource.setData(data);
                           } else {
                             if (existingSource) {
                               try { map.removeSource(sourceId); } catch (_) {}
                             }
                             map.addSource(sourceId, { type: 'geojson', data });
                           }

                           if (!map.getLayer(layerId)) {
                             map.addLayer({
                               id: layerId,
                               type: 'line',
                               source: sourceId,
                               layout: {
                                 'line-cap': 'round',
                                 'line-join': 'round'
                               },
                               paint: {
                                 'line-color': colorHex || '#2563EB',
                                 'line-width': 6,
                                 'line-opacity': 0.98
                               }
                             });
                           } else {
                             map.setPaintProperty(layerId, 'line-color', colorHex || '#2563EB');
                           }
                         };

                         const routeTrafficSourcePrefix = 'gawela-route-traffic-source-';
                         const routeTrafficLayerPrefix = 'gawela-route-traffic-layer-';

                         const clearTrafficRouteLayers = () => {
                           const style = map.getStyle();
                           if (!style || !Array.isArray(style.layers)) {
                             return;
                           }

                           const layerIds = style.layers
                             .map(x => x && x.id ? x.id : '')
                             .filter(id => id.startsWith(routeTrafficLayerPrefix));
                           layerIds.forEach(id => {
                             if (map.getLayer(id)) {
                               map.removeLayer(id);
                             }
                           });

                           const sourceIds = Object.keys(style.sources || {})
                             .filter(id => id.startsWith(routeTrafficSourcePrefix));
                           sourceIds.forEach(id => {
                             if (map.getSource(id)) {
                               map.removeSource(id);
                             }
                           });
                         };

                         const trafficColorForLevel = (trafficLevel) => {
                           const level = (trafficLevel || '').toString().trim().toLowerCase();
                           if (!level) return '#f59e0b';

                           if (level === '0') return '#22c55e';
                           if (level === '1') return '#f59e0b';
                           if (level === '2') return '#ef4444';
                           if (level === '3') return '#b91c1c';

                           if (level === 'freeflow' || level === 'free' || level === 'light' || level === 'low' || level === 'leicht') {
                             return '#22c55e';
                           }
                           if (level === 'moderate' || level === 'medium' || level === 'mittel') {
                             return '#f59e0b';
                           }
                           if (level === 'heavy' || level === 'high' || level === 'stark' || level === 'congested') {
                             return '#ef4444';
                           }
                           if (level === 'blocked' || level === 'severe' || level === 'jam' || level === 'jammed' || level === 'roadclosure' || level === 'stationary' || level === 'heavilycongested' || level === 'stopandgo' || level === 'stau') {
                             return '#b91c1c';
                           }

                           const numeric = Number(level);
                           if (Number.isFinite(numeric)) {
                             if (numeric <= 0) return '#22c55e';
                             if (numeric <= 1) return '#f59e0b';
                             if (numeric <= 2) return '#ef4444';
                             return '#b91c1c';
                           }

                           return '#f59e0b';
                         };

                         const renderTrafficRouteSegments = (path, trafficSegments) => {
                           clearTrafficRouteLayers();
                           if (!Array.isArray(path) || path.length < 2 || !Array.isArray(trafficSegments) || trafficSegments.length === 0) {
                             return;
                           }

                           let segmentIndex = 0;
                           trafficSegments.forEach(segment => {
                             const startIndex = Number(segment && segment.startIndex);
                             const endIndex = Number(segment && segment.endIndex);
                             if (!Number.isFinite(startIndex) || !Number.isFinite(endIndex)) {
                               return;
                             }

                             const from = Math.max(0, Math.min(path.length - 1, Math.floor(startIndex)));
                             const to = Math.max(0, Math.min(path.length - 1, Math.floor(endIndex)));
                             if (to <= from) {
                               return;
                             }

                             const coords = path.slice(from, to + 1);
                             if (coords.length < 2) {
                               return;
                             }

                             const sourceId = `${routeTrafficSourcePrefix}${segmentIndex}`;
                             const layerId = `${routeTrafficLayerPrefix}${segmentIndex}`;
                             segmentIndex++;

                             map.addSource(sourceId, {
                               type: 'geojson',
                               data: {
                                 type: 'Feature',
                                 geometry: {
                                   type: 'LineString',
                                   coordinates: coords
                                 }
                               }
                             });

                             map.addLayer({
                               id: layerId,
                               type: 'line',
                               source: sourceId,
                               paint: {
                                 'line-color': trafficColorForLevel(segment.trafficLevel),
                                 'line-width': 6,
                                 'line-opacity': 0.95
                               }
                             });
                           });
                         };

                         const setLayerVisible = (layerId, visible) => {
                           if (!map.getLayer(layerId)) {
                             return;
                           }
                           map.setLayoutProperty(layerId, 'visibility', visible ? 'visible' : 'none');
                         };

                         const ensureTrafficLayers = () => {
                           if (!map.getSource('gawela-traffic-flow-source')) {
                             map.addSource('gawela-traffic-flow-source', {
                               type: 'raster',
                               tiles: [`https://api.tomtom.com/traffic/map/4/tile/flow/relative0/{z}/{x}/{y}.png?tileSize=256&style=main&key=${encodeURIComponent(apiKey)}${cacheSuffix}`],
                               tileSize: preferredTileSize
                             });
                           }
                           if (!map.getLayer('gawela-traffic-flow-layer')) {
                             map.addLayer({
                               id: 'gawela-traffic-flow-layer',
                               type: 'raster',
                               source: 'gawela-traffic-flow-source',
                               paint: { 'raster-opacity': 0.65 }
                             });
                           }

                           if (!map.getSource('gawela-traffic-incidents-source')) {
                             map.addSource('gawela-traffic-incidents-source', {
                               type: 'raster',
                               tiles: [`https://api.tomtom.com/traffic/map/4/tile/incidents/s1/{z}/{x}/{y}.png?tileSize=${preferredTileSize}&key=${encodeURIComponent(apiKey)}${cacheSuffix}`],
                               tileSize: preferredTileSize
                             });
                           }
                           if (!map.getLayer('gawela-traffic-incidents-layer')) {
                             map.addLayer({
                               id: 'gawela-traffic-incidents-layer',
                               type: 'raster',
                               source: 'gawela-traffic-incidents-source',
                               paint: { 'raster-opacity': 0.95 }
                             });
                           }
                         };

                         const applyTrafficLayers = () => {
                           ensureTrafficLayers();
                           setLayerVisible('gawela-traffic-flow-layer', mapState.trafficFlow);
                           setLayerVisible('gawela-traffic-incidents-layer', mapState.trafficIncidents);
                           ensureRouteLayersOnTop();
                         };

                         const ensureRouteLayersOnTop = () => {
                           try {
                             const routeLayerId = 'gawela-route-layer';
                             if (map.getLayer(routeLayerId)) {
                               map.moveLayer(routeLayerId);
                             }

                             const style = map.getStyle();
                             if (!style || !Array.isArray(style.layers)) {
                               return;
                             }

                             const routeTrafficLayerIds = style.layers
                               .map(x => x && x.id ? x.id : '')
                               .filter(id => id.startsWith(routeTrafficLayerPrefix));

                             routeTrafficLayerIds.forEach(id => {
                               if (map.getLayer(id)) {
                                 map.moveLayer(id);
                               }
                             });
                           } catch (_) {
                             // ignore ordering errors if style is in transition
                           }
                         };

                         const applyPoiVisibility = () => {
                           const style = map.getStyle();
                           if (!style || !Array.isArray(style.layers)) {
                             return;
                           }

                           const isPoiLayer = (layerId) => {
                             const id = (layerId || '').toLowerCase();
                             return id.includes('poi') ||
                               id.includes('parking') ||
                               id.includes('fuel') ||
                               id.includes('restaurant') ||
                               id.includes('hotel') ||
                               id.includes('shop');
                           };

                           style.layers.forEach(layer => {
                             if (!layer || !layer.id) return;
                             if (layer.id.startsWith('gawela-')) return;
                             if (layer.type !== 'symbol') return;

                             const visible = isPoiLayer(layer.id)
                               ? mapState.showPoi
                               : mapState.showRoadLabels;

                             try {
                               map.setLayoutProperty(layer.id, 'visibility', visible ? 'visible' : 'none');
                             } catch (_) {
                               // ignore style-specific layers that cannot be toggled
                             }
                           });
                         };

                         const updateStyleButtonSelection = () => {
                           document.querySelectorAll('.style-btn').forEach(el => {
                             const match = el.getAttribute('data-style') === mapState.style;
                             el.classList.toggle('active', !!match);
                           });
                         };

                         const initOptionsOverlay = () => {
                           const overlay = document.getElementById('mapOptionsOverlay');
                           const toggleBtn = document.getElementById('mapOptionsToggle');
                           const closeBtn = document.getElementById('mapOptionsClose');
                           const trafficFlowToggle = document.getElementById('toggleTrafficFlow');
                           const trafficIncidentsToggle = document.getElementById('toggleTrafficIncidents');
                           const roadLabelsToggle = document.getElementById('toggleRoadLabels');
                           const poiToggle = document.getElementById('togglePoi');

                           if (!overlay || !toggleBtn || !closeBtn || !trafficFlowToggle || !trafficIncidentsToggle || !roadLabelsToggle || !poiToggle) {
                             return;
                           }

                           const postMapOptions = () => {
                             if (!(window.chrome && window.chrome.webview)) {
                               return;
                             }

                             const trafficFlowToken = mapState.trafficFlow ? '1' : '0';
                             const trafficIncidentsToken = mapState.trafficIncidents ? '1' : '0';
                             const showRoadLabelsToken = mapState.showRoadLabels ? '1' : '0';
                             const showPoiToken = mapState.showPoi ? '1' : '0';
                             window.chrome.webview.postMessage(`mapopts:${mapState.style}|${trafficFlowToken}|${trafficIncidentsToken}|${showRoadLabelsToken}|${showPoiToken}`);
                           };

                           const openOverlay = () => {
                             overlay.classList.add('open');
                             overlay.setAttribute('aria-hidden', 'false');
                           };

                           const closeOverlay = () => {
                             overlay.classList.remove('open');
                             overlay.setAttribute('aria-hidden', 'true');
                           };

                           toggleBtn.addEventListener('click', openOverlay);
                           closeBtn.addEventListener('click', closeOverlay);

                           trafficFlowToggle.checked = mapState.trafficFlow;
                           trafficIncidentsToggle.checked = mapState.trafficIncidents;
                           roadLabelsToggle.checked = mapState.showRoadLabels;
                           poiToggle.checked = mapState.showPoi;

                           trafficFlowToggle.addEventListener('change', () => {
                             mapState.trafficFlow = !!trafficFlowToggle.checked;
                             applyTrafficLayers();
                             postMapOptions();
                           });

                           trafficIncidentsToggle.addEventListener('change', () => {
                             mapState.trafficIncidents = !!trafficIncidentsToggle.checked;
                             applyTrafficLayers();
                             postMapOptions();
                           });

                           roadLabelsToggle.addEventListener('change', () => {
                             mapState.showRoadLabels = !!roadLabelsToggle.checked;
                             applyPoiVisibility();
                             applyTrafficLayers();
                             postMapOptions();
                           });

                           poiToggle.addEventListener('change', () => {
                             mapState.showPoi = !!poiToggle.checked;
                             applyPoiVisibility();
                             applyTrafficLayers();
                             postMapOptions();
                           });

                           document.querySelectorAll('.style-btn').forEach(btn => {
                             btn.addEventListener('click', () => {
                               const requested = btn.getAttribute('data-style');
                               if (!requested || requested === mapState.style) return;
                               mapState.style = requested;
                               updateStyleButtonSelection();
                               applyBaseStyle();
                               applyTrafficLayers();
                               postMapOptions();
                             });
                           });

                           updateStyleButtonSelection();
                         };

                         map.once('load', () => {
                           applyPoiVisibility();
                           applyTrafficLayers();
                           initOptionsOverlay();
                           setStatus('TomTom Karte aktiv');
                           postDiag(true, `Karte aktiv (TomTom SDK: ${loadedJs}, CSS: ${loadedCss})`);
                         });

                        map.on('styledata', () => {
                          applyPoiVisibility();
                          applyTrafficLayers();
                          if (window.__gawelaLastRoutePayload) {
                            const last = window.__gawelaLastRoutePayload;
                             window.gawelaSetRoute(last.routeStops, last.geometryPoints, last.routeColor, last.trafficSegments);
                           }
                         });

                         map.on('error', (e) => {
                           const reason = e && e.error && e.error.message ? e.error.message : 'Unbekannter Kartenfehler';
                           setStatus(`Kartenfehler: ${reason}`);
                           postDiag(false, `Kartenfehler: ${reason}`);
                         });

                         window.gawelaSetMarkers = function(markers) {
                           window.__gawelaLastMarkers = Array.isArray(markers) ? markers : [];
                           clearMarkers(mapMarkers);
                           clearMarkers(companyMarkers);
                           markerMap = new Map();

                           if (!Array.isArray(markers) || markers.length === 0) {
                             return;
                           }

                           const bounds = new ttSdk.LngLatBounds();
                           let hasBounds = false;

                           markers.forEach(m => {
                             if (!m || typeof m.lat !== 'number' || typeof m.lon !== 'number') return;

                             const popup = new ttSdk.Popup({ offset: 24 }).setHTML(`<b>${m.customer || m.id || 'Stopp'}</b><br/>${m.street || ''}`);
                             const marker = new ttSdk.Marker({ element: buildMarkerElement(m, false), anchor: 'center' })
                               .setLngLat([m.lon, m.lat])
                               .setPopup(popup)
                               .addTo(map);

                             marker.getElement().addEventListener('click', () => {
                               if (window.chrome && window.chrome.webview && m.id) {
                                 window.chrome.webview.postMessage(String(m.id));
                               }
                             });

                             markerMap.set(m.id, marker);
                             mapMarkers.push(marker);
                             bounds.extend([m.lon, m.lat]);
                             hasBounds = true;
                           });

                           if (hasBounds) {
                             map.fitBounds(bounds, { padding: 24, maxZoom: 14 });
                           }
                         };

                         window.gawelaSetCompanyMarker = function(company) {
                           if (!company || typeof company.lat !== 'number' || typeof company.lon !== 'number') return;
                             const popup = new ttSdk.Popup({ offset: 24 }).setHTML(`<b>${company.name || 'Firma'}</b><br/>${company.address || ''}`);
                           const marker = new ttSdk.Marker({ element: buildCompanyMarkerElement(), anchor: 'center' })
                             .setLngLat([company.lon, company.lat])
                             .setPopup(popup)
                             .addTo(map);
                           companyMarkers.push(marker);
                         };

                         window.gawelaSetRoute = function(routeStops, geometryPoints, routeColor, trafficSegments) {
                           window.__gawelaLastRoutePayload = {
                             routeStops: Array.isArray(routeStops) ? routeStops : [],
                             geometryPoints: Array.isArray(geometryPoints) ? geometryPoints : [],
                             routeColor: routeColor || '#2563EB',
                             trafficSegments: Array.isArray(trafficSegments) ? trafficSegments : []
                           };
                           clearMarkers(routeMarkers);
                           routeMarkerMap = new Map();

                           if (!Array.isArray(routeStops) || routeStops.length === 0) {
                             ensureRouteLayer([], routeColor);
                             return;
                           }

                           const toPoint = (p) => {
                             if (!p) return null;
                             const lat = Number(p.lat);
                             const lon = Number(p.lon);
                             if (!Number.isFinite(lat) || !Number.isFinite(lon)) return null;
                             return [lon, lat];
                           };

                           const path = (Array.isArray(geometryPoints) && geometryPoints.length > 1)
                             ? geometryPoints.map(toPoint).filter(p => Array.isArray(p))
                             : routeStops.map(toPoint).filter(p => Array.isArray(p));

                           ensureRouteLayer(path, routeColor || '#2563EB');
                           renderTrafficRouteSegments(path, trafficSegments);
                           ensureRouteLayersOnTop();

                           routeStops.forEach(stop => {
                             if (!stop || typeof stop.lat !== 'number' || typeof stop.lon !== 'number') return;

                             const popup = new ttSdk.Popup({ offset: 24 }).setHTML(`Route stop ${stop.label || stop.position || '?'}<br/>Order: ${stop.id || ''}`);
                             const marker = new ttSdk.Marker({ element: buildMarkerElement(stop, true), draggable: true, anchor: 'center' })
                               .setLngLat([stop.lon, stop.lat])
                               .setPopup(popup)
                               .addTo(map);

                             marker.getElement().addEventListener('click', () => {
                               if (window.chrome && window.chrome.webview && stop.id) {
                                 window.chrome.webview.postMessage(`routeSelect:${stop.id}`);
                               }
                             });

                             marker.on('dragend', () => {
                               const p = marker.getLngLat();
                               if (window.chrome && window.chrome.webview && stop.id) {
                                 window.chrome.webview.postMessage(`move:${stop.id}:${p.lat.toFixed(6)}:${p.lng.toFixed(6)}`);
                               }
                             });

                             routeMarkerMap.set(stop.id, marker);
                             routeMarkers.push(marker);
                           });
                         };

                         window.gawelaHighlightMarker = function(orderId) {
                           const marker = markerMap.get(orderId);
                           if (!marker) return;
                           map.easeTo({ center: marker.getLngLat(), duration: 350 });
                           const popup = marker.getPopup();
                           if (popup) popup.addTo(map);
                         };

                         window.gawelaHighlightRouteStop = function(orderId) {
                           const marker = routeMarkerMap.get(orderId);
                           if (!marker) return;
                           map.easeTo({ center: marker.getLngLat(), duration: 350 });
                           const popup = marker.getPopup();
                           if (popup) popup.addTo(map);
                         };

                         window.gawelaSetRouteInfo = function(text) {
                           const t = (text || '').toString().trim();
                           setStatus(t.length > 0 ? t : 'TomTom Karte aktiv');
                         };

                         window.gawelaAddToRoute = function(orderId) {
                           if (window.chrome && window.chrome.webview) {
                             window.chrome.webview.postMessage(`add:${orderId}`);
                           }
                         };

                         window.gawelaSetPlannedTourOverlays = function() {};
                         window.gawelaHighlightPlannedTourOverlay = function() {};
                         window.gawelaSetAllMarkerPopupsVisible = function(visible) {
                           routePopupVisible = !!visible;
                           mapMarkers.forEach(m => {
                             const p = m.getPopup();
                             if (!p) return;
                             if (routePopupVisible) p.addTo(map);
                             else p.remove();
                           });
                         };
                         window.gawelaSetPopupSizeMultiplier = function(multiplier) {
                           const parsed = Number(multiplier);
                           markerScale = Number.isFinite(parsed) ? Math.max(0.6, Math.min(2.4, parsed)) : 1.0;
                           mapMarkers.forEach(m => {
                             const el = m.getElement();
                             if (el) {
                               el.style.transform = `scale(${markerScale})`;
                             }
                           });
                           routeMarkers.forEach(m => {
                             const el = m.getElement();
                             if (el) {
                               el.style.transform = `scale(${markerScale})`;
                             }
                           });
                         };
                         window.gawelaSetDetailsToggle = function() {};
                       } catch (err) {
                         const msg = err && err.message ? err.message : 'Unbekannter Initialisierungsfehler';
                         setStatus(`Karteninitialisierung fehlgeschlagen: ${msg}`);
                         postDiag(false, `Karteninitialisierung fehlgeschlagen: ${msg}`);
                       }
                     })();
                   }
                 </script>
               </body>
               </html>
               """;

        return template
            .Replace("__TT_KEY__", EscapeJsString((tomTomApiKey ?? string.Empty).Trim()))
            .Replace("__TT_STYLE__", EscapeJsString(string.IsNullOrWhiteSpace(tomTomStyle) ? "main" : tomTomStyle.Trim()))
            .Replace("__TT_TRAFFIC__", tomTomShowTrafficFlow ? "true" : "false")
            .Replace("__TT_OVERLAY_STYLE__", EscapeJsString(string.IsNullOrWhiteSpace(mapOverlayStyle) ? "standard" : mapOverlayStyle.Trim()))
            .Replace("__TT_TRAFFIC_INCIDENTS__", mapOverlayShowTrafficIncidents ? "true" : "false")
            .Replace("__TT_ROAD_LABELS__", mapOverlayShowRoadLabels ? "true" : "false")
            .Replace("__TT_POI__", mapOverlayShowPoi ? "true" : "false")
            .Replace("__TT_TILE_CACHE__", tomTomEnableTileCache ? "true" : "false");
    }

    private static string EscapeJsString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }
}
