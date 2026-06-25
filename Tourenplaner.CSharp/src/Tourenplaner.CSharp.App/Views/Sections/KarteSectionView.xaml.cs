using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class KarteSectionView : UserControl
{
    [Flags]
    private enum MapRefreshOperation
    {
        None = 0,
        Markers = 1 << 0,
        Route = 1 << 1,
        MarkerSelection = 1 << 2,
        RouteSelection = 1 << 3,
        PinInfoCardsVisibility = 1 << 4,
        PinInfoCardScale = 1 << 5,
        PinInfoCardZoomBehavior = 1 << 6,
        DetailsToggle = 1 << 7,
        PlannedTourOverlayHighlight = 1 << 8,
        TemporarySearchPin = 1 << 9
    }

    private static readonly GridLength DefaultRoutePanelWidth = new(460d, GridUnitType.Pixel);
    private static readonly GridLength DefaultDetailsPanelWidth = new(390d, GridUnitType.Pixel);
    private static readonly GridLength HiddenDetailsPanelWidth = new(0d, GridUnitType.Pixel);
    private const int DataRefreshDebounceMilliseconds = 45;
    private const int UiRefreshDebounceMilliseconds = 12;
    private const int PinInfoCardScaleScriptThrottleMilliseconds = 16;
    private const double DetailsPanelMinWidthExpanded = 280d;
    private bool _viewInitialized;
    private bool _mapReady;
    private bool _mapScriptReady;
    private INotifyPropertyChanged? _vmNotifier;
    private INotifyCollectionChanged? _ordersCollection;
    private INotifyCollectionChanged? _routeCollection;
    private bool _suppressSelectionSync;
    private GridLength _lastDetailsPanelWidth = DefaultDetailsPanelWidth;
    private Point? _routeGridDragStart;
    private RouteDragPayload? _routeGridDragItem;
    private int _mapRefreshRevision;
    private bool _isMapRefreshLoopRunning;
    private MapRefreshOperation _pendingMapRefresh;
    private CancellationTokenSource? _pinInfoCardScaleThrottleCts;
    private double _pendingPinInfoCardScale = 1.0d;
    private double _lastAppliedPinInfoCardScale = double.NaN;
    private string? _lastCompanyMarkerPayloadJson;
    private readonly WebViewRouteExportService _routeExportService = new();

    private sealed record RouteDragPayload(string SourceOrderId, bool IsPauseBlock);

    public KarteSectionView()
    {
        try
        {
            InitializeComponent();
            _viewInitialized = true;
        }
        catch (Exception ex)
        {
            _viewInitialized = false;
            Content = new Border
            {
                Margin = new Thickness(16),
                Padding = new Thickness(12),
                BorderBrush = System.Windows.Media.Brushes.IndianRed,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = $"Karte konnte nicht initialisiert werden: {ex.Message}"
                }
            };
        }

        if (!_viewInitialized)
        {
            return;
        }

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_viewInitialized)
        {
            return;
        }

        try
        {
            await EnsureMapInitializedAsync();
            QueueMapRefresh(MapRefreshOperation.Markers);
        }
        catch
        {
            MapWebView.Visibility = Visibility.Collapsed;
            MapFallbackNotice.Visibility = Visibility.Visible;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_viewInitialized)
        {
            return;
        }

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

        if (e.OldValue is KarteSectionViewModel oldVm)
        {
            oldVm.PdfExportHandler = null;
        }

        if (DataContext is KarteSectionViewModel vm)
        {
            _vmNotifier = vm;
            _vmNotifier.PropertyChanged += OnViewModelPropertyChanged;
            _ordersCollection = vm.MapOrders;
            _ordersCollection.CollectionChanged += OnOrdersCollectionChanged;
            _routeCollection = vm.RouteStops;
            _routeCollection.CollectionChanged += OnRouteCollectionChanged;
            vm.PdfExportHandler = ExportRoutePdfAsync;
            ApplyDetailsPanelLayout(vm);
        }

        QueueMapRefresh(MapRefreshOperation.Markers | MapRefreshOperation.Route, DataRefreshDebounceMilliseconds);
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueMapRefresh(MapRefreshOperation.Markers, DataRefreshDebounceMilliseconds);
    }

    private void OnRouteCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueMapRefresh(MapRefreshOperation.Markers | MapRefreshOperation.Route, DataRefreshDebounceMilliseconds);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_viewInitialized)
        {
            return;
        }

        if (e.PropertyName == nameof(KarteSectionViewModel.SelectedOrder) && !_suppressSelectionSync)
        {
            if (DataContext is KarteSectionViewModel routeVm &&
                routeVm.SelectedRouteStop is { IsLegSelected: true })
            {
                QueueMapRefresh(MapRefreshOperation.MarkerSelection | MapRefreshOperation.RouteSelection);
            }
            else
            {
                QueueMapRefresh(MapRefreshOperation.MarkerSelection);
            }
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.SearchFocusRevision))
        {
            QueueMapRefresh(MapRefreshOperation.MarkerSelection, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.SelectedRouteStop))
        {
            QueueMapRefresh(MapRefreshOperation.RouteSelection);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.RouteStops))
        {
            QueueMapRefresh(MapRefreshOperation.Route, DataRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.DetailSelectedStatus) ||
                 e.PropertyName == nameof(KarteSectionViewModel.DetailOrderStatus) ||
                 e.PropertyName == nameof(KarteSectionViewModel.DetailSelectedAvisoStatus) ||
                 e.PropertyName == nameof(KarteSectionViewModel.DetailAvisoStatus))
        {
            QueueMapRefresh(MapRefreshOperation.Markers | MapRefreshOperation.Route, DataRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.RouteGeometryPoints))
        {
            QueueMapRefresh(MapRefreshOperation.Route, DataRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.RouteVisualRevision))
        {
            QueueMapRefresh(MapRefreshOperation.Route, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.IsAllPlannedToursVisible) ||
                 e.PropertyName == nameof(KarteSectionViewModel.PlannedTourOverlayRevision))
        {
            QueueMapRefresh(MapRefreshOperation.Route, DataRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.SelectedBatchOrderCount))
        {
            QueueMapRefresh(MapRefreshOperation.Markers, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.PlannedTourOverlayHighlightTourId))
        {
            QueueMapRefresh(MapRefreshOperation.PlannedTourOverlayHighlight, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.ArePinInfoCardsVisible))
        {
            QueueMapRefresh(MapRefreshOperation.PinInfoCardsVisibility, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.PinInfoCardScale))
        {
            QueueMapRefresh(MapRefreshOperation.PinInfoCardScale, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.PinInfoCardZoomBehaviorStrength))
        {
            QueueMapRefresh(MapRefreshOperation.PinInfoCardZoomBehavior, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.MapPinInfoCardShowName) ||
                 e.PropertyName == nameof(KarteSectionViewModel.MapPinInfoCardShowOrderNumber) ||
                 e.PropertyName == nameof(KarteSectionViewModel.MapPinInfoCardShowStreet) ||
                 e.PropertyName == nameof(KarteSectionViewModel.MapPinInfoCardShowPostalCodeCity) ||
                 e.PropertyName == nameof(KarteSectionViewModel.MapPinInfoCardShowNotes) ||
                 e.PropertyName == nameof(KarteSectionViewModel.MapPinInfoCardShowProducts) ||
                 e.PropertyName == nameof(KarteSectionViewModel.MapPinInfoCardShowTotalWeight))
        {
            QueueMapRefresh(MapRefreshOperation.Markers, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.TemporarySearchPinRevision))
        {
            QueueMapRefresh(MapRefreshOperation.TemporarySearchPin, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.IsDetailsOpen) ||
                 e.PropertyName == nameof(KarteSectionViewModel.IsDetailsPanelExpanded) ||
                 e.PropertyName == nameof(KarteSectionViewModel.DetailsToggleGlyph))
        {
            QueueMapRefresh(MapRefreshOperation.DetailsToggle, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.TomTomApiKey) ||
                 e.PropertyName == nameof(KarteSectionViewModel.TomTomEnableTileCache) ||
                 e.PropertyName == nameof(KarteSectionViewModel.CurrentRouteAppliedMaxSpeedKmh))
        {
            _ = ReloadMapDocumentAsync();
        }
    }

    private async Task ReloadMapDocumentAsync()
    {
        if (!_mapReady || MapWebView.CoreWebView2 is null || DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        _mapScriptReady = false;
        _lastCompanyMarkerPayloadJson = null;
        var html = MapHtmlDocumentBuilder.Build(
            vm.TomTomApiKey,
            vm.TomTomShowTrafficFlow,
            vm.TomTomEnableTileCache,
            vm.TomTomMapOverlayStyle,
            vm.TomTomShowTrafficIncidents,
            vm.TomTomShowRoadLabels,
            vm.TomTomShowPoi,
            vm.TomTomUseVehicleDimensions,
            vm.TomTomUseVehicleWeightRestrictions,
            vm.TomTomUseDepartAtTraffic,
            vm.PinInfoCardScale,
            vm.CurrentRouteAppliedMaxSpeedKmh);
        MapWebView.NavigateToString(html);
        await Task.CompletedTask;
    }

    private void QueueMapRefresh(MapRefreshOperation operation, int debounceMilliseconds = 0)
    {
        _pendingMapRefresh |= operation;
        _ = ProcessMapRefreshQueueAsync(debounceMilliseconds);
    }

    private async Task ProcessMapRefreshQueueAsync(int debounceMilliseconds)
    {
        var revision = Interlocked.Increment(ref _mapRefreshRevision);
        if (debounceMilliseconds > 0)
        {
            await Task.Delay(debounceMilliseconds);
            if (revision != Volatile.Read(ref _mapRefreshRevision))
            {
                return;
            }
        }

        if (_isMapRefreshLoopRunning)
        {
            return;
        }

        _isMapRefreshLoopRunning = true;
        try
        {
            while (true)
            {
                var operation = _pendingMapRefresh;
                if (operation == MapRefreshOperation.None)
                {
                    break;
                }

                _pendingMapRefresh = MapRefreshOperation.None;
                await ExecuteMapRefreshAsync(operation);
            }
        }
        catch
        {
            MapWebView.Visibility = Visibility.Collapsed;
            MapFallbackNotice.Visibility = Visibility.Visible;
        }
        finally
        {
            _isMapRefreshLoopRunning = false;
            if (_pendingMapRefresh != MapRefreshOperation.None)
            {
                _ = ProcessMapRefreshQueueAsync(0);
            }
        }
    }

    private async Task ExecuteMapRefreshAsync(MapRefreshOperation operation)
    {
        if ((operation & MapRefreshOperation.DetailsToggle) != 0)
        {
            await ApplyDetailsToggleStateAsync();
        }

        if ((operation & MapRefreshOperation.Markers) != 0)
        {
            await PushMarkersToMapAsync();
        }
        else
        {
            if ((operation & MapRefreshOperation.PinInfoCardsVisibility) != 0)
            {
                await ApplyPinInfoCardsVisibilityAsync();
            }

            if ((operation & MapRefreshOperation.PinInfoCardScale) != 0)
            {
                await ApplyPinInfoCardScaleAsync();
            }
            if ((operation & MapRefreshOperation.PinInfoCardZoomBehavior) != 0)
            {
                await ApplyPinInfoCardZoomBehaviorAsync();
            }

            if ((operation & MapRefreshOperation.TemporarySearchPin) != 0)
            {
                await ApplyTemporarySearchPinAsync();
            }

            if ((operation & MapRefreshOperation.MarkerSelection) != 0)
            {
                await HighlightSelectedMarkerAsync();
            }
        }

        if ((operation & MapRefreshOperation.Route) != 0)
        {
            await PushRouteToMapAsync();
        }
        else
        {
            if ((operation & MapRefreshOperation.RouteSelection) != 0)
            {
                await HighlightSelectedRouteStopAsync();
            }

            if ((operation & MapRefreshOperation.PlannedTourOverlayHighlight) != 0)
            {
                await HighlightPlannedTourOverlayAsync();
            }
        }
    }

    private void OnDetailsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer detailsViewer)
        {
            return;
        }

        var delta = e.Delta / 3d;
        var target = detailsViewer.VerticalOffset - delta;

        if (detailsViewer.ScrollableHeight > 0 && target >= 0 && target <= detailsViewer.ScrollableHeight)
        {
            detailsViewer.ScrollToVerticalOffset(target);
            e.Handled = true;
            return;
        }

        if (PageScrollViewer is null || PageScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var pageTarget = PageScrollViewer.VerticalOffset - delta;
        if (pageTarget < 0)
        {
            pageTarget = 0;
        }
        else if (pageTarget > PageScrollViewer.ScrollableHeight)
        {
            pageTarget = PageScrollViewer.ScrollableHeight;
        }

        PageScrollViewer.ScrollToVerticalOffset(pageTarget);
        e.Handled = true;
    }

    private void OnRouteStopsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer stopsViewer || stopsViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var delta = e.Delta / 3d;
        var target = stopsViewer.VerticalOffset - delta;

        if (target < 0)
        {
            target = 0;
        }
        else if (target > stopsViewer.ScrollableHeight)
        {
            target = stopsViewer.ScrollableHeight;
        }

        stopsViewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private async void OnDetailProductMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not DetailProductItem productItem)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            vm.ToggleDetailProductSelection(productItem);
            e.Handled = true;
            return;
        }

        vm.SelectSingleDetailProduct(productItem);

        if (e.ClickCount != 2)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        await vm.EditDetailProductAsync(productItem);
    }

    private void OnStartTimePreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = string.IsNullOrWhiteSpace(e.Text) || !e.Text.All(char.IsDigit);
    }

    private void OnStartTimePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(text) || !text.All(char.IsDigit))
        {
            e.CancelCommand();
        }
    }

    private void OnStartTimeFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm || sender is not TextBox textBox)
        {
            return;
        }

        var isMinuteField = string.Equals(textBox.Tag as string, "minute", StringComparison.OrdinalIgnoreCase);
        var max = isMinuteField ? 59 : 23;
        var normalized = NormalizeTwoDigitTimePart(textBox.Text, max);
        if (isMinuteField)
        {
            vm.RouteStartMinute = normalized;
        }
        else
        {
            vm.RouteStartHour = normalized;
        }
    }

    private static string NormalizeTwoDigitTimePart(string? value, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = 0;
        }

        parsed = Math.Clamp(parsed, 0, max);
        return parsed.ToString("00", CultureInfo.InvariantCulture);
    }

    private void OnMainSplitterMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RoutePanelColumn is null)
        {
            return;
        }

        RoutePanelColumn.Width = DefaultRoutePanelWidth;
        e.Handled = true;
    }

    private void OnDetailsSplitterMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DetailsPanelColumn is null || DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        if (!vm.IsDetailsOpen || !vm.IsDetailsPanelExpanded)
        {
            return;
        }

        _lastDetailsPanelWidth = DefaultDetailsPanelWidth;
        DetailsPanelColumn.MinWidth = DetailsPanelMinWidthExpanded;
        DetailsPanelColumn.Width = DefaultDetailsPanelWidth;
        e.Handled = true;
    }

    private async Task EnsureMapInitializedAsync()
    {
        if (!_viewInitialized)
        {
            return;
        }

        if (_mapReady)
        {
            return;
        }

        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync("Map");
            await MapWebView.EnsureCoreWebView2Async(environment);
            MapWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            MapWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            MapWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            MapWebView.CoreWebView2.ContextMenuRequested += OnMapContextMenuRequested;
            var vm = DataContext as KarteSectionViewModel;
            var html = MapHtmlDocumentBuilder.Build(
                vm?.TomTomApiKey,
                vm?.TomTomShowTrafficFlow ?? true,
                vm?.TomTomEnableTileCache ?? true,
                vm?.TomTomMapOverlayStyle ?? AppSettings.DefaultMapOverlayStyle,
                vm?.TomTomShowTrafficIncidents ?? false,
                vm?.TomTomShowRoadLabels ?? true,
                vm?.TomTomShowPoi ?? true,
                vm?.TomTomUseVehicleDimensions ?? false,
                vm?.TomTomUseVehicleWeightRestrictions ?? false,
                vm?.TomTomUseDepartAtTraffic ?? true,
                vm?.PinInfoCardScale ?? AppSettings.DefaultPinInfoCardScale,
                vm?.CurrentRouteAppliedMaxSpeedKmh ?? 0);
            MapWebView.NavigateToString(html);
            _mapReady = true;
            MapWebView.Visibility = Visibility.Visible;
            MapFallbackNotice.Visibility = Visibility.Collapsed;
            QueueMapRefresh(MapRefreshOperation.DetailsToggle);
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

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || MapWebView.CoreWebView2 is null)
        {
            _mapScriptReady = false;
            MapFallbackNotice.Text = "Karte nicht verfügbar. Navigation zur Kartenansicht fehlgeschlagen.";
            MapFallbackNotice.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _mapScriptReady = false;
            for (var i = 0; i < 30; i++)
            {
                var ready = await MapWebView.CoreWebView2.ExecuteScriptAsync(
                    "(document.readyState === 'complete' && window.gawelaMapReady === true && typeof window.gawelaSetMarkers === 'function' && typeof window.gawelaSetRoute === 'function' && typeof window.gawelaSetPlannedTourOverlays === 'function' && typeof window.gawelaSetCompanyMarker === 'function' && typeof window.gawelaHighlightPlannedTourOverlay === 'function') ? 'true' : 'false';");
                _mapScriptReady = ready.Contains("true", StringComparison.OrdinalIgnoreCase);
                if (_mapScriptReady)
                {
                    break;
                }

                await Task.Delay(100);
            }

            if (_mapScriptReady)
            {
                MapFallbackNotice.Visibility = Visibility.Collapsed;
                QueueMapRefresh(
                    MapRefreshOperation.Markers |
                    MapRefreshOperation.Route |
                    MapRefreshOperation.PinInfoCardScale |
                    MapRefreshOperation.PinInfoCardZoomBehavior |
                    MapRefreshOperation.DetailsToggle);
            }
            else
            {
                MapFallbackNotice.Text = "Karte nicht verfügbar: Map-Skript wurde nicht initialisiert.";
                MapFallbackNotice.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            _mapScriptReady = false;
            MapFallbackNotice.Text = "Karte nicht verfügbar: Fehler beim Laden der Kartenlogik.";
            MapFallbackNotice.Visibility = Visibility.Visible;
        }
    }

    private void OnMapContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        RemoveBlockedMapContextMenuItems(e.MenuItems);
    }

    private static void RemoveBlockedMapContextMenuItems(IList<CoreWebView2ContextMenuItem> menuItems)
    {
        for (var i = menuItems.Count - 1; i >= 0; i--)
        {
            var item = menuItems[i];
            if (item.Children.Count > 0)
            {
                RemoveBlockedMapContextMenuItems(item.Children);
            }

            var name = (item.Name ?? string.Empty).Trim();
            var label = (item.Label ?? string.Empty).Trim();
            var blockByName = string.Equals(name, "saveAs", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(name, "inspectElement", StringComparison.OrdinalIgnoreCase);
            var blockByLabel = string.Equals(label, "Speichern unter", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(label, "Untersuchen", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(label, "Save as", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(label, "Inspect", StringComparison.OrdinalIgnoreCase);

            if (blockByName || blockByLabel)
            {
                menuItems.RemoveAt(i);
            }
        }
    }

    private async Task PushMarkersToMapAsync()
    {
        if (!_mapReady || !_mapScriptReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var markers = vm.GetMapMarkerSnapshot().Select(x => new
        {
            id = x.OrderId,
            customer = x.Customer,
            street = x.Street,
            postalCodeCity = x.PostalCodeCity,
            notes = x.Notes,
            products = x.ProductLines,
            totalWeightKgText = x.TotalWeightKgText,
            showName = vm.MapPinInfoCardShowName,
            showOrderNumber = vm.MapPinInfoCardShowOrderNumber,
            showStreet = vm.MapPinInfoCardShowStreet,
            showPostalCodeCity = vm.MapPinInfoCardShowPostalCodeCity,
            showNotes = vm.MapPinInfoCardShowNotes,
            showProducts = vm.MapPinInfoCardShowProducts,
            showTotalWeight = vm.MapPinInfoCardShowTotalWeight,
            status = x.StatusLabel,
            avisoStatus = x.AvisoStatusLabel,
            isAssigned = x.IsAssigned,
            isDimmed = x.IsDimmed,
            isBatchSelected = x.IsBatchSelected,
            hasPendingPreparation = x.HasPendingPreparation,
            color = x.StatusColorHex,
            shape = ResolveDeliveryShape(x.DeliveryLabel),
            lat = x.Latitude,
            lon = x.Longitude
        }).ToList();

        var json = JsonSerializer.Serialize(markers);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetMarkers === 'function') window.gawelaSetMarkers({json});");
        var company = vm.CompanyMarker is null
            ? null
            : new
            {
                name = vm.CompanyMarker.Name,
                address = vm.CompanyMarker.Address,
                lat = vm.CompanyMarker.Latitude,
                lon = vm.CompanyMarker.Longitude
            };
        var companyJson = JsonSerializer.Serialize(company);
        if (!string.Equals(_lastCompanyMarkerPayloadJson, companyJson, StringComparison.Ordinal))
        {
            await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetCompanyMarker === 'function') window.gawelaSetCompanyMarker({companyJson});");
            _lastCompanyMarkerPayloadJson = companyJson;
        }

        await ApplyPinInfoCardsVisibilityAsync();
        await ApplyPinInfoCardScaleAsync();
        await ApplyPinInfoCardZoomBehaviorAsync();
        await ApplyTemporarySearchPinAsync();
    }

    private async Task PushRouteToMapAsync()
    {
        if (!_mapReady || !_mapScriptReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var route = vm.GetRouteSnapshot()
            .Select(r =>
            {
                var visual = vm.ResolveOrderVisualInfo(r.OrderId);
                return new
                {
                    id = r.OrderId,
                    position = r.Position,
                    label = r.DisplayPosition,
                    avisoStatus = visual.AvisoStatusLabel,
                    isAssigned = visual.IsAssigned,
                    hasPendingPreparation = visual.HasPendingPreparation,
                    color = visual.StatusColorHex,
                    shape = ResolveDeliveryShape(visual.DeliveryLabel),
                    lat = r.Latitude,
                    lon = r.Longitude
                };
            })
            .ToList();
        var geometry = vm.GetRouteGeometrySnapshot()
            .Select(x => new { lat = x.Latitude, lon = x.Longitude })
            .ToList();
        var trafficSegments = vm.GetRouteTrafficSegmentSnapshot()
            .Select(x => new
            {
                startIndex = x.StartIndex,
                endIndex = x.EndIndex,
                trafficLevel = x.TrafficLevel
            })
            .ToList();
        var overlays = vm.GetPlannedTourRouteOverlaySnapshot()
            .Select(x => new
            {
                id = x.TourId,
                label = x.Label,
                color = x.ColorHex,
                outlineColor = x.WarningOutlineColorHex,
                path = x.Points.Select(p => new { lat = p.Latitude, lon = p.Longitude }).ToList()
            })
            .ToList();

        var routeJson = JsonSerializer.Serialize(route);
        var geometryJson = JsonSerializer.Serialize(geometry);
        var trafficSegmentsJson = JsonSerializer.Serialize(trafficSegments);
        var routeColorJson = JsonSerializer.Serialize(vm.GetActiveRoutePolylineColor());
        var overlaysJson = JsonSerializer.Serialize(overlays);
        var routeInfoJson = JsonSerializer.Serialize(vm.RoutingProviderStatusText);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetPlannedTourOverlays === 'function') window.gawelaSetPlannedTourOverlays({overlaysJson});");
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetRoute === 'function') window.gawelaSetRoute({routeJson}, {geometryJson}, {routeColorJson}, {trafficSegmentsJson});");
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetRouteInfo === 'function') window.gawelaSetRouteInfo({routeInfoJson});");
        await HighlightPlannedTourOverlayAsync();
    }

    private async Task HighlightSelectedMarkerAsync()
    {
        if (!_mapReady || !_mapScriptReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var id = vm.SelectedRouteStop is { IsLegSelected: true }
            ? string.Empty
            : vm.SelectedOrder?.OrderId ?? string.Empty;
        var jsonId = JsonSerializer.Serialize(id);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetStickyPopupOrderId === 'function') window.gawelaSetStickyPopupOrderId({jsonId});");
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaHighlightMarker === 'function') window.gawelaHighlightMarker({jsonId});");
    }

    private async Task HighlightSelectedRouteStopAsync()
    {
        if (!_mapReady || !_mapScriptReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var id = vm.SelectedRouteStop is { IsLegSelected: true }
            ? string.Empty
            : vm.SelectedRouteStop?.OrderId ?? string.Empty;
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaHighlightRouteStop === 'function') window.gawelaHighlightRouteStop({JsonSerializer.Serialize(id)});");
    }

    private async Task HighlightPlannedTourOverlayAsync()
    {
        if (!_mapReady || !_mapScriptReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var tourIdJson = JsonSerializer.Serialize(vm.PlannedTourOverlayHighlightTourId);
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaHighlightPlannedTourOverlay === 'function') window.gawelaHighlightPlannedTourOverlay({tourIdJson});");
    }

    private async Task ApplyPinInfoCardsVisibilityAsync()
    {
        if (!_mapReady || !_mapScriptReady || MapWebView.CoreWebView2 is null || DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaSetAllMarkerPopupsVisible === 'function') window.gawelaSetAllMarkerPopupsVisible({(vm.ArePinInfoCardsVisible ? "true" : "false")});");
    }

    private async Task ApplyPinInfoCardScaleAsync()
    {
        if (!_mapReady || !_mapScriptReady || MapWebView.CoreWebView2 is null || DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        _pendingPinInfoCardScale = vm.PinInfoCardScale;
        _pinInfoCardScaleThrottleCts?.Cancel();
        var throttleCts = new CancellationTokenSource();
        _pinInfoCardScaleThrottleCts = throttleCts;

        try
        {
            await Task.Delay(PinInfoCardScaleScriptThrottleMilliseconds, throttleCts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (throttleCts.IsCancellationRequested)
        {
            return;
        }

        var targetScale = _pendingPinInfoCardScale;
        if (Math.Abs(targetScale - _lastAppliedPinInfoCardScale) < 0.0001d)
        {
            return;
        }

        _lastAppliedPinInfoCardScale = targetScale;
        var scaleJson = JsonSerializer.Serialize(targetScale);
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaSetPopupSizeMultiplier === 'function') window.gawelaSetPopupSizeMultiplier({scaleJson});" +
            $"if (typeof window.gawelaSetMapOptionsPinInfoCardScale === 'function') window.gawelaSetMapOptionsPinInfoCardScale({scaleJson});");
    }

    private async Task ApplyPinInfoCardZoomBehaviorAsync()
    {
        if (!_mapReady || !_mapScriptReady || MapWebView.CoreWebView2 is null || DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        var strengthJson = JsonSerializer.Serialize(vm.PinInfoCardZoomBehaviorStrength);
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaSetPopupZoomBehaviorStrength === 'function') window.gawelaSetPopupZoomBehaviorStrength({strengthJson});");
    }

    private async Task ApplyDetailsToggleStateAsync()
    {
        if (DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        ApplyDetailsPanelLayout(vm);

        if (!_mapReady || !_mapScriptReady || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var isVisible = vm.IsDetailsOpen && !vm.IsDetailsPanelExpanded;
        var glyph = vm.DetailsToggleGlyph;
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaSetDetailsToggle === 'function') window.gawelaSetDetailsToggle({(isVisible ? "true" : "false")}, {JsonSerializer.Serialize(glyph)});");
    }

    private void ApplyDetailsPanelLayout(KarteSectionViewModel vm)
    {
        if (DetailsPanelColumn is null)
        {
            return;
        }

        var showDetailsPanel = vm.IsDetailsOpen && vm.IsDetailsPanelExpanded;
        if (!showDetailsPanel)
        {
            if (DetailsPanelColumn.Width is { GridUnitType: GridUnitType.Pixel } currentWidth && currentWidth.Value > 0d)
            {
                _lastDetailsPanelWidth = currentWidth;
            }

            DetailsPanelColumn.MinWidth = 0d;
            DetailsPanelColumn.Width = HiddenDetailsPanelWidth;
            return;
        }

        var targetWidth = _lastDetailsPanelWidth;
        if (targetWidth.GridUnitType != GridUnitType.Pixel || targetWidth.Value < DetailsPanelMinWidthExpanded)
        {
            targetWidth = DefaultDetailsPanelWidth;
        }

        DetailsPanelColumn.MinWidth = DetailsPanelMinWidthExpanded;
        DetailsPanelColumn.Width = targetWidth;
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

        if (raw.StartsWith("mapdiag:", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.StartsWith("mapdiag:error:", StringComparison.OrdinalIgnoreCase))
            {
                var reason = raw["mapdiag:error:".Length..];
                MapFallbackNotice.Text = $"Karte nicht verfügbar: {reason}";
                MapFallbackNotice.Visibility = Visibility.Visible;
            }
            else if (raw.StartsWith("mapdiag:ok:", StringComparison.OrdinalIgnoreCase))
            {
                MapFallbackNotice.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (raw.StartsWith("batchToggle:", StringComparison.OrdinalIgnoreCase))
        {
            var orderId = raw["batchToggle:".Length..];
            vm.ToggleBatchOrderSelectionById(orderId);
            return;
        }

        if (raw.StartsWith("mapopts:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = raw["mapopts:".Length..];
            var parts = payload.Split('|');
            if (parts.Length >= 4)
            {
                var style = (parts[0] ?? string.Empty).Trim();
                var showTrafficFlow = string.Equals(parts[1], "1", StringComparison.Ordinal);
                var showTrafficIncidents = string.Equals(parts[2], "1", StringComparison.Ordinal);
                var showRoadLabels = parts.Length >= 5
                    ? string.Equals(parts[3], "1", StringComparison.Ordinal)
                    : true;
                var showPoi = string.Equals(parts.Length >= 5 ? parts[4] : parts[3], "1", StringComparison.Ordinal);
                var useVehicleDimensions = parts.Length >= 6 && string.Equals(parts[5], "1", StringComparison.Ordinal);
                var useVehicleWeightRestrictions = parts.Length >= 7 && string.Equals(parts[6], "1", StringComparison.Ordinal);
                var useDepartAtTraffic = parts.Length >= 8 ? string.Equals(parts[7], "1", StringComparison.Ordinal) : true;
                _ = ApplyMapOptionsFromWebAsync(vm, style, showTrafficFlow, showTrafficIncidents, showRoadLabels, showPoi, useVehicleDimensions, useVehicleWeightRestrictions, useDepartAtTraffic);
            }

            return;
        }

        if (raw.StartsWith("pinInfoCardScale:", StringComparison.OrdinalIgnoreCase))
        {
            var valueText = raw["pinInfoCardScale:".Length..];
            if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            {
                vm.PinInfoCardScale = Math.Clamp(scale, 0.7d, 1.8d);
            }

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

        if (raw.StartsWith("plannedTourSelect:", StringComparison.OrdinalIgnoreCase))
        {
            var tourIdText = raw["plannedTourSelect:".Length..];
            if (int.TryParse(tourIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tourId) && tourId > 0)
            {
                vm.SelectTourOverviewById(tourId);
            }

            return;
        }

        if (string.Equals(raw, "toggleDetails", StringComparison.OrdinalIgnoreCase))
        {
            if (vm.ToggleDetailsPanelCommand.CanExecute(null))
            {
                vm.ToggleDetailsPanelCommand.Execute(null);
            }

            return;
        }

        var match = vm.MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, raw, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            try
            {
                _suppressSelectionSync = true;
                vm.SelectOrderFromMapPin(match.OrderId);
            }
            finally
            {
                _suppressSelectionSync = false;
            }

            return;
        }

        vm.SelectOrderFromMapPin(raw);
    }

    private async Task ApplyMapOptionsFromWebAsync(
        KarteSectionViewModel vm,
        string style,
        bool showTrafficFlow,
        bool showTrafficIncidents,
        bool showRoadLabels,
        bool showPoi,
        bool useVehicleDimensions,
        bool useVehicleWeightRestrictions,
        bool useDepartAtTraffic)
    {
        await vm.UpdateMapOverlayOptionsAsync(style, showTrafficFlow, showTrafficIncidents, showRoadLabels, showPoi, useVehicleDimensions, useVehicleWeightRestrictions, useDepartAtTraffic);
        if ((useVehicleDimensions && !vm.TomTomUseVehicleDimensions) ||
            (useVehicleWeightRestrictions && !vm.TomTomUseVehicleWeightRestrictions))
        {
            if (MapWebView.CoreWebView2 is not null)
            {
                await MapWebView.CoreWebView2.ExecuteScriptAsync(
                    $"if (window.gawelaSetVehicleRoutingOptions) {{ window.gawelaSetVehicleRoutingOptions({(vm.TomTomUseVehicleDimensions ? "true" : "false")}, {(vm.TomTomUseVehicleWeightRestrictions ? "true" : "false")}); }}");
            }
        }
    }

    private async void RouteStopsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        if (RouteStopsList.SelectedItem is not RouteStopItem)
        {
            return;
        }

        await vm.EditSelectedRouteStopStayMinutesAsync();
    }

    private async void TourOverviewList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not ListBox listBox ||
            listBox.SelectedItem is not SavedTourOverviewItem selected ||
            selected.TourId <= 0)
        {
            return;
        }

        await vm.FocusTourAsync(selected.TourId);
    }

    private void TourOverviewList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not ListBox listBox)
        {
            return;
        }

        var listItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listItem is not null)
        {
            return;
        }

        listBox.SelectedItem = null;
        vm.SetHoveredTourOverviewId(0);
    }

    private void TourOverviewList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var listItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listItem?.DataContext is SavedTourOverviewItem selected)
        {
            listBox.SelectedItem = selected;
            return;
        }

        listBox.SelectedItem = null;
        if (DataContext is KarteSectionViewModel vm)
        {
            vm.SetHoveredTourOverviewId(0);
        }
    }

    private void TourOverviewList_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        var listItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listItem?.DataContext is SavedTourOverviewItem tour && tour.TourId > 0)
        {
            vm.SetHoveredTourOverviewId(tour.TourId);
            return;
        }

        vm.SetHoveredTourOverviewId(0);
    }

    private void TourOverviewList_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is KarteSectionViewModel vm)
        {
            vm.SetHoveredTourOverviewId(0);
        }
    }

    private void TourOverviewList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBox listBox ||
            DataContext is not KarteSectionViewModel vm ||
            listBox.SelectedItem is not SavedTourOverviewItem selected ||
            selected.TourId <= 0)
        {
            e.Handled = true;
            return;
        }

        vm.SelectTourOverviewById(selected.TourId);
    }

    private void OnRouteStopContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            e.Handled = true;
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
    }

    private async void OnRouteStopEditStayMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        await vm.EditSelectedRouteStopStayMinutesAsync();
    }

    private void OnRouteStopMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        QueueMapRefresh(MapRefreshOperation.RouteSelection);
    }

    private async void OnRouteStopAddPauseMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        await vm.AddPauseAfterSelectedRouteStopAsync();
    }

    private void OnRouteStopRemoveMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        if (vm.RemoveFromRouteCommand.CanExecute(null))
        {
            vm.RemoveFromRouteCommand.Execute(null);
        }
    }

    private void OnRouteStopEditOrderMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        if (vm.EditOrderCommand.CanExecute(null))
        {
            vm.EditOrderCommand.Execute(null);
        }
    }

    private void OnRouteLegContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            e.Handled = true;
            return;
        }

        vm.SelectRouteLegByOrderId(stopItem.OrderId);
        if (element.ContextMenu is not ContextMenu contextMenu)
        {
            e.Handled = true;
            return;
        }

        var hasPause = vm.HasPauseAfterSelectedRouteStop();
        if (contextMenu.Items.Count >= 3 &&
            contextMenu.Items[0] is MenuItem addPauseMenuItem &&
            contextMenu.Items[1] is MenuItem editPauseMenuItem &&
            contextMenu.Items[2] is MenuItem removePauseMenuItem)
        {
            addPauseMenuItem.Visibility = hasPause ? Visibility.Collapsed : Visibility.Visible;
            editPauseMenuItem.Visibility = hasPause ? Visibility.Visible : Visibility.Collapsed;
            removePauseMenuItem.Visibility = hasPause ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnRouteLegMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteLegByOrderId(stopItem.OrderId);
        QueueMapRefresh(MapRefreshOperation.RouteSelection);
        e.Handled = true;
    }

    private async void OnRouteLegAddPauseMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        await vm.AddPauseAfterSelectedRouteStopAsync();
    }

    private async void OnRouteLegEditPauseMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        await vm.EditPauseAfterSelectedRouteStopAsync();
    }

    private void OnRouteLegRemovePauseMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        vm.RemovePauseAfterSelectedRouteStop();
    }

    private async Task ApplyTemporarySearchPinAsync()
    {
        if (!_mapReady || !_mapScriptReady || MapWebView.CoreWebView2 is null || DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        if (!vm.HasTemporarySearchPin)
        {
            await MapWebView.CoreWebView2.ExecuteScriptAsync(
                "if (typeof window.gawelaClearTempSearchMarker === 'function') window.gawelaClearTempSearchMarker();");
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            lat = vm.TemporarySearchPinLatitude,
            lon = vm.TemporarySearchPinLongitude,
            label = vm.TemporarySearchPinLabel
        });
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaSetTempSearchMarker === 'function') window.gawelaSetTempSearchMarker({payload});");
    }

    private void RouteStopsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _routeGridDragStart = null;
        _routeGridDragItem = null;
        ClearRouteDropIndicator();

        if (VisualTreeUtilities.FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var listItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listItem?.DataContext is not RouteStopItem item || item.IsCompanyAnchor)
        {
            return;
        }

        if (HasAncestorTag(e.OriginalSource as DependencyObject, "RoutePauseDragHandle"))
        {
            if (!item.HasPauseAfter)
            {
                return;
            }

            _routeGridDragStart = e.GetPosition(RouteStopsList);
            _routeGridDragItem = new RouteDragPayload(item.OrderId, true);
            return;
        }

        if (HasAncestorTag(e.OriginalSource as DependencyObject, "RouteLegSurface"))
        {
            return;
        }

        _routeGridDragStart = e.GetPosition(RouteStopsList);
        _routeGridDragItem = new RouteDragPayload(item.OrderId, false);
    }

    private void RouteStopsList_MouseMove(object sender, MouseEventArgs e)
    {
        if (_routeGridDragStart is null || _routeGridDragItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(RouteStopsList);
        var delta = current - _routeGridDragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragItem = _routeGridDragItem;
        _routeGridDragStart = null;
        _routeGridDragItem = null;
        DragDrop.DoDragDrop(RouteStopsList, new DataObject(typeof(RouteDragPayload), dragItem), DragDropEffects.Move);
        ClearRouteDropIndicator();
    }

    private void RouteStopsList_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            !e.Data.GetDataPresent(typeof(RouteDragPayload)))
        {
            ClearRouteDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var payload = e.Data.GetData(typeof(RouteDragPayload)) as RouteDragPayload;
        var targetItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetStop = targetItem?.DataContext as RouteStopItem;
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.SourceOrderId) ||
            targetItem is null ||
            targetStop is null ||
            targetStop.IsCompanyAnchor ||
            targetStop.IsPauseStop)
        {
            ClearRouteDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourceStop = vm.RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, payload.SourceOrderId, StringComparison.OrdinalIgnoreCase));
        if (sourceStop is null || sourceStop.IsCompanyAnchor)
        {
            ClearRouteDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var insertAfter = GetInsertAfter(targetItem, e);
        if (!payload.IsPauseBlock &&
            string.Equals(sourceStop.OrderId, targetStop.OrderId, StringComparison.OrdinalIgnoreCase))
        {
            ClearRouteDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (payload.IsPauseBlock &&
            string.Equals(sourceStop.OrderId, targetStop.OrderId, StringComparison.OrdinalIgnoreCase) &&
            insertAfter)
        {
            ClearRouteDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        SetRouteDropIndicator(vm, targetStop, insertAfter);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void RouteStopsList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            !e.Data.GetDataPresent(typeof(RouteDragPayload)))
        {
            return;
        }

        var payload = e.Data.GetData(typeof(RouteDragPayload)) as RouteDragPayload;
        var targetItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetStop = targetItem?.DataContext as RouteStopItem;
        if (payload is null || string.IsNullOrWhiteSpace(payload.SourceOrderId) || targetItem is null || targetStop is null)
        {
            ClearRouteDropIndicator();
            return;
        }

        var insertAfter = GetInsertAfter(targetItem, e);
        var moved = payload.IsPauseBlock
            ? vm.MovePauseBlockByAnchorOrderIds(payload.SourceOrderId, targetStop.OrderId, insertAfter)
            : vm.MoveRouteStopByOrderIds(payload.SourceOrderId, targetStop.OrderId, insertAfter);

        ClearRouteDropIndicator();
        if (moved && vm.SelectedRouteStop is not null)
        {
            RouteStopsList.SelectedItem = vm.SelectedRouteStop;
            RouteStopsList.ScrollIntoView(vm.SelectedRouteStop);
        }
    }

    private void RouteStopsList_DragLeave(object sender, DragEventArgs e)
    {
        ClearRouteDropIndicator();
    }

    private static bool GetInsertAfter(ListBoxItem targetItem, DragEventArgs e)
    {
        var position = e.GetPosition(targetItem);
        return position.Y > targetItem.ActualHeight / 2d;
    }

    private void SetRouteDropIndicator(KarteSectionViewModel vm, RouteStopItem targetStop, bool insertAfter)
    {
        foreach (var stop in vm.RouteStops)
        {
            stop.IsDropTargetBefore = false;
            stop.IsDropTargetAfter = false;
        }

        targetStop.IsDropTargetBefore = !insertAfter;
        targetStop.IsDropTargetAfter = insertAfter;
    }

    private void ClearRouteDropIndicator()
    {
        if (DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        foreach (var stop in vm.RouteStops)
        {
            stop.IsDropTargetBefore = false;
            stop.IsDropTargetAfter = false;
        }
    }

    private static bool HasAncestorTag(DependencyObject? child, string tag)
    {
        while (child is not null)
        {
            if (child is FrameworkElement element &&
                string.Equals(element.Tag as string, tag, StringComparison.Ordinal))
            {
                return true;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return false;
    }

    private static string ResolveDeliveryShape(string? deliveryLabel)
    {
        var normalized = (deliveryLabel ?? string.Empty).Trim();
        if (normalized.Contains("Montage", StringComparison.OrdinalIgnoreCase))
        {
            return "triangle";
        }

        if (normalized.Contains("Verteilung", StringComparison.OrdinalIgnoreCase))
        {
            return "square";
        }

        return "circle";
    }

    private async Task<RoutePdfExportResult> ExportRoutePdfAsync(RouteExportSnapshot snapshot)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Tour als PDF exportieren",
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = BuildDefaultPdfFileName(snapshot),
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return RoutePdfExportResult.UserCancelled();
        }

        try
        {
            var mapImageBytes = await _routeExportService.CaptureMapImageAsync(snapshot);
            var mapImageBase64 = mapImageBytes is null || mapImageBytes.Length == 0
                ? null
                : Convert.ToBase64String(mapImageBytes);
            var html = TourPdfHtmlBuilder.Build(snapshot, mapImageBase64);
            await _routeExportService.ExportPdfAsync(html, dialog.FileName);
            return RoutePdfExportResult.Success($"Tour-PDF gespeichert: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            return RoutePdfExportResult.Failure($"Der PDF-Export ist fehlgeschlagen.\n{ex.Message}");
        }
    }

    private static string BuildDefaultPdfFileName(RouteExportSnapshot snapshot)
    {
        var name = string.IsNullOrWhiteSpace(snapshot.TourName) ? "Tour-Export" : snapshot.TourName.Trim();
        var date = string.IsNullOrWhiteSpace(snapshot.TourDate) ? DateTime.Today.ToString("yyyy-MM-dd") : snapshot.TourDate.Trim();
        var raw = $"{name}_{date}.pdf";
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(raw.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}

