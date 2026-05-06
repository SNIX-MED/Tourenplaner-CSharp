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
                 <link rel="stylesheet" href="https://api.tomtom.com/maps-sdk-js/4.47.6/maps/maps.css" />
                 <style>
                   html, body, #map { height: 100%; margin: 0; padding: 0; }
                   body { overflow: hidden; font-family: Segoe UI, sans-serif; background: transparent; }
                   #map { background: #f8fafc; }
                   .status { position: absolute; left: 10px; top: 10px; z-index: 1000; background: rgba(255,255,255,.9); border: 1px solid #cbd5e1; border-radius: 8px; padding: 6px 8px; font-size: 12px; color: #334155; }
                 </style>
               </head>
               <body>
                 <div id="map"></div>
                 <div id="status" class="status">Karte wird initialisiert...</div>
                 <script src="https://api.tomtom.com/maps-sdk-js/4.47.6/maps/maps-web.min.js"></script>
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
                   } else if (!(window.tomtom && window.tomtom.L)) {
                     setStatus('TomTom SDK konnte nicht geladen werden.');
                     postDiag(false, 'TomTom SDK konnte nicht geladen werden.');
                   } else {
                     const L = window.tomtom.L;
                     let map = null;
                     let markerLayer = null;
                     let routeLayer = null;
                     let routeMarkerMap = new Map();
                     let markerMap = new Map();

                     try {
                       map = L.map('map', {
                         key: apiKey.trim(),
                         source: 'raster',
                         layer: 'basic',
                         style: style,
                         center: [8.5417, 47.3769],
                         zoom: 10,
                         language: 'de-DE'
                       });

                       markerLayer = L.layerGroup().addTo(map);
                       routeLayer = L.layerGroup().addTo(map);

                       if (showTraffic) {
                         const suffix = useTileCache ? '' : `&nocache=${Date.now()}`;
                         L.tileLayer(`https://api.tomtom.com/traffic/map/4/tile/flow/relative0/{z}/{x}/{y}.png?tileSize=256&style=main&key=${encodeURIComponent(apiKey)}${suffix}`, {
                           maxZoom: 22,
                           opacity: 0.65,
                           attribution: '&copy; TomTom Traffic'
                         }).addTo(map);
                       }

                       map.on('tileerror', () => {
                         setStatus('TomTom Tiles konnten nicht geladen werden.');
                         postDiag(false, 'TomTom Tiles konnten nicht geladen werden (Key/Netzwerk/Quota).');
                       });

                       setStatus('TomTom Karte aktiv');
                       postDiag(true, 'TomTom Karte aktiv');

                       window.gawelaSetMarkers = function(markers) {
                         markerLayer.clearLayers();
                         markerMap.clear();
                         if (!Array.isArray(markers) || markers.length === 0) return;
                         const bounds = [];
                         markers.forEach(m => {
                           if (!m || typeof m.lat !== 'number' || typeof m.lon !== 'number') return;
                           const marker = L.marker([m.lat, m.lon]).addTo(markerLayer);
                           marker.bindPopup(`<b>${m.name || m.id || 'Stopp'}</b><br/>${m.address || ''}`);
                           marker.on('click', () => {
                             if (window.chrome && window.chrome.webview && m.id) {
                               window.chrome.webview.postMessage(String(m.id));
                             }
                           });
                           markerMap.set(m.id, marker);
                           bounds.push([m.lat, m.lon]);
                         });
                         if (bounds.length > 0) map.fitBounds(bounds, { padding: [24, 24] });
                       };

                       window.gawelaSetCompanyMarker = function(company) {
                         if (!company || typeof company.lat !== 'number' || typeof company.lon !== 'number') return;
                         L.marker([company.lat, company.lon]).addTo(markerLayer).bindPopup(`<b>${company.name || 'Firma'}</b><br/>${company.address || ''}`);
                       };

                       window.gawelaSetRoute = function(routeStops, geometryPoints) {
                         routeLayer.clearLayers();
                         routeMarkerMap.clear();
                         if (!Array.isArray(routeStops) || routeStops.length === 0) return;

                         const path = (Array.isArray(geometryPoints) && geometryPoints.length > 1)
                           ? geometryPoints.filter(p => p && typeof p.lat === 'number' && typeof p.lon === 'number').map(p => [p.lat, p.lon])
                           : routeStops.filter(s => s && typeof s.lat === 'number' && typeof s.lon === 'number').map(s => [s.lat, s.lon]);

                         if (path.length > 1) {
                           L.polyline(path, { color: '#2563EB', weight: 5, opacity: 0.95 }).addTo(routeLayer);
                         }

                         routeStops.forEach(stop => {
                           if (!stop || typeof stop.lat !== 'number' || typeof stop.lon !== 'number') return;
                           const marker = L.marker([stop.lat, stop.lon], { draggable: true }).addTo(routeLayer);
                           marker.bindPopup(`Route stop ${stop.label || stop.position || '?'}<br/>Order: ${stop.id || ''}`);
                           marker.on('click', () => {
                             if (window.chrome && window.chrome.webview && stop.id) {
                               window.chrome.webview.postMessage(`routeSelect:${stop.id}`);
                             }
                           });
                           marker.on('dragend', () => {
                             const p = marker.getLatLng();
                             if (window.chrome && window.chrome.webview && stop.id) {
                               window.chrome.webview.postMessage(`move:${stop.id}:${p.lat.toFixed(6)}:${p.lng.toFixed(6)}`);
                             }
                           });
                           routeMarkerMap.set(stop.id, marker);
                         });
                       };

                       window.gawelaHighlightMarker = function(orderId) {
                         const marker = markerMap.get(orderId);
                         if (!marker) return;
                         map.panTo(marker.getLatLng());
                         marker.openPopup();
                       };

                       window.gawelaHighlightRouteStop = function(orderId) {
                         const marker = routeMarkerMap.get(orderId);
                         if (!marker) return;
                         map.panTo(marker.getLatLng());
                         marker.openPopup();
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
                       window.gawelaSetAllMarkerPopupsVisible = function() {};
                       window.gawelaSetPopupSizeMultiplier = function() {};
                       window.gawelaSetDetailsToggle = function() {};
                     } catch (err) {
                       const msg = err && err.message ? err.message : 'Unbekannter Initialisierungsfehler';
                       setStatus(`TomTom Initialisierung fehlgeschlagen: ${msg}`);
                       postDiag(false, `TomTom Initialisierung fehlgeschlagen: ${msg}`);
                     }
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
