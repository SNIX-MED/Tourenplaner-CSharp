using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        DetailsToggle = 1 << 6
    }

    private static readonly GridLength DefaultRoutePanelWidth = new(460d, GridUnitType.Pixel);
    private static readonly GridLength DefaultDetailsPanelWidth = new(390d, GridUnitType.Pixel);
    private static readonly GridLength HiddenDetailsPanelWidth = new(0d, GridUnitType.Pixel);
    private const int DataRefreshDebounceMilliseconds = 45;
    private const int UiRefreshDebounceMilliseconds = 12;
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
    private RouteStopItem? _routeGridDragItem;
    private int _mapRefreshRevision;
    private bool _isMapRefreshLoopRunning;
    private MapRefreshOperation _pendingMapRefresh;
    private readonly WebViewRouteExportService _routeExportService = new();

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
            QueueMapRefresh(MapRefreshOperation.MarkerSelection);
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
        else if (e.PropertyName == nameof(KarteSectionViewModel.ArePinInfoCardsVisible))
        {
            QueueMapRefresh(MapRefreshOperation.PinInfoCardsVisibility, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.PinInfoCardScale))
        {
            QueueMapRefresh(MapRefreshOperation.PinInfoCardScale, UiRefreshDebounceMilliseconds);
        }
        else if (e.PropertyName == nameof(KarteSectionViewModel.IsDetailsOpen) ||
                 e.PropertyName == nameof(KarteSectionViewModel.IsDetailsPanelExpanded) ||
                 e.PropertyName == nameof(KarteSectionViewModel.DetailsToggleGlyph))
        {
            QueueMapRefresh(MapRefreshOperation.DetailsToggle, UiRefreshDebounceMilliseconds);
        }
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

            if ((operation & MapRefreshOperation.MarkerSelection) != 0)
            {
                await HighlightSelectedMarkerAsync();
            }
        }

        if ((operation & MapRefreshOperation.Route) != 0)
        {
            await PushRouteToMapAsync();
        }
        else if ((operation & MapRefreshOperation.RouteSelection) != 0)
        {
            await HighlightSelectedRouteStopAsync();
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
            await MapWebView.EnsureCoreWebView2Async();
            MapWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            MapWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            MapWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            MapWebView.CoreWebView2.ContextMenuRequested += OnMapContextMenuRequested;
            MapWebView.NavigateToString(MapHtmlDocumentBuilder.Build());
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
            return;
        }

        try
        {
            var ready = await MapWebView.CoreWebView2.ExecuteScriptAsync(
                "(typeof window.gawelaSetMarkers === 'function' && typeof window.gawelaSetRoute === 'function' && typeof window.gawelaSetCompanyMarker === 'function').toString();");
            _mapScriptReady = ready.Contains("true", StringComparison.OrdinalIgnoreCase);
            if (_mapScriptReady)
            {
                QueueMapRefresh(
                    MapRefreshOperation.Markers |
                    MapRefreshOperation.Route |
                    MapRefreshOperation.PinInfoCardScale |
                    MapRefreshOperation.DetailsToggle);
            }
        }
        catch
        {
            _mapScriptReady = false;
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
            color = vm.ResolveOrderStatusColor(x.StatusLabel, x.IsAssigned),
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
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetCompanyMarker === 'function') window.gawelaSetCompanyMarker({JsonSerializer.Serialize(company)});");
        await ApplyPinInfoCardsVisibilityAsync();
        await ApplyPinInfoCardScaleAsync();
        await HighlightSelectedMarkerAsync();
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
                    color = vm.ResolveOrderStatusColor(visual.StatusLabel, visual.IsAssigned),
                    shape = ResolveDeliveryShape(visual.DeliveryLabel),
                    lat = r.Latitude,
                    lon = r.Longitude
                };
            })
            .ToList();
        var geometry = vm.GetRouteGeometrySnapshot()
            .Select(x => new { lat = x.Latitude, lon = x.Longitude })
            .ToList();

        var routeJson = JsonSerializer.Serialize(route);
        var geometryJson = JsonSerializer.Serialize(geometry);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaSetRoute === 'function') window.gawelaSetRoute({routeJson}, {geometryJson});");
        await HighlightSelectedRouteStopAsync();
    }

    private async Task HighlightSelectedMarkerAsync()
    {
        if (!_mapReady || !_mapScriptReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var id = vm.SelectedOrder?.OrderId ?? string.Empty;
        var jsonId = JsonSerializer.Serialize(id);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaHighlightMarker === 'function') window.gawelaHighlightMarker({jsonId});");
    }

    private async Task HighlightSelectedRouteStopAsync()
    {
        if (!_mapReady || !_mapScriptReady || DataContext is not KarteSectionViewModel vm || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        var id = vm.SelectedRouteStop?.OrderId ?? string.Empty;
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"if (typeof window.gawelaHighlightRouteStop === 'function') window.gawelaHighlightRouteStop({JsonSerializer.Serialize(id)});");
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

        var scaleJson = JsonSerializer.Serialize(vm.PinInfoCardScale);
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaSetPopupSizeMultiplier === 'function') window.gawelaSetPopupSizeMultiplier({scaleJson});");
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

        if (string.Equals(raw, "toggleDetails", StringComparison.OrdinalIgnoreCase))
        {
            if (vm.ToggleDetailsPanelCommand.CanExecute(null))
            {
                vm.ToggleDetailsPanelCommand.Execute(null);
            }

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

    private void RouteStopsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        if (RouteStopsList.SelectedItem is not RouteStopItem)
        {
            return;
        }

        vm.EditSelectedRouteStopStayMinutes();
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

    private void OnRouteStopEditStayMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            sender is not FrameworkElement element ||
            element.DataContext is not RouteStopItem stopItem ||
            stopItem.IsCompanyAnchor)
        {
            return;
        }

        vm.SelectRouteStopByOrderId(stopItem.OrderId);
        vm.EditSelectedRouteStopStayMinutes();
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

    private void RouteStopsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _routeGridDragStart = null;
        _routeGridDragItem = null;

        if (VisualTreeUtilities.FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var listItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listItem?.DataContext is not RouteStopItem item || item.IsCompanyAnchor)
        {
            return;
        }

        _routeGridDragStart = e.GetPosition(RouteStopsList);
        _routeGridDragItem = item;
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
        DragDrop.DoDragDrop(RouteStopsList, new DataObject(typeof(string), dragItem.OrderId), DragDropEffects.Move);
    }

    private void RouteStopsList_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            !e.Data.GetDataPresent(typeof(string)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourceOrderId = e.Data.GetData(typeof(string)) as string;
        var targetItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetStop = targetItem?.DataContext as RouteStopItem;
        if (string.IsNullOrWhiteSpace(sourceOrderId) ||
            targetStop is null ||
            targetStop.IsCompanyAnchor)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourceStop = vm.RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, sourceOrderId, StringComparison.OrdinalIgnoreCase));
        if (sourceStop is null ||
            sourceStop.IsCompanyAnchor ||
            string.Equals(sourceStop.OrderId, targetStop.OrderId, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void RouteStopsList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm ||
            !e.Data.GetDataPresent(typeof(string)))
        {
            return;
        }

        var sourceOrderId = e.Data.GetData(typeof(string)) as string;
        var targetItem = VisualTreeUtilities.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetStop = targetItem?.DataContext as RouteStopItem;
        if (string.IsNullOrWhiteSpace(sourceOrderId) || targetStop is null)
        {
            return;
        }

        var moved = vm.MoveRouteStopByOrderIds(sourceOrderId, targetStop.OrderId);
        if (moved && vm.SelectedRouteStop is not null)
        {
            RouteStopsList.SelectedItem = vm.SelectedRouteStop;
            RouteStopsList.ScrollIntoView(vm.SelectedRouteStop);
        }
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

