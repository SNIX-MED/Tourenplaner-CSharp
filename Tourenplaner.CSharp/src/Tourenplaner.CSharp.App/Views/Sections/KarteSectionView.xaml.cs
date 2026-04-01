using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class KarteSectionView : UserControl
{
    private static readonly GridLength DefaultRoutePanelWidth = new(460d, GridUnitType.Pixel);
    private bool _viewInitialized;
    private bool _mapReady;
    private bool _mapScriptReady;
    private INotifyPropertyChanged? _vmNotifier;
    private INotifyCollectionChanged? _ordersCollection;
    private INotifyCollectionChanged? _routeCollection;
    private bool _suppressSelectionSync;
    private Point? _routeGridDragStart;
    private RouteStopItem? _routeGridDragItem;
    private int _markersRefreshRevision;
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
            await PushMarkersToMapAsync();
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
        }

        _ = PushMarkersToMapAsync();
        _ = PushRouteToMapAsync();
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = RefreshMarkersDebouncedAsync();
    }

    private async void OnRouteCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_viewInitialized)
        {
            return;
        }

        try
        {
            await PushMarkersToMapAsync();
            await PushRouteToMapAsync();
        }
        catch
        {
            MapWebView.Visibility = Visibility.Collapsed;
            MapFallbackNotice.Visibility = Visibility.Visible;
        }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_viewInitialized)
        {
            return;
        }

        try
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
            else if (e.PropertyName == nameof(KarteSectionViewModel.DetailSelectedStatus) ||
                     e.PropertyName == nameof(KarteSectionViewModel.DetailOrderStatus))
            {
                await PushMarkersToMapAsync();
                await PushRouteToMapAsync();
            }
            else if (e.PropertyName == nameof(KarteSectionViewModel.DetailSelectedAvisoStatus) ||
                     e.PropertyName == nameof(KarteSectionViewModel.DetailAvisoStatus))
            {
                await PushMarkersToMapAsync();
                await PushRouteToMapAsync();
            }
            else if (e.PropertyName == nameof(KarteSectionViewModel.RouteGeometryPoints))
            {
                await PushRouteToMapAsync();
            }
            else if (e.PropertyName == nameof(KarteSectionViewModel.ArePinInfoCardsVisible))
            {
                await ApplyPinInfoCardsVisibilityAsync();
            }
            else if (e.PropertyName == nameof(KarteSectionViewModel.PinInfoCardScale))
            {
                await ApplyPinInfoCardScaleAsync();
            }
            else if (e.PropertyName == nameof(KarteSectionViewModel.IsDetailsOpen) ||
                     e.PropertyName == nameof(KarteSectionViewModel.IsDetailsPanelExpanded) ||
                     e.PropertyName == nameof(KarteSectionViewModel.DetailsToggleGlyph))
            {
                await ApplyDetailsToggleStateAsync();
            }
        }
        catch
        {
            MapWebView.Visibility = Visibility.Collapsed;
            MapFallbackNotice.Visibility = Visibility.Visible;
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

    private void OnMainSplitterMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RoutePanelColumn is null)
        {
            return;
        }

        RoutePanelColumn.Width = DefaultRoutePanelWidth;
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
            MapWebView.NavigateToString(BuildMapHtml());
            _mapReady = true;
            MapWebView.Visibility = Visibility.Visible;
            MapFallbackNotice.Visibility = Visibility.Collapsed;
            await ApplyDetailsToggleStateAsync();
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
                await PushMarkersToMapAsync();
                await PushRouteToMapAsync();
                await ApplyPinInfoCardScaleAsync();
                await ApplyDetailsToggleStateAsync();
            }
        }
        catch
        {
            _mapScriptReady = false;
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
            address = x.Address,
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

    private async Task RefreshMarkersDebouncedAsync()
    {
        if (!_viewInitialized)
        {
            return;
        }

        var revision = Interlocked.Increment(ref _markersRefreshRevision);
        try
        {
            await Task.Delay(45);
            if (revision != _markersRefreshRevision)
            {
                return;
            }

            await PushMarkersToMapAsync();
        }
        catch
        {
            MapWebView.Visibility = Visibility.Collapsed;
            MapFallbackNotice.Visibility = Visibility.Visible;
        }
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
        if (!_mapReady || !_mapScriptReady || MapWebView.CoreWebView2 is null || DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        var isVisible = vm.IsDetailsOpen && !vm.IsDetailsPanelExpanded;
        var glyph = vm.DetailsToggleGlyph;
        await MapWebView.CoreWebView2.ExecuteScriptAsync(
            $"if (typeof window.gawelaSetDetailsToggle === 'function') window.gawelaSetDetailsToggle({(isVisible ? "true" : "false")}, {JsonSerializer.Serialize(glyph)});");
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

    private void RouteStopsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _routeGridDragStart = null;
        _routeGridDragItem = null;

        if (FindVisualParent<ScrollBar>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var listItem = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
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
        var targetItem = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
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
        var targetItem = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
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

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
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
                   html, body, #map-shell, #map { height: 100%; margin: 0; padding: 0; }
                   html, body { overflow: hidden; background: transparent; }
                   body { font-family: Segoe UI, sans-serif; }
                   #map-shell {
                     position: relative;
                     width: 100%;
                     height: 100%;
                     overflow: hidden;
                     background: transparent;
                     clip-path: inset(0 round 14px);
                   }
                   #map {
                     position: absolute;
                     inset: 0;
                     background: transparent;
                   }
                   .leaflet-container { background: transparent; }
                   .leaflet-top,
                   .leaflet-bottom {
                     pointer-events: none;
                   }
                   .leaflet-top .leaflet-control,
                   .leaflet-bottom .leaflet-control {
                     pointer-events: auto;
                     margin: 14px;
                   }
                   .leaflet-control-zoom,
                   .leaflet-bar {
                     border-radius: 10px;
                     overflow: hidden;
                     border: 1px solid #D4DCE9;
                     box-shadow: 0 2px 8px rgba(15, 23, 42, 0.12);
                   }
                   .leaflet-bar a,
                   .leaflet-bar a:hover {
                     border-radius: 0;
                   }
                   .gawela-details-toggle {
                     position: absolute;
                     right: 10px;
                     top: 50%;
                     transform: translateY(-50%);
                     width: 22px;
                     height: 64px;
                     border-radius: 12px;
                     border: 1px solid #CBD5E1;
                     background: rgba(248, 250, 252, 0.96);
                     color: #64748B;
                     cursor: pointer;
                     display: none;
                     z-index: 650;
                     box-shadow: 0 4px 10px rgba(15, 23, 42, 0.12);
                     align-items: center;
                     justify-content: center;
                   }
                   .gawela-details-toggle svg {
                     width: 12px;
                     height: 12px;
                     stroke: currentColor;
                     stroke-width: 2.25;
                     fill: none;
                     stroke-linecap: round;
                     stroke-linejoin: round;
                     shape-rendering: geometricPrecision;
                     transition: transform 120ms ease;
                     transform-origin: center;
                   }
                   .gawela-details-toggle.is-right svg {
                     transform: rotate(180deg);
                   }
                   .gawela-details-toggle:hover {
                     background: #FFFFFF;
                     border-color: #94A3B8;
                     color: #334155;
                   }
                   .leaflet-popup.gawela-pin-popup {
                     transform-origin: center bottom;
                   }
                   .leaflet-popup.gawela-pin-popup .leaflet-popup-content-wrapper {
                     border-radius: 12px;
                     box-shadow: 0 14px 34px rgba(15, 23, 42, 0.22);
                   }
                   .leaflet-popup.gawela-pin-popup .leaflet-popup-content {
                     margin: var(--gawela-popup-margin-y, 10px) var(--gawela-popup-margin-x, 11px);
                     width: var(--gawela-popup-width, 192px);
                     min-width: var(--gawela-popup-width, 192px);
                     max-width: var(--gawela-popup-width, 192px);
                     font-size: var(--gawela-popup-font-size, 11px);
                     line-height: 1.3;
                   }
                   .leaflet-popup.gawela-pin-popup .gawela-popup-title {
                     font-weight: 700;
                     font-size: var(--gawela-popup-title-size, 14px);
                     margin-bottom: 2px;
                     color: #111827;
                   }
                   .leaflet-popup.gawela-pin-popup .gawela-popup-address {
                     margin-bottom: var(--gawela-popup-address-gap, 8px);
                     color: #334155;
                   }
                   .leaflet-popup.gawela-pin-popup .gawela-popup-action {
                     height: var(--gawela-popup-button-height, 24px);
                     padding: 0 var(--gawela-popup-button-pad-x, 8px);
                     border-radius: 6px;
                     border: 1px solid #9ca3af;
                     background: #f8fafc;
                     color: #111827;
                     cursor: pointer;
                     font-size: var(--gawela-popup-button-font-size, 11px);
                   }
                 </style>
               </head>
                <body>
                 <div id="map-shell"><div id="map"></div></div>
                 <button id="details-toggle" class="gawela-details-toggle" type="button" aria-label="Details einblenden">
                   <svg viewBox="0 0 12 12" aria-hidden="true" focusable="false">
                     <polyline points="8.5,1.8 3.8,6 8.5,10.2"></polyline>
                   </svg>
                 </button>
                 <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
                 <script>
                   const map = L.map('map').setView([47.3769, 8.5417], 10);
                   L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
                     maxZoom: 20,
                     subdomains: 'abcd',
                     attribution: '&copy; OpenStreetMap contributors &copy; CARTO'
                   }).addTo(map);

                   let markerMap = new Map();
                   let markerLayer = L.layerGroup().addTo(map);
                   let companyMarkerLayer = L.layerGroup().addTo(map);
                   let routeLayer = L.layerGroup().addTo(map);
                   let routePolyline = null;
                   let routeMarkerMap = new Map();
                   let keepAllMarkerPopupsOpen = false;
                   let hasInitialViewport = false;
                   let popupSizeMultiplier = 1.0;
                   const detailsToggle = document.getElementById('details-toggle');

                   if (detailsToggle) {
                     detailsToggle.addEventListener('click', () => {
                       if (window.chrome && window.chrome.webview) {
                         window.chrome.webview.postMessage('toggleDetails');
                       }
                     });
                   }

                   function clearMarkers() {
                     markerLayer.clearLayers();
                     markerMap.clear();
                   }

                   function updatePopupScale() {
                     const zoom = map.getZoom();
                     const baseScale = Math.max(0.18, Math.min(1.05, Math.pow(0.74, 11 - zoom)));
                     const effectiveScale = baseScale * popupSizeMultiplier;
                     const widthPx = Math.max(80, Math.round(192 * effectiveScale));
                     const marginY = Math.max(3, Math.round(10 * effectiveScale));
                     const marginX = Math.max(4, Math.round(11 * effectiveScale));
                     const bodyFont = Math.max(8, Math.round(11 * effectiveScale));
                     const titleFont = Math.max(9, Math.round(14 * effectiveScale));
                     const addressGap = Math.max(3, Math.round(8 * effectiveScale));
                     const buttonHeight = Math.max(14, Math.round(24 * effectiveScale));
                     const buttonPadX = Math.max(4, Math.round(8 * effectiveScale));
                     const buttonFont = Math.max(8, Math.round(11 * effectiveScale));
                     document.documentElement.style.setProperty('--gawela-popup-width', `${widthPx}px`);
                     document.documentElement.style.setProperty('--gawela-popup-margin-y', `${marginY}px`);
                     document.documentElement.style.setProperty('--gawela-popup-margin-x', `${marginX}px`);
                     document.documentElement.style.setProperty('--gawela-popup-font-size', `${bodyFont}px`);
                     document.documentElement.style.setProperty('--gawela-popup-title-size', `${titleFont}px`);
                     document.documentElement.style.setProperty('--gawela-popup-address-gap', `${addressGap}px`);
                     document.documentElement.style.setProperty('--gawela-popup-button-height', `${buttonHeight}px`);
                     document.documentElement.style.setProperty('--gawela-popup-button-pad-x', `${buttonPadX}px`);
                     document.documentElement.style.setProperty('--gawela-popup-button-font-size', `${buttonFont}px`);
                   }

                   function refreshOpenPopupLayouts() {
                     markerMap.forEach(marker => {
                       const popup = marker.getPopup();
                       if (popup && popup.isOpen()) {
                         enablePopupWheelZoom(popup);
                         popup.update();
                       }
                     });
                   }

                   function enablePopupWheelZoom(popup) {
                     if (!popup || !popup.getElement) {
                       return;
                     }
                     const popupElement = popup.getElement();
                     if (!popupElement) {
                       return;
                     }

                     // Leaflet blocks wheel propagation on popups by default.
                     // Remove that block so map zoom still works while hovering a popup.
                     L.DomEvent.off(popupElement, 'wheel', L.DomEvent.stopPropagation);
                     const wrapper = popupElement.querySelector('.leaflet-popup-content-wrapper');
                     if (wrapper) {
                       L.DomEvent.off(wrapper, 'wheel', L.DomEvent.stopPropagation);
                     }
                     const content = popupElement.querySelector('.leaflet-popup-content');
                     if (content) {
                       L.DomEvent.off(content, 'wheel', L.DomEvent.stopPropagation);
                     }
                   }

                   function schedulePopupUpdate(popup) {
                     if (!popup) {
                       return;
                     }
                     enablePopupWheelZoom(popup);
                     requestAnimationFrame(() => {
                       try {
                         popup.update();
                       } catch (_) {
                       }
                       requestAnimationFrame(() => {
                         try {
                           popup.update();
                         } catch (_) {
                         }
                       });
                     });
                     setTimeout(() => {
                       try {
                         popup.update();
                       } catch (_) {
                       }
                     }, 40);
                   }

                   function setAllMarkerPopupsVisible(show) {
                     keepAllMarkerPopupsOpen = !!show;
                     markerMap.forEach(marker => {
                       if (keepAllMarkerPopupsOpen) {
                         marker.openPopup();
                       } else {
                         marker.closePopup();
                       }
                     });
                     if (keepAllMarkerPopupsOpen) {
                       setTimeout(refreshOpenPopupLayouts, 0);
                     }
                   }

                   function clearCompanyMarker() {
                     companyMarkerLayer.clearLayers();
                   }

                   function clearRoute() {
                     routeLayer.clearLayers();
                     routePolyline = null;
                     routeMarkerMap.clear();
                   }

                   function buildOrderMarkerHtml(shape, color, avisoStatus, isAssigned, isDimmed) {
                     const stroke = '#1e293b';
                     const coreSize = 22;
                     const border = 2;
                     const shadow = '0 4px 12px rgba(15, 23, 42, 0.28)';
                     const normalizedAviso = (avisoStatus || '').trim().toLowerCase();
                     let badgeColor = '';
                     if (isAssigned) {
                       if (normalizedAviso === 'informiert') {
                         badgeColor = '#f59e0b';
                       } else if (normalizedAviso === 'bestätigt' || normalizedAviso === 'bestaetigt') {
                         badgeColor = '#16a34a';
                       } else {
                         badgeColor = '#64748b';
                       }
                     }

                     const dimmedStyle = isDimmed ? 'opacity:0.4;filter:saturate(0.15) grayscale(0.6);' : '';

                     let shapeHtml = '';
                     if (shape === 'square') {
                       shapeHtml = `<div style="width:${coreSize}px;height:${coreSize}px;background:${color};border:${border}px solid #fff;border-radius:7px;box-shadow:0 0 0 1px ${stroke},${shadow};box-sizing:border-box;${dimmedStyle}"></div>`;
                     } else if (shape === 'triangle') {
                       shapeHtml = `<div style="position:relative;width:24px;height:24px;filter:drop-shadow(0 3px 8px rgba(15, 23, 42, 0.28));${dimmedStyle}"><div style="position:absolute;left:0;top:0;width:0;height:0;border-left:12px solid transparent;border-right:12px solid transparent;border-bottom:22px solid #fff;"></div><div style="position:absolute;left:2px;top:3px;width:0;height:0;border-left:10px solid transparent;border-right:10px solid transparent;border-bottom:18px solid ${color};"></div><div style="position:absolute;left:0;top:0;width:0;height:0;border-left:12px solid transparent;border-right:12px solid transparent;border-bottom:22px solid transparent;filter:drop-shadow(0 0 0 ${stroke});"></div></div>`;
                     } else {
                       shapeHtml = `<div style="width:${coreSize}px;height:${coreSize}px;border-radius:50%;background:${color};border:${border}px solid #fff;box-shadow:0 0 0 1px ${stroke},${shadow};box-sizing:border-box;${dimmedStyle}"></div>`;
                     }

                     const badgeHtml = badgeColor
                       ? `<div style="position:absolute;top:-2px;right:-2px;width:9px;height:9px;border-radius:50%;background:${badgeColor};border:2px solid #fff;box-shadow:0 0 0 1px rgba(30,41,59,0.55);${dimmedStyle}"></div>`
                       : '';

                     return `<div style="position:relative;width:28px;height:28px;display:flex;align-items:center;justify-content:center;">${shapeHtml}${badgeHtml}</div>`;
                   }

                   function resolveMarkerColor(status) {
                     const normalized = (status || '').trim().toLowerCase();
                     if (normalized === 'bereits eingeplant') {
                       return '#64748b';
                     }
                     if (normalized === 'bestellt') {
                       return '#0ea5e9';
                     }
                     if (normalized === 'auf dem weg') {
                       return '#f59e0b';
                     }
                     if (normalized === 'an lager') {
                       return '#16a34a';
                     }
                     return '#a855f7';
                   }

                   function buildRouteStopMarkerHtml(shape, color, label, avisoStatus, isAssigned) {
                     const safeLabel = label || '?';
                     const labelTopOffset = shape === 'triangle' ? '4px' : '0';
                     return `<div style="position:relative;width:28px;height:28px;display:flex;align-items:center;justify-content:center;">${buildOrderMarkerHtml(shape, color, avisoStatus, isAssigned, false)}<div style="position:absolute;inset:0;top:${labelTopOffset};display:flex;align-items:center;justify-content:center;color:#fff;font-size:11px;font-weight:700;line-height:1;text-shadow:0 1px 2px rgba(15,23,42,0.85);pointer-events:none;">${safeLabel}</div></div>`;
                   }

                   function buildPopupAddressHtml(address) {
                     const raw = (address || '').trim();
                     if (!raw) {
                       return '';
                     }
                     const commaIndex = raw.indexOf(',');
                     if (commaIndex < 0) {
                       return raw;
                     }
                     const street = raw.substring(0, commaIndex).trim();
                     const postalAndCity = raw.substring(commaIndex + 1).trim();
                     if (!street) {
                       return postalAndCity;
                     }
                     if (!postalAndCity) {
                       return street;
                     }
                     return `${street}<br/>${postalAndCity}`;
                   }

                   window.gawelaSetMarkers = function(markers) {
                     clearMarkers();
                     updatePopupScale();
                     if (!markers || markers.length === 0) {
                       return;
                     }
                     const bounds = [];
                     markers.forEach(m => {
                       const color = m.color || '#A855F7';
                       const icon = L.divIcon({
                         className: 'gawela-marker',
                         html: buildOrderMarkerHtml(m.shape, color, m.avisoStatus, m.isAssigned, m.isDimmed),
                         iconSize: [28, 28],
                         iconAnchor: [14, 14]
                       });
                       const marker = L.marker([m.lat, m.lon], { icon });
                       const title = (m.customer && m.customer.trim().length > 0)
                         ? `${m.customer} (${m.id})`
                         : (m.id || '');
                       const addressHtml = buildPopupAddressHtml(m.address);
                       marker.bindPopup(
                         `<div class="gawela-popup-title">${title}</div><div class="gawela-popup-address">${addressHtml}</div><button class="gawela-popup-action" onclick="window.gawelaAddToRoute('${m.id}')">Add to route</button>`,
                         {
                           className: 'gawela-pin-popup',
                           autoClose: false,
                           closeOnClick: false,
                           autoPan: false,
                           minWidth: 192,
                           maxWidth: 192
                         }
                       );
                       marker.on('click', () => {
                         if (window.chrome && window.chrome.webview) {
                           window.chrome.webview.postMessage(m.id);
                         }
                       });
                       marker.addTo(markerLayer);
                       markerMap.set(m.id, marker);
                       if (keepAllMarkerPopupsOpen) {
                         marker.openPopup();
                       }
                       bounds.push([m.lat, m.lon]);
                     });
                     if (!hasInitialViewport && bounds.length > 0) {
                       map.fitBounds(bounds, { padding: [24, 24] });
                       hasInitialViewport = true;
                     }
                   };

                   window.gawelaHighlightMarker = function(orderId) {
                     if (!orderId || !markerMap.has(orderId)) {
                       return;
                     }
                     const marker = markerMap.get(orderId);
                     marker.openPopup();
                     const popup = marker.getPopup();
                     schedulePopupUpdate(popup);
                     map.panTo(marker.getLatLng());
                     map.once('moveend', () => schedulePopupUpdate(popup));
                   };

                   window.gawelaSetAllMarkerPopupsVisible = function(show) {
                     setAllMarkerPopupsVisible(show);
                   };

                   window.gawelaSetPopupSizeMultiplier = function(multiplier) {
                     const parsed = Number(multiplier);
                     if (Number.isFinite(parsed)) {
                       popupSizeMultiplier = Math.max(0.7, Math.min(1.8, parsed));
                     } else {
                       popupSizeMultiplier = 1.0;
                     }
                     updatePopupScale();
                     refreshOpenPopupLayouts();
                   };

                   window.gawelaSetDetailsToggle = function(show, glyph) {
                     if (!detailsToggle) {
                       return;
                     }

                     detailsToggle.style.display = show ? 'flex' : 'none';
                     const isRight = (glyph || '<').trim() === '>';
                     detailsToggle.classList.toggle('is-right', isRight);
                   };

                   window.gawelaSetCompanyMarker = function(company) {
                     clearCompanyMarker();
                     if (!company || typeof company.lat !== 'number' || typeof company.lon !== 'number') {
                       return;
                     }

                     const houseIcon = L.divIcon({
                       className: 'gawela-company-marker',
                       html: `<div style="width:28px;height:28px;border-radius:14px;background:#16a34a;color:#fff;font-size:16px;line-height:28px;text-align:center;border:2px solid #fff;box-shadow:0 0 0 1px #166534;">🏠</div>`,
                       iconSize: [28, 28],
                       iconAnchor: [14, 14]
                     });

                     const marker = L.marker([company.lat, company.lon], { icon: houseIcon })
                       .addTo(companyMarkerLayer)
                       .bindPopup(`<b>${company.name || 'Firma'}</b><br/>${company.address || ''}`);

                     if (!hasInitialViewport && markerMap.size === 0) {
                       map.setView([company.lat, company.lon], 12);
                       hasInitialViewport = true;
                       return;
                     }

                     if (!hasInitialViewport) {
                       const points = [];
                       markerMap.forEach(m => {
                         const p = m.getLatLng();
                         points.push([p.lat, p.lng]);
                       });
                       points.push([company.lat, company.lon]);
                       map.fitBounds(points, { padding: [24, 24] });
                       hasInitialViewport = true;
                     }
                   };

                   window.gawelaSetRoute = function(routeStops, geometryPoints) {
                     clearRoute();
                     if (!routeStops || routeStops.length === 0) {
                       return;
                     }
                     const toAlphaLabel = function(position) {
                       let n = Number(position);
                       if (!Number.isFinite(n) || n < 1) return '?';
                       n = Math.floor(n);
                       let label = '';
                       while (n > 0) {
                         const rem = (n - 1) % 26;
                         label = String.fromCharCode(65 + rem) + label;
                         n = Math.floor((n - 1) / 26);
                       }
                       return label;
                     };
                     const path = (geometryPoints && geometryPoints.length > 1)
                       ? geometryPoints.map(x => [x.lat, x.lon])
                       : routeStops.map(x => [x.lat, x.lon]);
                     routePolyline = L.polyline(path, { color: '#2563eb', weight: 4, opacity: 0.9 }).addTo(routeLayer);

                     routeStops.forEach(stop => {
                       const stopLabel = toAlphaLabel(stop.position);
                       const icon = L.divIcon({
                         className: 'gawela-route-stop',
                         html: buildRouteStopMarkerHtml(stop.shape || 'circle', stop.color || '#2563eb', stopLabel, stop.avisoStatus, stop.isAssigned),
                         iconSize: [28, 28],
                         iconAnchor: [14, 14]
                       });

                       const routeMarker = L.marker([stop.lat, stop.lon], { icon, draggable: true })
                         .addTo(routeLayer)
                         .bindPopup(`Route stop ${stopLabel}<br/>Order: ${stop.id}`);

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

                   map.on('zoom', updatePopupScale);
                   map.on('zoomend', () => {
                     updatePopupScale();
                     refreshOpenPopupLayouts();
                   });
                   map.on('popupopen', e => {
                     if (!e || !e.popup) {
                       return;
                     }
                     enablePopupWheelZoom(e.popup);
                     if (keepAllMarkerPopupsOpen) {
                       return;
                     }
                     schedulePopupUpdate(e.popup);
                   });
                   updatePopupScale();
                 </script>
               </body>
               </html>
               """;
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
