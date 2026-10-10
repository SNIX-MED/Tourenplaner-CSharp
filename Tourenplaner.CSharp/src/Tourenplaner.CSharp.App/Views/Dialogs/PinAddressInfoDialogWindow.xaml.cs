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
    private AddressGeocodingResult? _result;
    private string _failureSummary;
    private readonly GeoPoint? _initialLocation;
    private readonly Task<AddressGeocodingResolution>? _pendingResolutionTask;
    private readonly bool _requireManualPlacement;
    private GeoPoint? _selectedLocation;
    private bool _mapReady;
    private bool _satelliteView;
    private CancellationTokenSource? _mapLoadTimeoutCts;

    public bool UseFoundAddress { get; private set; }
    public bool IsSelectedLocationManual { get; private set; }
    public bool IsSelectedLocationReviewRequired { get; private set; }
    public GeoPoint? SelectedLocation => _selectedLocation;

    public PinAddressInfoDialogWindow(Order order, string originalAddress,
        AddressGeocodingResult? result, string failureSummary, string tomTomApiKey,
        GeoPoint? initialLocation = null, bool initialLocationIsManual = false,
        bool initialLocationRequiresReview = false,
        Task<AddressGeocodingResolution>? pendingResolutionTask = null,
        bool requireManualPlacement = false)
    {
        InitializeComponent();
        _originalAddress = originalAddress;
        _tomTomApiKey = (tomTomApiKey ?? string.Empty).Trim();
        _result = result;
        _failureSummary = failureSummary;
        _pendingResolutionTask = pendingResolutionTask;
        _requireManualPlacement = requireManualPlacement;
        _initialLocation = initialLocation ?? result?.Location;
        _selectedLocation = _initialLocation;
        IsSelectedLocationManual = initialLocationIsManual;
        IsSelectedLocationReviewRequired = initialLocationRequiresReview;

        OrderText.Text = $"Auftrag {order.Id} · {order.CustomerName}";
        OriginalText.Text = $"Auftragsadresse: {originalAddress}";
        FoundText.Text = result is null
            ? failureSummary
            : $"Nächster TomTom-Treffer: {result.ResultFreeformAddress ?? originalAddress} ({result.MatchType})";
        MovePinButton.IsEnabled = result is not null || initialLocation is not null;
        SaveButton.IsEnabled = pendingResolutionTask is null && initialLocation is not null && !requireManualPlacement;
        UpdatePositionText(manuallyPlaced: initialLocation is not null);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_pendingResolutionTask is not null)
        {
            FoundText.Text = "TomTom-Position der Lieferadresse wird geladen …";
            try
            {
                var resolution = await _pendingResolutionTask;
                _result = resolution.Result;
                _failureSummary = ResolveFailureSummary(resolution.FailureReason);
                FoundText.Text = _result is null
                    ? _failureSummary
                    : $"Nächster TomTom-Treffer: {_result.ResultFreeformAddress ?? _originalAddress} ({_result.MatchType})";
                MovePinButton.IsEnabled = _result is not null || _initialLocation is not null;
                SaveButton.IsEnabled = (_result is not null || _initialLocation is not null) && !_requireManualPlacement;
            }
            catch (Exception ex)
            {
                _result = null;
                _failureSummary = $"TomTom-Position konnte nicht geladen werden: {ex.Message}";
                FoundText.Text = _failureSummary;
            }
        }

        if (_result is null && _initialLocation is null)
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
            _mapLoadTimeoutCts = new CancellationTokenSource();
            _ = ReportMapLoadTimeoutAsync(_mapLoadTimeoutCts.Token);
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

    private static string ResolveFailureSummary(AddressGeocodingFailureReason reason) => reason switch
    {
        AddressGeocodingFailureReason.MissingApiKey => "TomTom-API-Key fehlt.",
        AddressGeocodingFailureReason.AuthenticationFailed => "TomTom-Zugang wurde abgelehnt.",
        AddressGeocodingFailureReason.RateLimited => "TomTom drosselt die Adresssuche. Bitte später erneut versuchen.",
        AddressGeocodingFailureReason.Timeout => "Die TomTom-Anfrage hat zu lange gedauert.",
        AddressGeocodingFailureReason.ConnectionFailed => "TomTom ist derzeit nicht erreichbar.",
        _ => "Kein passender TomTom-Adresspunkt gefunden."
    };

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
                _mapLoadTimeoutCts?.Cancel();
                MapMessagePanel.Visibility = Visibility.Collapsed;
            }
            else if (message?.Type == "error")
            {
                ShowMapMessage(string.IsNullOrWhiteSpace(message.Message)
                    ? "Die Kartenvorschau konnte nicht geladen werden."
                    : message.Message);
            }
            else if (message?.Type == "position" && message.Latitude is double latitude && message.Longitude is double longitude)
            {
                _selectedLocation = new GeoPoint(latitude, longitude);
                IsSelectedLocationManual = message.Manual ?? true;
                IsSelectedLocationReviewRequired = false;
                SaveButton.IsEnabled = true;
                UpdatePositionText(manuallyPlaced: IsSelectedLocationManual);
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
        var initialLocation = _initialLocation ?? _result!.Location;
        var latitude = initialLocation.Latitude.ToString("R", CultureInfo.InvariantCulture);
        var longitude = initialLocation.Longitude.ToString("R", CultureInfo.InvariantCulture);
        var resetLocation = _result?.Location ?? initialLocation;
        var resetLatitude = resetLocation.Latitude.ToString("R", CultureInfo.InvariantCulture);
        var resetLongitude = resetLocation.Longitude.ToString("R", CultureInfo.InvariantCulture);
        var showReferenceMarker = _result is not null && IsSelectedLocationReviewRequired &&
                                  (_initialLocation is null || _initialLocation != _result.Location)
            ? "true"
            : "false";
        var label = JsonSerializer.Serialize(_result?.ResultFreeformAddress ?? _originalAddress);
        return $$"""
            <!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <style>html,body,#map{width:100%;height:100%;margin:0}.pin{width:28px;height:28px;border-radius:50% 50% 50% 0;background:#98008f;border:3px solid white;box-shadow:0 2px 8px #0008;transform:rotate(-45deg)}.reference-pin{width:22px;height:22px;border-radius:50%;background:#2563eb;border:3px solid white;box-shadow:0 2px 8px #0008;position:relative}.reference-pin:after{content:'';position:absolute;inset:6px;border-radius:50%;background:white}.edit-note{position:absolute;z-index:2;top:12px;left:50%;transform:translateX(-50%);padding:8px 12px;border-radius:8px;background:#fff;color:#172033;box-shadow:0 2px 10px #0004;font:600 13px Segoe UI;display:none}.pin-legend{position:absolute;z-index:2;left:12px;bottom:12px;padding:7px 10px;border-radius:8px;background:#fffffff2;color:#334155;box-shadow:0 2px 10px #0003;font:600 12px Segoe UI;display:none}.pin-legend span+span{margin-left:12px}.legend-manual{color:#98008f}.legend-tomtom{color:#2563eb}</style>
            </head><body><div id="note" class="edit-note">Auf die Karte klicken oder den Pin ziehen</div><div id="pinLegend" class="pin-legend"><span class="legend-manual">● Manueller Pin</span><span class="legend-tomtom">● TomTom-Adresse</span></div><div id="map"></div>
            <script>
            (()=>{const apiKey={{apiKey}},initial=[{{longitude}},{{latitude}}],resetPosition=[{{resetLongitude}},{{resetLatitude}}],showReferenceMarker={{showReferenceMarker}},label={{label}};
            const post=message=>{if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(JSON.stringify(message));};
            const cssCandidates=['https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.64.0/maps/maps.css','https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.51.0/maps/maps.css'];
            const jsCandidates=['https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.64.0/maps/maps-web.min.js','https://api.tomtom.com/maps-sdk-for-web/cdn/5.x/5.51.0/maps/maps-web.min.js'];
            const withTimeout=(promise,label)=>Promise.race([promise,new Promise((_,reject)=>setTimeout(()=>reject(new Error(`${label} Zeitüberschreitung`)),10000))]);
            const loadCss=href=>withTimeout(new Promise((resolve,reject)=>{const link=document.createElement('link');link.rel='stylesheet';link.href=href;link.onload=resolve;link.onerror=()=>reject(new Error(`CSS konnte nicht geladen werden: ${href}`));document.head.appendChild(link);}), 'CSS');
            const loadScript=src=>withTimeout(new Promise((resolve,reject)=>{const script=document.createElement('script');script.src=src;script.async=false;script.onload=resolve;script.onerror=()=>reject(new Error(`Script konnte nicht geladen werden: ${src}`));document.head.appendChild(script);}), 'Script');
            const loadFirst=async(candidates,loader)=>{let lastError;for(const candidate of candidates){try{await loader(candidate);return;}catch(error){lastError=error;} }throw lastError||new Error('Keine Quelle erreichbar.');};
            (async()=>{try{
              await loadFirst(cssCandidates,loadCss);
              await loadFirst(jsCandidates,loadScript);
              if(!(window.tt&&typeof window.tt.map==='function'))throw new Error('TomTom Maps SDK ist nicht verfügbar.');
              let mapReady=false;const map=window.tt.map({key:apiKey,container:'map',center:initial,zoom:16,language:'de-DE'});map.addControl(new window.tt.NavigationControl(),'top-right');
              const element=document.createElement('div');element.className='pin';element.title=label;
              const marker=new window.tt.Marker({element,draggable:false}).setLngLat(initial).addTo(map);let editing=false;
              let referenceMarker=null;
              if(showReferenceMarker){const referenceElement=document.createElement('div');referenceElement.className='reference-pin';referenceElement.title='TomTom-Position der aktuellen Lieferadresse';referenceMarker=new window.tt.Marker({element:referenceElement,draggable:false}).setLngLat(resetPosition).addTo(map);document.getElementById('pinLegend').style.display='block';}
              const sendPosition=()=>{const p=marker.getLngLat();post({type:'position',latitude:p.lat,longitude:p.lng,manual:editing});};
              marker.on('dragend',sendPosition);map.on('click',e=>{if(!editing)return;marker.setLngLat(e.lngLat);sendPosition();});
              map.on('load',()=>{mapReady=true;if(showReferenceMarker){const bounds=new window.tt.LngLatBounds();bounds.extend(initial);bounds.extend(resetPosition);map.fitBounds(bounds,{padding:70,maxZoom:16,duration:0});}post({type:'ready'});});
              map.on('error',event=>{if(!mapReady)post({type:'error',message:event&&event.error&&event.error.message?`Kartenvorschau: ${event.error.message}`:'Die TomTom-Karte konnte nicht geladen werden.'});});
              window.gawelaEnablePinEdit=()=>{editing=true;marker.setDraggable(true);document.getElementById('note').style.display='block';};
              window.gawelaSetSatellite=(enabled)=>{const style=enabled
                ? `https://api.tomtom.com/style/1/style/*?map=2/basic_street-satellite&poi=2/poi_satellite&key=${encodeURIComponent(apiKey)}`
                : 'tomtom://vector/1/basic-main';map.setStyle(style);};
              window.gawelaResetPin=()=>{editing=false;marker.setDraggable(false);marker.setLngLat(resetPosition);if(referenceMarker){referenceMarker.remove();referenceMarker=null;}document.getElementById('pinLegend').style.display='none';document.getElementById('note').style.display='none';map.easeTo({center:resetPosition,zoom:16});sendPosition();};
            }catch(error){post({type:'error',message:`Kartenvorschau konnte nicht geladen werden: ${error&&error.message?error.message:'Unbekannter Fehler'}`});} })();})();
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
        IsSelectedLocationManual = false;
        IsSelectedLocationReviewRequired = false;
        SaveButton.IsEnabled = true;
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
        if (_selectedLocation is null)
        {
            PositionText.Text = "Es ist keine Kartenposition verfügbar.";
            return;
        }

        var deviationKm = _result is null ? 0 : DistanceInKilometers(_result.Location, _selectedLocation);
        if (_result is not null && deviationKm >= 2 && AppMessageBox.Show(
                $"Der gewählte Pin liegt {FormatDistance(deviationKm)} vom TomTom-Treffer entfernt. Möchten Sie diese Position trotzdem übernehmen?",
                "Grosse Abweichung der Kartenposition",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        UseFoundAddress = _result is not null && PinAddressComparison.CanUseFoundAddress(_result);
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
        PositionSourceText.Text = IsSelectedLocationReviewRequired
            ? "Manueller Pin – Adresse geändert"
            : IsSelectedLocationManual ? "Pin manuell gesetzt" : "TomTom-Position";
        SaveButton.Content = IsSelectedLocationReviewRequired
            ? "Manuellen Pin beibehalten"
            : IsSelectedLocationManual ? "Manuelle Position übernehmen" : "TomTom-Position übernehmen";
        var sourceColor = IsSelectedLocationReviewRequired
            ? System.Windows.Media.Color.FromRgb(194, 65, 12)
            : IsSelectedLocationManual
                ? System.Windows.Media.Color.FromRgb(160, 0, 152)
                : System.Windows.Media.Color.FromRgb(37, 99, 235);
        PositionSourceBadge.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(24, sourceColor.R, sourceColor.G, sourceColor.B));
        PositionSourceBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(sourceColor);
        PositionSourceText.Foreground = PositionSourceBadge.BorderBrush;
    }

    private void ShowMapMessage(string message)
    {
        MapMessageText.Text = message;
        MapMessagePanel.Visibility = Visibility.Visible;
    }

    private async Task ReportMapLoadTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            if (!_mapReady)
            {
                ShowMapMessage("Die Kartenvorschau konnte nicht initialisiert werden. Bitte den Dialog erneut öffnen.");
            }
        }
        catch (OperationCanceledException)
        {
            // The map reported that it is ready.
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _mapLoadTimeoutCts?.Cancel();
        _mapLoadTimeoutCts?.Dispose();
        _mapLoadTimeoutCts = null;
        if (MapWebView.CoreWebView2 is not null)
        {
            MapWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            MapWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }
        MapWebView.Dispose();
    }

    private sealed record MapMessage(string? Type, double? Latitude = null, double? Longitude = null, string? Message = null, bool? Manual = null);
}
