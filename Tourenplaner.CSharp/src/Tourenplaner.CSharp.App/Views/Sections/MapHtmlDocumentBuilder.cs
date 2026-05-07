namespace Tourenplaner.CSharp.App.Views.Sections;

internal static class MapHtmlDocumentBuilder
{
    public static string Build(string? tomTomApiKey, string? tomTomStyle, bool tomTomShowTrafficFlow, bool tomTomEnableTileCache)
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
                 </style>
               </head>
               <body>
                 <div id="map"></div>
                 <div id="status" class="status">Karte wird initialisiert...</div>
                 <script>
                   const apiKey = '__TT_KEY__';
                   const style = '__TT_STYLE__' === 'night' ? 'night' : 'main';
                   const showTraffic = __TT_TRAFFIC__;
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

                     const mapLibreCssCandidates = [
                       'https://cdn.jsdelivr.net/npm/maplibre-gl@4.7.1/dist/maplibre-gl.css',
                       'https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.css'
                     ];

                     const mapLibreJsCandidates = [
                       'https://cdn.jsdelivr.net/npm/maplibre-gl@4.7.1/dist/maplibre-gl.js',
                       'https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.js'
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
                         const loadedCss = await tryLoadFromCandidates(mapLibreCssCandidates, loadCss);
                         const loadedJs = await tryLoadFromCandidates(mapLibreJsCandidates, loadScript);

                         if (!(window.maplibregl && typeof window.maplibregl.Map === 'function')) {
                           throw new Error('MapLibre wurde geladen, aber maplibregl ist nicht verfuegbar.');
                         }

                         const maplibregl = window.maplibregl;
                         const cacheSuffix = useTileCache ? '' : `&nocache=${Date.now()}`;

                         const map = new maplibregl.Map({
                           container: 'map',
                           style: {
                             version: 8,
                             sources: {
                               tomtomBase: {
                                 type: 'raster',
                                 tiles: [`https://api.tomtom.com/map/1/tile/basic/${style}/{z}/{x}/{y}.png?tileSize=256&key=${encodeURIComponent(apiKey)}${cacheSuffix}`],
                                 tileSize: 256,
                                 attribution: '&copy; TomTom'
                               }
                             },
                             layers: [
                               { id: 'tomtomBaseLayer', type: 'raster', source: 'tomtomBase' }
                             ]
                           },
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

                         const ensureRouteLayer = (coordinates, colorHex) => {
                           const sourceId = 'gawela-route-source';
                           const layerId = 'gawela-route-layer';

                           if (map.getLayer(layerId)) map.removeLayer(layerId);
                           if (map.getSource(sourceId)) map.removeSource(sourceId);

                           if (!Array.isArray(coordinates) || coordinates.length < 2) {
                             return;
                           }

                           map.addSource(sourceId, {
                             type: 'geojson',
                             data: {
                               type: 'Feature',
                               geometry: {
                                 type: 'LineString',
                                 coordinates
                               }
                             }
                           });

                           map.addLayer({
                             id: layerId,
                             type: 'line',
                             source: sourceId,
                             paint: {
                               'line-color': colorHex || '#2563EB',
                               'line-width': 5,
                               'line-opacity': 0.95
                             }
                           });
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

                         const ensureTrafficLayer = () => {
                           if (!showTraffic) {
                             return;
                           }

                           const sourceId = 'gawela-traffic-source';
                           const layerId = 'gawela-traffic-layer';

                           if (!map.getSource(sourceId)) {
                             map.addSource(sourceId, {
                               type: 'raster',
                               tiles: [`https://api.tomtom.com/traffic/map/4/tile/flow/relative0/{z}/{x}/{y}.png?tileSize=256&style=main&key=${encodeURIComponent(apiKey)}${cacheSuffix}`],
                               tileSize: 256,
                               attribution: '&copy; TomTom Traffic'
                             });
                           }

                           if (!map.getLayer(layerId)) {
                             map.addLayer({
                               id: layerId,
                               type: 'raster',
                               source: sourceId,
                               paint: { 'raster-opacity': 0.65 }
                             });
                           }
                         };

                         map.on('load', () => {
                           ensureTrafficLayer();
                           setStatus('TomTom Karte aktiv');
                           postDiag(true, `Karte aktiv (MapLibre: ${loadedJs}, CSS: ${loadedCss})`);
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

                           const bounds = new maplibregl.LngLatBounds();
                           let hasBounds = false;

                           markers.forEach(m => {
                             if (!m || typeof m.lat !== 'number' || typeof m.lon !== 'number') return;

                             const popup = new maplibregl.Popup({ offset: 24 }).setHTML(`<b>${m.customer || m.id || 'Stopp'}</b><br/>${m.street || ''}`);
                             const marker = new maplibregl.Marker({ element: buildMarkerElement(m, false), anchor: 'center' })
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
                             const popup = new maplibregl.Popup({ offset: 24 }).setHTML(`<b>${company.name || 'Firma'}</b><br/>${company.address || ''}`);
                           const marker = new maplibregl.Marker({ color: '#0F766E' })
                             .setLngLat([company.lon, company.lat])
                             .setPopup(popup)
                             .addTo(map);
                           companyMarkers.push(marker);
                         };

                         window.gawelaSetRoute = function(routeStops, geometryPoints, routeColor, trafficSegments) {
                           clearMarkers(routeMarkers);
                           routeMarkerMap = new Map();

                           if (!Array.isArray(routeStops) || routeStops.length === 0) {
                             ensureRouteLayer([], routeColor);
                             return;
                           }

                           const path = (Array.isArray(geometryPoints) && geometryPoints.length > 1)
                             ? geometryPoints.filter(p => p && typeof p.lat === 'number' && typeof p.lon === 'number').map(p => [p.lon, p.lat])
                             : routeStops.filter(s => s && typeof s.lat === 'number' && typeof s.lon === 'number').map(s => [s.lon, s.lat]);

                           ensureRouteLayer(path, routeColor || '#2563EB');
                           renderTrafficRouteSegments(path, trafficSegments);

                           routeStops.forEach(stop => {
                             if (!stop || typeof stop.lat !== 'number' || typeof stop.lon !== 'number') return;

                             const popup = new maplibregl.Popup({ offset: 24 }).setHTML(`Route stop ${stop.label || stop.position || '?'}<br/>Order: ${stop.id || ''}`);
                             const marker = new maplibregl.Marker({ element: buildMarkerElement(stop, true), draggable: true, anchor: 'center' })
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
            .Replace("__TT_TILE_CACHE__", tomTomEnableTileCache ? "true" : "false");
    }

    private static string EscapeJsString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }
}
