using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class PinAddressInfoDialogWindow : Window
{
    private readonly string _originalAddress;
    private readonly string _tomTomApiKey;
    private readonly AddressGeocodingResult? _result;
    private readonly string _failureSummary;
    private GeoPoint? _selectedLocation;
    private bool _mapReady;
    private bool _satelliteView;

    public bool UseFoundAddress { get; private set; }
    public GeoPoint? SelectedLocation => _selectedLocation;

    public PinAddressInfoDialogWindow(Order order, string originalAddress,
        AddressGeocodingResult? result, string failureSummary, string tomTomApiKey)
    {
        InitializeComponent();
        _originalAddress = originalAddress;
        _tomTomApiKey = (tomTomApiKey ?? string.Empty).Trim();
        _result = result;
        _failureSummary = failureSummary;
        _selectedLocation = result?.Location;

        OrderText.Text = $"Auftrag {order.Id} · {order.CustomerName}";
        OriginalText.Text = $"Auftragsadresse: {originalAddress}";
        FoundText.Text = result is null
            ? failureSummary
            : $"Nächster TomTom-Treffer: {result.ResultFreeformAddress ?? originalAddress} ({result.MatchType})";
        MovePinButton.IsEnabled = result is not null;
        SaveButton.IsEnabled = result is not null;
        UpdatePositionText();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_result is null)
        {
            ShowMapMessage(_failureSummary);
            return;
        }
        if (string.IsNullOrWhiteSpace(_tomTomApiKey))
        {
            ShowMapMessage("TomTom-API-Key fehlt. Die Kartenvorschau kann nicht geladen werden.");
            return;
        }

        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync("PinPreview");
            await MapWebView.EnsureCoreWebView2Async(environment);
            MapWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            MapWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            MapWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            MapWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            MapWebView.NavigateToString(BuildMapHtml());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowMapMessage("Microsoft WebView2 ist nicht installiert. Die Kartenvorschau ist deshalb nicht verfügbar.");
        }
        catch (Exception ex)
        {
            ShowMapMessage($"Kartenvorschau konnte nicht geladen werden: {ex.Message}");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) ShowMapMessage("Die TomTom-Karte konnte nicht geladen werden.");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<MapMessage>(e.TryGetWebMessageAsString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (message?.Type == "ready")
            {
                _mapReady = true;
                MapMessagePanel.Visibility = Visibility.Collapsed;
            }
            else if (message?.Type == "position" && message.Latitude is double latitude && message.Longitude is double longitude)
            {
                _selectedLocation = new GeoPoint(latitude, longitude);
                UpdatePositionText(manuallyPlaced: true);
            }
        }
        catch (JsonException)
        {
            // Ignore unrelated messages from the embedded map.
        }
    }

    private string BuildMapHtml()
    {
        var apiKey = JsonSerializer.Serialize(_tomTomApiKey);
        var latitude = _result!.Location.Latitude.ToString("R", CultureInfo.InvariantCulture);
        var longitude = _result.Location.Longitude.ToString("R", CultureInfo.InvariantCulture);
        var label = JsonSerializer.Serialize(_result.ResultFreeformAddress ?? _originalAddress);
        return $$"""
            <!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <link rel="stylesheet" href="https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.64.0/maps/maps.css">
            <style>html,body,#map{width:100%;height:100%;margin:0}.pin{width:28px;height:28px;border-radius:50% 50% 50% 0;background:#98008f;border:3px solid white;box-shadow:0 2px 8px #0008;transform:rotate(-45deg)}.edit-note{position:absolute;z-index:2;top:12px;left:50%;transform:translateX(-50%);padding:8px 12px;border-radius:8px;background:#fff;color:#172033;box-shadow:0 2px 10px #0004;font:600 13px Segoe UI;display:none}</style>
            </head><body><div id="note" class="edit-note">Auf die Karte klicken oder den Pin ziehen</div><div id="map"></div>
            <script src="https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.64.0/maps/maps-web.min.js"></script><script>
            (()=>{const apiKey={{apiKey}},initial=[{{longitude}},{{latitude}}],label={{label}};
            const map=tt.map({key:apiKey,container:'map',center:initial,zoom:16,language:'de-DE'});map.addControl(new tt.NavigationControl(),'top-right');
            const element=document.createElement('div');element.className='pin';element.title=label;
            const marker=new tt.Marker({element,draggable:false}).setLngLat(initial).addTo(map);let editing=false;
            const sendPosition=()=>{const p=marker.getLngLat();chrome.webview.postMessage(JSON.stringify({type:'position',latitude:p.lat,longitude:p.lng}));};
            marker.on('dragend',sendPosition);map.on('click',e=>{if(!editing)return;marker.setLngLat(e.lngLat);sendPosition();});
            map.on('load',()=>chrome.webview.postMessage(JSON.stringify({type:'ready'})));
            window.gawelaEnablePinEdit=()=>{editing=true;marker.setDraggable(true);document.getElementById('note').style.display='block';};
            window.gawelaSetSatellite=(enabled)=>{const style=enabled
              ? `https://api.tomtom.com/style/1/style/*?map=2/basic_street-satellite&poi=2/poi_satellite&key=${encodeURIComponent(apiKey)}`
              : 'tomtom://vector/1/basic-main';map.setStyle(style);};
            window.gawelaResetPin=()=>{editing=false;marker.setDraggable(false);marker.setLngLat(initial);document.getElementById('note').style.display='none';map.easeTo({center:initial,zoom:16});sendPosition();};})();
            </script></body></html>
            """;
    }

    private async void MovePin_Click(object sender, RoutedEventArgs e)
    {
        if (!_mapReady || MapWebView.CoreWebView2 is null) return;
        await MapWebView.CoreWebView2.ExecuteScriptAsync("window.gawelaEnablePinEdit && window.gawelaEnablePinEdit();");
        MovePinButton.Content = "Pin kann verschoben werden";
    }

    private async void MapStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!_mapReady || MapWebView.CoreWebView2 is null) return;
        _satelliteView = !_satelliteView;
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.gawelaSetSatellite && window.gawelaSetSatellite({_satelliteView.ToString().ToLowerInvariant()});");
        MapStyleButton.Content = _satelliteView ? "Karte" : "Satellit";
    }

    private async void ResetPin_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        _selectedLocation = _result.Location;
        UpdatePositionText();
        MovePinButton.Content = "Pin manuell platzieren";
        if (_mapReady && MapWebView.CoreWebView2 is not null)
            await MapWebView.CoreWebView2.ExecuteScriptAsync("window.gawelaResetPin && window.gawelaResetPin();");
    }

    private void Google_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PinAddressComparison.GoogleMapsUrl(_originalAddress)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PositionText.Text = $"Google Maps konnte nicht geöffnet werden: {ex.Message}";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null || _selectedLocation is null)
        {
            PositionText.Text = "Es ist keine Kartenposition verfügbar.";
            return;
        }

        var deviationKm = DistanceInKilometers(_result.Location, _selectedLocation);
        if (deviationKm >= 2 && AppMessageBox.Show(
                $"Der gewählte Pin liegt {FormatDistance(deviationKm)} vom TomTom-Treffer entfernt. Möchten Sie diese Position trotzdem übernehmen?",
                "Grosse Abweichung der Kartenposition",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        UseFoundAddress = PinAddressComparison.CanUseFoundAddress(_result);
        DialogResult = true;
    }

    private static double DistanceInKilometers(GeoPoint first, GeoPoint second)
    {
        const double earthRadiusKm = 6371.0088;
        static double ToRadians(double degrees) => degrees * Math.PI / 180d;
        var latitudeDelta = ToRadians(second.Latitude - first.Latitude);
        var longitudeDelta = ToRadians(second.Longitude - first.Longitude);
        var firstLatitude = ToRadians(first.Latitude);
        var secondLatitude = ToRadians(second.Latitude);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
                Math.Cos(firstLatitude) * Math.Cos(secondLatitude) *
                Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string FormatDistance(double kilometers) => kilometers < 10
        ? $"{kilometers:F1} km"
        : $"{kilometers:F0} km";

    private void UpdatePositionText(bool manuallyPlaced = false)
    {
        PositionText.Text = _selectedLocation is null
            ? "Keine Kartenposition verfügbar."
            : string.Format(CultureInfo.InvariantCulture,
                manuallyPlaced ? "Manuell gewählte Position: {0:F6}, {1:F6}" : "TomTom-Position: {0:F6}, {1:F6}",
                _selectedLocation.Latitude, _selectedLocation.Longitude);
    }

    private void ShowMapMessage(string message)
    {
        MapMessageText.Text = message;
        MapMessagePanel.Visibility = Visibility.Visible;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (MapWebView.CoreWebView2 is not null)
        {
            MapWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            MapWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }
        MapWebView.Dispose();
    }

    private sealed record MapMessage(string? Type, double? Latitude = null, double? Longitude = null);
}
