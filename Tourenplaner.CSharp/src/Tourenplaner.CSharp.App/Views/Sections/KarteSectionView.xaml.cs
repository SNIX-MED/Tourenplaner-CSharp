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

        if (DataContext is KarteSectionViewModel vm)
        {
            _vmNotifier = vm;
            _vmNotifier.PropertyChanged += OnViewModelPropertyChanged;
            _ordersCollection = vm.MapOrders;
            _ordersCollection.CollectionChanged += OnOrdersCollectionChanged;
        }

        _ = PushMarkersToMapAsync();
    }

    private async void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        await PushMarkersToMapAsync();
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KarteSectionViewModel.SelectedOrder) && !_suppressSelectionSync)
        {
            await HighlightSelectedMarkerAsync();
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
            lat = x.Latitude,
            lon = x.Longitude
        }).ToList();

        var json = JsonSerializer.Serialize(markers);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"window.gawelaSetMarkers({json});");
        await HighlightSelectedMarkerAsync();
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

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        var orderId = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        var match = vm.MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
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

                   function clearMarkers() {
                     markerLayer.clearLayers();
                     markerMap.clear();
                   }

                   window.gawelaSetMarkers = function(markers) {
                     clearMarkers();
                     if (!markers || markers.length === 0) {
                       return;
                     }
                     const bounds = [];
                     markers.forEach(m => {
                       const marker = L.marker([m.lat, m.lon]);
                       marker.bindPopup(`<b>${m.customer || m.id}</b><br/>${m.address || ''}`);
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
                 </script>
               </body>
               </html>
               """;
    }
}
