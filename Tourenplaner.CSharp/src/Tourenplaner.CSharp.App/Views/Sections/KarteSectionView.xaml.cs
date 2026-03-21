using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class KarteSectionView : UserControl
{
    private bool _mapReady;
    private INotifyPropertyChanged? _vmNotifier;
    private INotifyCollectionChanged? _ordersCollection;
    private INotifyCollectionChanged? _routeCollection;
    private bool _suppressSelectionSync;

    public KarteSectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureMapInitializedAsync();
        await PushMarkersToMapAsync();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vmNotifier is not null)
        {
            _vmNotifier.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_ordersCollection is not null)
        {
            _ordersCollection.CollectionChanged -= OnOrdersCollectionChanged;
        }
        if (_routeCollection is not null)
        {
            _routeCollection.CollectionChanged -= OnRouteCollectionChanged;
        }

        if (DataContext is KarteSectionViewModel vm)
        {
            _vmNotifier = vm;
            _vmNotifier.PropertyChanged += OnViewModelPropertyChanged;
            _ordersCollection = vm.MapOrders;
            _ordersCollection.CollectionChanged += OnOrdersCollectionChanged;
            _routeCollection = vm.RouteStops;
            _routeCollection.CollectionChanged += OnRouteCollectionChanged;
        }

        _ = PushMarkersToMapAsync();
        _ = PushRouteToMapAsync();
    }

    private async void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        await PushMarkersToMapAsync();
    }

    private async void OnRouteCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        await PushMarkersToMapAsync();
        await PushRouteToMapAsync();
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KarteSectionViewModel.SelectedOrder) && !_suppressSelectionSync)
        {
            await HighlightSelectedMarkerAsync();
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.SelectedRouteStop))
        {
            await HighlightSelectedRouteStopAsync();
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.RouteStops))
        {
            await PushRouteToMapAsync();
        }
    }

    private async Task EnsureMapInitializedAsync()
    {
        if (_mapReady)
        {
            return;
        }

        try
        {
            await MapWebView.EnsureCoreWebView2Async();
            MapWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            MapWebView.NavigateToString(BuildMapHtml());
            _mapReady = true;
            MapWebView.Visibility = Visibility.Visible;
            MapFallbackNotice.Visibility = Visibility.Collapsed;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MapWebView.Visibility = Visibility.Collapsed;
            MapFallbackNotice.Visibility = Visibility.Visible;
        }
        catch
        {
            MapWebView.Visibility = Visibility.Collapsed;
            MapFallbackNotice.Visibility = Visibility.Visible;
        }
    }

    private async Task PushMarkersToMapAsync()
    {
        if (!_mapReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var markers = vm.MapOrders.Select(x => new
        {
            id = x.OrderId,
            customer = x.Customer,
            address = x.Address,
            isAssigned = x.IsAssigned,
            lat = x.Latitude,
            lon = x.Longitude
        }).ToList();

        var json = JsonSerializer.Serialize(markers);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"window.gawelaSetMarkers({json});");
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"window.gawelaSetRoute({JsonSerializer.Serialize(vm.GetRouteSnapshot().Select(r => new { id = r.OrderId, lat = r.Latitude, lon = r.Longitude }))});");
        await HighlightSelectedMarkerAsync();
    }

    private async Task PushRouteToMapAsync()
    {
        if (!_mapReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var route = vm.GetRouteSnapshot()
            .Select(r => new
            {
                id = r.OrderId,
                position = r.Position,
                lat = r.Latitude,
                lon = r.Longitude
            }).ToList();

        var json = JsonSerializer.Serialize(route);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"window.gawelaSetRoute({json});");
        await HighlightSelectedRouteStopAsync();
    }

    private async Task HighlightSelectedMarkerAsync()
    {
        if (!_mapReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var id = vm.SelectedOrder?.OrderId ?? string.Empty;
        var jsonId = JsonSerializer.Serialize(id);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"window.gawelaHighlightMarker({jsonId});");
    }

    private async Task HighlightSelectedRouteStopAsync()
    {
        if (!_mapReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var id = vm.SelectedRouteStop?.OrderId ?? string.Empty;
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"window.gawelaHighlightRouteStop({JsonSerializer.Serialize(id)});");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        var raw = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (raw.StartsWith("add:", StringComparison.OrdinalIgnoreCase))
        {
            var addId = raw["add:".Length..];
            vm.AddOrderToRouteById(addId);
            return;
        }

        if (raw.StartsWith("swap:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = raw.Split(':');
            if (parts.Length == 3)
            {
                vm.SwapRouteStops(parts[1], parts[2]);
            }
            return;
        }

        if (raw.StartsWith("move:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = raw.Split(':');
            if (parts.Length == 4 &&
                double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
            {
                vm.UpdateRouteStopCoordinates(parts[1], lat, lon);
            }
            return;
        }

        if (raw.StartsWith("routeSelect:", StringComparison.OrdinalIgnoreCase))
        {
            var id = raw["routeSelect:".Length..];
            vm.SelectRouteStopByOrderId(id);
            return;
        }

        var match = vm.MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, raw, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        try
        {
            _suppressSelectionSync = true;
            vm.SelectedOrder = match;
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private static string BuildMapHtml()
    {
        return """
               <!doctype html>
               <html>
               <head>
                 <meta charset="utf-8" />
                 <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                 <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
                 <style>
                   html, body, #map { height: 100%; margin: 0; padding: 0; }
                   body { font-family: Segoe UI, sans-serif; }
                 </style>
               </head>
               <body>
                 <div id="map"></div>
                 <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
                 <script>
                   const map = L.map('map').setView([47.3769, 8.5417], 10);
                   L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                     maxZoom: 19,
                     attribution: '&copy; OpenStreetMap'
                   }).addTo(map);

                   let markerMap = new Map();
                   let markerLayer = L.layerGroup().addTo(map);
                   let routeLayer = L.layerGroup().addTo(map);
                   let routePolyline = null;
                   let routeMarkerMap = new Map();

                   function clearMarkers() {
                     markerLayer.clearLayers();
                     markerMap.clear();
                   }

                   function clearRoute() {
                     routeLayer.clearLayers();
                     routePolyline = null;
                     routeMarkerMap.clear();
                   }

                   window.gawelaSetMarkers = function(markers) {
                     clearMarkers();
                     if (!markers || markers.length === 0) {
                       return;
                     }
                     const bounds = [];
                     markers.forEach(m => {
                       const color = m.isAssigned ? '#64748b' : '#0ea5e9';
                       const icon = L.divIcon({
                         className: 'gawela-marker',
                         html: `<div style="width:14px;height:14px;border-radius:7px;background:${color};border:2px solid #fff;box-shadow:0 0 0 1px #334155;"></div>`,
                         iconSize: [14, 14],
                         iconAnchor: [7, 7]
                       });
                       const marker = L.marker([m.lat, m.lon], { icon });
                       marker.bindPopup(
                         `<b>${m.customer || m.id}</b><br/>${m.address || ''}<br/><button onclick="window.gawelaAddToRoute('${m.id}')">Add to route</button>`
                       );
                       marker.on('click', () => {
                         if (window.chrome && window.chrome.webview) {
                           window.chrome.webview.postMessage(m.id);
                         }
                       });
                       marker.addTo(markerLayer);
                       markerMap.set(m.id, marker);
                       bounds.push([m.lat, m.lon]);
                     });
                     if (bounds.length > 0) {
                       map.fitBounds(bounds, { padding: [24, 24] });
                     }
                   };

                   window.gawelaHighlightMarker = function(orderId) {
                     if (!orderId || !markerMap.has(orderId)) {
                       return;
                     }
                     const marker = markerMap.get(orderId);
                     marker.openPopup();
                     map.panTo(marker.getLatLng());
                   };

                   window.gawelaSetRoute = function(routeStops) {
                     clearRoute();
                     if (!routeStops || routeStops.length === 0) {
                       return;
                     }
                     const points = routeStops.map(x => [x.lat, x.lon]);
                     routePolyline = L.polyline(points, { color: '#f97316', weight: 4, opacity: 0.8 }).addTo(routeLayer);

                     routeStops.forEach(stop => {
                       const icon = L.divIcon({
                         className: 'gawela-route-stop',
                         html: `<div style="width:20px;height:20px;border-radius:10px;background:#f97316;color:#fff;font-size:11px;line-height:20px;text-align:center;border:1px solid #fff;">${stop.position}</div>`,
                         iconSize: [20, 20],
                         iconAnchor: [10, 10]
                       });

                       const routeMarker = L.marker([stop.lat, stop.lon], { icon, draggable: true })
                         .addTo(routeLayer)
                         .bindPopup(`Route stop ${stop.position}<br/>Order: ${stop.id}`);

                       routeMarker.on('click', () => {
                         if (window.chrome && window.chrome.webview) {
                           window.chrome.webview.postMessage(`routeSelect:${stop.id}`);
                         }
                       });

                       routeMarker.on('dragend', () => {
                         const currentLatLng = routeMarker.getLatLng();
                         let nearest = null;
                         let nearestDistance = Number.MAX_VALUE;
                         routeMarkerMap.forEach((candidate, id) => {
                           if (id === stop.id) return;
                           const d = currentLatLng.distanceTo(candidate.getLatLng());
                           if (d < nearestDistance) {
                             nearestDistance = d;
                             nearest = id;
                           }
                         });

                         if (nearest && nearestDistance < 300) {
                           if (window.chrome && window.chrome.webview) {
                             window.chrome.webview.postMessage(`swap:${stop.id}:${nearest}`);
                           }
                         } else {
                           if (window.chrome && window.chrome.webview) {
                             window.chrome.webview.postMessage(`move:${stop.id}:${currentLatLng.lat.toFixed(6)}:${currentLatLng.lng.toFixed(6)}`);
                           }
                         }
                       });

                       routeMarkerMap.set(stop.id, routeMarker);
                     });
                   };

                   window.gawelaHighlightRouteStop = function(orderId) {
                     if (!orderId || !routeMarkerMap.has(orderId)) {
                       return;
                     }
                     const marker = routeMarkerMap.get(orderId);
                     map.panTo(marker.getLatLng());
                     marker.openPopup();
                   };

                   window.gawelaAddToRoute = function(orderId) {
                     if (window.chrome && window.chrome.webview) {
                       window.chrome.webview.postMessage(`add:${orderId}`);
                     }
                   };
                 </script>
               </body>
               </html>
               """;
    }
}
