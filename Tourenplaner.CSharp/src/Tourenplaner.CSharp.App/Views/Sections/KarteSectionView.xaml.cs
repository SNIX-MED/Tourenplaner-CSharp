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

                   function clearMarkers() {
                     markerLayer.clearLayers();
                     markerMap.clear();
                   }

                   function clearRoute() {
                     routeLayer.clearLayers();
                     routePolyline = null;
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
                   };

                   window.gawelaHighlightRouteStop = function(orderId) {
                     if (!orderId || !markerMap.has(orderId)) {
                       return;
                     }
                     const marker = markerMap.get(orderId);
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
