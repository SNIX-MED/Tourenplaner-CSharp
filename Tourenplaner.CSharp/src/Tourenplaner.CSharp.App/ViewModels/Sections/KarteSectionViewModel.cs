using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class KarteSectionViewModel : SectionViewModelBase
{
    private const string CompanyStartStopId = "__company_start__";
    private const string CompanyEndStopId = "__company_end__";
    private const string PlannedTourStatus = "Eingeplant";
    private static readonly IReadOnlyList<string> _orderStatusOptions =
    [
        "nicht festgelegt",
        "Bestellt",
        "Auf dem Weg",
        "An Lager"
    ];
    private readonly JsonOrderRepository _orderRepository;
    private readonly JsonToursRepository _tourRepository;
    private readonly JsonEmployeesRepository _employeeRepository;
    private readonly JsonVehicleDataRepository _vehicleRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly RouteOptimizationService _optimizationService;
    private readonly MapRouteService _mapRouteService;
    private readonly OsrmRoutingService _osrmRoutingService;
    private readonly List<Order> _allOrders = new();
    private readonly List<TourRecord> _savedTours = new();
    private readonly List<GeoPoint> _routeGeometryPoints = new();
    private readonly List<OsrmRouteLeg> _routeLegs = new();
    private readonly List<RouteStopItem> _timedStops = new();
    private static readonly IReadOnlyList<string> _filterOptions = ["Alle", "Nur offen", "Nur zugewiesen"];

    private string _searchText = string.Empty;
    private string _selectedFilter = "Alle";
    private MapOrderItem? _selectedOrder;
    private RouteStopItem? _selectedRouteStop;
    private string _routeName = "Neue Karte-Tour";
    private string _routeDate = DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy");
    private string _routeStartHour = "08";
    private string _routeStartMinute = "00";
    private double _routeDistanceKm;
    private string _statusText = "Loading map orders...";
    private string _routeTimingSummary = "Noch keine Stopps geplant.";
    private string _driveTimesText = "Noch keine Stopps geplant.";
    private string _avisoEmailSubjectTemplate = AppSettings.DefaultAvisoEmailSubjectTemplate;
    private string _companyName = "Firma";
    private string _companyAddress = string.Empty;
    private string _statusColorNotSpecified = AppSettings.DefaultStatusColorNotSpecified;
    private string _statusColorOrdered = AppSettings.DefaultStatusColorOrdered;
    private string _statusColorOnTheWay = AppSettings.DefaultStatusColorOnTheWay;
    private string _statusColorInStock = AppSettings.DefaultStatusColorInStock;
    private string _statusColorPlanned = AppSettings.DefaultStatusColorPlanned;
    private GeoPoint? _companyLocation;
    private bool _isDetailsOpen;
    private bool _isDetailsPanelExpanded = true;
    private bool _savedTourSelectionSync;
    private bool _suppressDetailStatusSave;
    private bool _suppressRouteChangeTracking;
    private bool _hasUnsavedRouteChanges;
    private int _routeGeometryRevision;
    private int _activeTourId;
    private SavedTourLookupItem? _selectedSavedTour;
    private string _detailSelectedStatus = "nicht festgelegt";

    public KarteSectionViewModel(string ordersJsonPath, string toursJsonPath, string settingsJsonPath)
        : base("Karte", "Map order review, marker filters, route panel and save-to-tour workflow.")
    {
        _orderRepository = new JsonOrderRepository(ordersJsonPath);
        _tourRepository = new JsonToursRepository(toursJsonPath);
        var dataRoot = Path.GetDirectoryName(settingsJsonPath) ?? string.Empty;
        _employeeRepository = new JsonEmployeesRepository(Path.Combine(dataRoot, "employees.json"));
        _vehicleRepository = new JsonVehicleDataRepository(Path.Combine(dataRoot, "vehicles.json"));
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _optimizationService = new RouteOptimizationService();
        _mapRouteService = new MapRouteService();
        _osrmRoutingService = new OsrmRoutingService();

        RefreshCommand = new AsyncCommand(RefreshAsync);
        AddToRouteCommand = new DelegateCommand(AddSelectedOrderToRoute, () => SelectedOrder is not null);
        RemoveOrderFromTourCommand = new AsyncCommand(RemoveSelectedOrderFromTourAsync, CanRemoveSelectedOrderFromTour);
        RemoveFromRouteCommand = new DelegateCommand(RemoveSelectedRouteStop, () => SelectedRouteStop is not null && !IsCompanyStop(SelectedRouteStop));
        MoveStopUpCommand = new DelegateCommand(MoveSelectedStopUp, () => CanMoveSelectedStop(-1));
        MoveStopDownCommand = new DelegateCommand(MoveSelectedStopDown, () => CanMoveSelectedStop(1));
        OptimizeRouteCommand = new DelegateCommand(OptimizeRoute, () => RouteStops.Count(x => !IsCompanyStop(x)) > 2);
        OpenCreateTourDialogCommand = new AsyncCommand(OpenCreateTourDialogAsync);
        EditSelectedTourCommand = new AsyncCommand(OpenEditSelectedTourDialogAsync, CanEditOrLeaveSelectedTour);
        ExportRouteCommand = new DelegateCommand(ExportRouteToGoogleMaps, CanExportRoute);
        SaveRouteAsTourCommand = new AsyncCommand(SaveRouteAsTourAsync, () => RouteStops.Any(x => !IsCompanyStop(x)));
        SaveCurrentTourCommand = new AsyncCommand(SaveCurrentTourAsync, CanSaveCurrentTour);
        ClearRouteCommand = new DelegateCommand(ClearRoute, () => RouteStops.Any(x => !IsCompanyStop(x)));
        LeaveSelectedTourCommand = new DelegateCommand(LeaveSelectedTour, CanEditOrLeaveSelectedTour);
        ApplyStartTimeCommand = new DelegateCommand(ApplyRouteStartTime);
        ToggleDetailsPanelCommand = new AsyncCommand(ToggleDetailsPanelAsync);
        CloseDetailsCommand = new DelegateCommand(CloseDetails, () => SelectedOrder is not null);
        SendEmailCommand = new DelegateCommand(SendEmailToSelectedOrder, () => SelectedOrder is not null);
        EditOrderCommand = new AsyncCommand(EditSelectedOrderAsync, () => SelectedOrder is not null);

        EnsureCompanyAnchors();
        _ = RefreshAsync();
    }

    public ObservableCollection<MapOrderItem> MapOrders { get; } = new();

    public ObservableCollection<RouteStopItem> RouteStops { get; } = new();

    public ObservableCollection<SavedTourLookupItem> SavedTours { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand AddToRouteCommand { get; }

    public ICommand RemoveOrderFromTourCommand { get; }

    public ICommand RemoveFromRouteCommand { get; }

    public ICommand MoveStopUpCommand { get; }

    public ICommand MoveStopDownCommand { get; }

    public ICommand OptimizeRouteCommand { get; }

    public ICommand OpenCreateTourDialogCommand { get; }

    public ICommand EditSelectedTourCommand { get; }

    public ICommand ExportRouteCommand { get; }

    public ICommand SaveRouteAsTourCommand { get; }

    public ICommand SaveCurrentTourCommand { get; }

    public ICommand ClearRouteCommand { get; }

    public ICommand LeaveSelectedTourCommand { get; }

    public ICommand ApplyStartTimeCommand { get; }

    public ICommand ToggleDetailsPanelCommand { get; }

    public ICommand CloseDetailsCommand { get; }

    public ICommand SendEmailCommand { get; }

    public ICommand EditOrderCommand { get; }

    public IReadOnlyList<string> FilterOptions => _filterOptions;

    public SavedTourLookupItem? SelectedSavedTour
    {
        get => _selectedSavedTour;
        set
        {
            if (SetProperty(ref _selectedSavedTour, value) && !_savedTourSelectionSync)
            {
                _ = LoadSelectedSavedTourAsync();
                if (LeaveSelectedTourCommand is DelegateCommand leave)
                {
                    leave.RaiseCanExecuteChanged();
                }

                if (EditSelectedTourCommand is AsyncCommand editTour)
                {
                    editTour.RaiseCanExecuteChanged();
                }
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildOrderGrid();
            }
        }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                RebuildOrderGrid();
            }
        }
    }

    public string RouteName
    {
        get => _routeName;
        set
        {
            if (SetProperty(ref _routeName, value))
            {
                MarkRouteChanged();
            }
        }
    }

    public string RouteDate
    {
        get => _routeDate;
        set
        {
            if (SetProperty(ref _routeDate, value))
            {
                MarkRouteChanged();
            }
        }
    }

    public string RouteStartHour
    {
        get => _routeStartHour;
        set
        {
            if (SetProperty(ref _routeStartHour, value))
            {
                MarkRouteChanged();
            }
        }
    }

    public string RouteStartMinute
    {
        get => _routeStartMinute;
        set
        {
            if (SetProperty(ref _routeStartMinute, value))
            {
                MarkRouteChanged();
            }
        }
    }

    public double RouteDistanceKm
    {
        get => _routeDistanceKm;
        private set => SetProperty(ref _routeDistanceKm, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string RouteTimingSummary
    {
        get => _routeTimingSummary;
        private set => SetProperty(ref _routeTimingSummary, value);
    }

    public string DriveTimesText
    {
        get => _driveTimesText;
        private set => SetProperty(ref _driveTimesText, value);
    }

    public IReadOnlyList<string> OrderStatusOptions => _orderStatusOptions;

    public string DetailSelectedStatus
    {
        get => _detailSelectedStatus;
        set
        {
            if (!SetProperty(ref _detailSelectedStatus, value))
            {
                return;
            }

            if (_suppressDetailStatusSave)
            {
                return;
            }

            _ = UpdateSelectedOrderStatusAsync(value);
        }
    }

    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        private set => SetProperty(ref _isDetailsOpen, value);
    }

    public bool IsDetailsPanelExpanded
    {
        get => _isDetailsPanelExpanded;
        private set
        {
            if (SetProperty(ref _isDetailsPanelExpanded, value))
            {
                OnPropertyChanged(nameof(DetailsToggleGlyph));
            }
        }
    }

    public string DetailsToggleGlyph => IsDetailsPanelExpanded ? ">" : "<";

    public IReadOnlyList<GeoPoint> RouteGeometryPoints => _routeGeometryPoints;

    public MapOrderItem? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
            {
                IsDetailsOpen = value is not null;
                _suppressDetailStatusSave = true;
                try
                {
                    DetailSelectedStatus = value is null ? _orderStatusOptions[0] : DetailOrderStatus;
                }
                finally
                {
                    _suppressDetailStatusSave = false;
                }
                OnPropertyChanged(nameof(DetailAddress));
                OnPropertyChanged(nameof(DetailCustomer));
                OnPropertyChanged(nameof(DetailOrderNumber));
                OnPropertyChanged(nameof(DetailOrderStatus));
                OnPropertyChanged(nameof(DetailTourStatus));
                OnPropertyChanged(nameof(DetailEmail));
                OnPropertyChanged(nameof(DetailPhone));
                OnPropertyChanged(nameof(DetailDeliveryType));
                RaiseCommandStates();
            }
        }
    }

    public RouteStopItem? SelectedRouteStop
    {
        get => _selectedRouteStop;
        set
        {
            if (SetProperty(ref _selectedRouteStop, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        var settingsTask = _settingsRepository.LoadAsync();
        var ordersTask = _orderRepository.GetAllAsync();
        await Task.WhenAll(settingsTask, ordersTask);

        var settings = await settingsTask;
        _avisoEmailSubjectTemplate = string.IsNullOrWhiteSpace(settings.AvisoEmailSubjectTemplate)
            ? AppSettings.DefaultAvisoEmailSubjectTemplate
            : settings.AvisoEmailSubjectTemplate.Trim();
        _companyName = string.IsNullOrWhiteSpace(settings.CompanyName) ? "Firma" : settings.CompanyName.Trim();
        _companyAddress = BuildCompanyAddressText(settings.CompanyStreet, settings.CompanyPostalCode, settings.CompanyCity);
        _statusColorNotSpecified = NormalizeStatusColor(settings.StatusColorNotSpecified, AppSettings.DefaultStatusColorNotSpecified);
        _statusColorOrdered = NormalizeStatusColor(settings.StatusColorOrdered, AppSettings.DefaultStatusColorOrdered);
        _statusColorOnTheWay = NormalizeStatusColor(settings.StatusColorOnTheWay, AppSettings.DefaultStatusColorOnTheWay);
        _statusColorInStock = NormalizeStatusColor(settings.StatusColorInStock, AppSettings.DefaultStatusColorInStock);
        _statusColorPlanned = NormalizeStatusColor(settings.StatusColorPlanned, AppSettings.DefaultStatusColorPlanned);
        IsDetailsPanelExpanded = settings.MapDetailsPanelExpanded;
        _companyLocation = await AddressGeocodingService.TryGeocodeAddressAsync(
            settings.CompanyStreet,
            settings.CompanyPostalCode,
            settings.CompanyCity,
            _companyAddress);
        EnsureCompanyAnchors();
        OnPropertyChanged(nameof(CompanyMarker));
        _allOrders.Clear();
        _allOrders.AddRange(await ordersTask);

        if (await BackfillMissingLocationsAsync())
        {
            await _orderRepository.SaveAllAsync(_allOrders);
        }

        RebuildOrderGrid();
        await LoadSavedToursAsync(0);
        _ = RebuildRouteGeometryAsync();
    }

    public string DetailAddress => SelectedOrder?.Address ?? "Keine Auswahl";
    public string DetailCustomer => SelectedOrder?.Customer ?? "Keine Auswahl";
    public string DetailOrderNumber => SelectedOrder?.OrderId ?? "n/a";
    public string DetailOrderStatus => FindSelectedOrderModel()?.OrderStatus ?? SelectedOrder?.StatusLabel ?? "nicht festgelegt";
    public string DetailTourStatus => SelectedOrder?.TourStatusLabel ?? "Offen";
    public string DetailEmail => FindSelectedOrderModel()?.Email ?? "n/a";
    public string DetailPhone => FindSelectedOrderModel()?.Phone ?? "n/a";
    public string DetailDeliveryType => FindSelectedOrderModel()?.DeliveryType ?? SelectedOrder?.DeliveryLabel ?? "Frei Bordsteinkante";

    public CompanyMarkerInfo? CompanyMarker =>
        _companyLocation is null
            ? null
            : new CompanyMarkerInfo(_companyName, _companyAddress, _companyLocation.Latitude, _companyLocation.Longitude);

    public void AddOrderToRouteById(string orderId)
    {
        var match = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        SelectedOrder = match;
        AddSelectedOrderToRoute();
    }

    public IReadOnlyList<RouteStopItem> GetRouteSnapshot()
    {
        return RouteStops
            .Where(x => !IsCompanyStop(x))
            .Select(x => new RouteStopItem
        {
            Position = x.Position,
            OrderId = x.OrderId,
            Customer = x.Customer,
            Address = x.Address,
            Latitude = x.Latitude,
            Longitude = x.Longitude
        })
            .ToList();
    }

    public IReadOnlyList<GeoPoint> GetRouteGeometrySnapshot()
    {
        return _routeGeometryPoints.ToList();
    }

    public void SelectRouteStopByOrderId(string orderId)
    {
        var match = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedRouteStop = match;
            SelectOrderDetailsById(orderId);
        }
    }

    public async Task FocusTourAsync(int tourId)
    {
        await LoadSavedToursAsync(tourId);
        await LoadTourIntoRouteAsync(tourId);
    }

    public async Task FocusAndEditTourAsync(int tourId)
    {
        await FocusTourAsync(tourId);

        if (EditSelectedTourCommand?.CanExecute(null) == true)
        {
            EditSelectedTourCommand.Execute(null);
        }
    }

    public bool MoveRouteStopByOrderIds(string sourceOrderId, string targetOrderId)
    {
        if (string.IsNullOrWhiteSpace(sourceOrderId) || string.IsNullOrWhiteSpace(targetOrderId))
        {
            return false;
        }

        if (string.Equals(sourceOrderId, targetOrderId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, sourceOrderId, StringComparison.OrdinalIgnoreCase));
        var target = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, targetOrderId, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null || IsCompanyStop(source) || IsCompanyStop(target))
        {
            return false;
        }

        var sourceIndex = RouteStops.IndexOf(source);
        var targetIndex = RouteStops.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return false;
        }

        RouteStops.Move(sourceIndex, targetIndex);
        SelectedRouteStop = source;
        RebuildPositions();
        MarkRouteChanged();
        return true;
    }

    public void SwapRouteStops(string sourceOrderId, string targetOrderId)
    {
        var swapped = _mapRouteService.SwapStops(ToMapRouteStops(), sourceOrderId, targetOrderId);
        ApplyRouteStops(swapped, sourceOrderId);
    }

    public void UpdateRouteStopCoordinates(string orderId, double lat, double lon)
    {
        var match = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        match.Latitude = lat;
        match.Longitude = lon;
        RebuildPositions();
        MarkRouteChanged();
    }

    public void EditSelectedRouteStopStayMinutes()
    {
        if (SelectedRouteStop is null || IsCompanyStop(SelectedRouteStop))
        {
            return;
        }

        var dialog = new RouteStopStayMinutesDialogWindow(SelectedRouteStop.PlannedStayMinutes)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.StayMinutes is null)
        {
            return;
        }

        SelectedRouteStop.PlannedStayMinutes = dialog.StayMinutes.Value;
        RefreshDriveTimesFromCurrentRoute();
        MarkRouteChanged();
        StatusText = $"Aufenthaltszeit für Auftrag {SelectedRouteStop.OrderId} gesetzt: {SelectedRouteStop.PlannedStayMinutes} min.";
    }

    private void RebuildOrderGrid()
    {
        var previousSelectedId = SelectedOrder?.OrderId;
        var routeOrderIds = RouteStops.Select(s => s.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = (_searchText ?? string.Empty).Trim();
        IEnumerable<Order> filtered = _allOrders
            .Where(o => o.Type == OrderType.Map && o.Location is not null)
            .Where(o => !routeOrderIds.Contains(o.Id));

        if (string.Equals(_selectedFilter, "Nur offen", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(o => string.IsNullOrWhiteSpace(o.AssignedTourId));
        }
        else if (string.Equals(_selectedFilter, "Nur zugewiesen", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(o => !string.IsNullOrWhiteSpace(o.AssignedTourId));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(o =>
                o.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (o.AssignedTourId ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        MapOrders.Clear();
        foreach (var order in filtered.OrderBy(o => o.ScheduledDate).ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            MapOrders.Add(BuildMapOrderItem(order));
        }

        if (string.IsNullOrWhiteSpace(previousSelectedId))
        {
            SelectedOrder = null;
        }
        else
        {
            SelectOrderDetailsById(previousSelectedId);
        }
        UpdateStatus();
        RaiseCommandStates();
    }

    private void AddSelectedOrderToRoute()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var existing = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, SelectedOrder.OrderId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedRouteStop = existing;
            return;
        }

        var item = new RouteStopItem
        {
            Position = 0,
            OrderId = SelectedOrder.OrderId,
            Customer = SelectedOrder.Customer,
            Address = SelectedOrder.Address,
            Latitude = SelectedOrder.Latitude,
            Longitude = SelectedOrder.Longitude,
            PlannedStayMinutes = 10
        };

        var endIndex = RouteStops
            .Select((stop, index) => new { stop, index })
            .FirstOrDefault(x => IsCompanyStop(x.stop) && string.Equals(x.stop.OrderId, CompanyEndStopId, StringComparison.OrdinalIgnoreCase))
            ?.index ?? RouteStops.Count;

        RouteStops.Insert(endIndex, item);

        RebuildPositions();
        RebuildOrderGrid();
        MarkRouteChanged();
    }

    private void RemoveSelectedRouteStop()
    {
        if (SelectedRouteStop is null)
        {
            return;
        }

        if (IsCompanyStop(SelectedRouteStop))
        {
            return;
        }

        RouteStops.Remove(SelectedRouteStop);
        SelectedRouteStop = RouteStops.FirstOrDefault(x => !IsCompanyStop(x));
        RebuildPositions();
        RebuildOrderGrid();
        MarkRouteChanged();
    }

    private async Task RemoveSelectedOrderFromTourAsync()
    {
        var order = FindSelectedOrderModel();
        if (order is null || string.IsNullOrWhiteSpace(order.AssignedTourId))
        {
            return;
        }

        var selectedOrderId = order.Id;
        var tourKey = order.AssignedTourId.Trim();
        var tours = (await _tourRepository.LoadAsync()).ToList();
        var tour = tours.FirstOrDefault(x => string.Equals(x.Id.ToString(CultureInfo.InvariantCulture), tourKey, StringComparison.OrdinalIgnoreCase));
        if (tour is not null)
        {
            tour.Stops = (tour.Stops ?? [])
                .Where(x => !string.Equals(x.Auftragsnummer, selectedOrderId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (var i = 0; i < tour.Stops.Count; i++)
            {
                tour.Stops[i].Order = i + 1;
            }

            await _tourRepository.SaveAsync(tours);
        }

        order.AssignedTourId = string.Empty;
        await _orderRepository.SaveAllAsync(_allOrders);

        var currentTourId = ResolveCurrentTourId();
        await RefreshAsync();
        if (tour is not null && currentTourId == tour.Id)
        {
            await FocusTourAsync(tour.Id);
        }

        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, selectedOrderId, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Auftrag {selectedOrderId} wurde aus der Tour entfernt.";
    }

    private void MoveSelectedStopUp()
    {
        MoveSelectedStop(-1);
    }

    private void MoveSelectedStopDown()
    {
        MoveSelectedStop(1);
    }

    private void MoveSelectedStop(int delta)
    {
        if (SelectedRouteStop is null)
        {
            return;
        }

        var moved = _mapRouteService.MoveStop(ToMapRouteStops(), SelectedRouteStop.OrderId, delta);
        ApplyRouteStops(moved, SelectedRouteStop.OrderId);
    }

    private bool CanMoveSelectedStop(int delta)
    {
        if (SelectedRouteStop is null)
        {
            return false;
        }

        if (IsCompanyStop(SelectedRouteStop))
        {
            return false;
        }

        var index = RouteStops.IndexOf(SelectedRouteStop);
        var newIndex = index + delta;
        if (index < 0 || newIndex < 0 || newIndex >= RouteStops.Count)
        {
            return false;
        }

        return !IsCompanyStop(RouteStops[newIndex]);
    }

    private void OptimizeRoute()
    {
        var movableStops = RouteStops.Where(x => !IsCompanyStop(x)).ToList();
        if (movableStops.Count < 3)
        {
            return;
        }

        var optimized = _optimizationService.OptimizeNearestNeighbor(
            movableStops,
            x => x.Latitude,
            x => x.Longitude);

        var start = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
        var end = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyEndStopId, StringComparison.OrdinalIgnoreCase));

        RouteStops.Clear();
        if (start is not null)
        {
            RouteStops.Add(start);
        }

        foreach (var stop in optimized)
        {
            RouteStops.Add(stop);
        }

        if (end is not null)
        {
            RouteStops.Add(end);
        }

        RebuildPositions();
        MarkRouteChanged();
        StatusText = "Route optimized (nearest-neighbor).";
    }

    private async Task SaveRouteAsTourAsync()
    {
        await SaveRouteAsTourAsync(
            routeName: RouteName,
            routeDate: RouteDate,
            startTime: RouteStartTime,
            vehicleId: null,
            trailerId: null,
            employeeIds: []);
    }

    private async Task SaveCurrentTourAsync()
    {
        if (!CanSaveCurrentTour())
        {
            return;
        }

        var selectedTourId = ResolveCurrentTourId();
        if (selectedTourId > 0)
        {
            var tour = _savedTours.FirstOrDefault(x => x.Id == selectedTourId);
            if (tour is null)
            {
                await LoadSavedToursAsync(selectedTourId);
                tour = _savedTours.FirstOrDefault(x => x.Id == selectedTourId);
            }

            if (tour is null)
            {
                System.Windows.MessageBox.Show(
                    "Die ausgewaehlte Tour wurde nicht gefunden. Bitte als neue Tour speichern.",
                    "Tour speichern",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                await OpenCreateTourDialogAsync();
                return;
            }

            await UpdateExistingTourAsync(
                selectedTourId,
                RouteName,
                RouteDate,
                RouteStartTime,
                tour.VehicleId,
                tour.TrailerId,
                tour.EmployeeIds ?? []);
            return;
        }

        await OpenCreateTourDialogAsync();
    }

    private async Task OpenCreateTourDialogAsync()
    {
        var hasRouteStops = RouteStops.Any(x => !IsCompanyStop(x));
        var (employees, vehicles, trailers) = await LoadTourDialogOptionsAsync();

        var dialog = new CreateTourDialogWindow(
            RouteDate,
            RouteName,
            RouteStartHour,
            RouteStartMinute,
            vehicles,
            trailers,
            employees)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        if (!hasRouteStops)
        {
            System.Windows.MessageBox.Show(
                "Bitte zuerst mindestens einen Auftrag zur Route hinzufügen.",
                "Neue Tour",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var result = dialog.Result;
        await SaveRouteAsTourAsync(
            result.RouteName,
            result.RouteDate,
            result.StartTime,
            result.VehicleId,
            result.TrailerId,
            result.EmployeeIds);
    }

    private async Task OpenEditSelectedTourDialogAsync()
    {
        var selectedTourId = ResolveCurrentTourId();
        if (selectedTourId <= 0)
        {
            System.Windows.MessageBox.Show(
                "Bitte zuerst eine gespeicherte Tour auswaehlen oder auf der Karte laden.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var tour = _savedTours.FirstOrDefault(x => x.Id == selectedTourId);
        if (tour is null)
        {
            await LoadSavedToursAsync(selectedTourId);
            tour = _savedTours.FirstOrDefault(x => x.Id == selectedTourId);
        }

        if (tour is null)
        {
            System.Windows.MessageBox.Show(
                "Die ausgewaehlte Tour wurde nicht gefunden.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var (editHour, editMinute) = ParseStartTimeParts(tour.StartTime);
        var (employees, vehicles, trailers) = await LoadTourDialogOptionsAsync();

        var dialog = new CreateTourDialogWindow(
            routeDate: string.IsNullOrWhiteSpace(tour.Date) ? RouteDate : tour.Date.Trim(),
            routeName: string.IsNullOrWhiteSpace(tour.Name) ? $"Tour {tour.Id}" : tour.Name.Trim(),
            routeStartHour: editHour,
            routeStartMinute: editMinute,
            vehicleOptions: vehicles,
            trailerOptions: trailers,
            employeeOptions: employees,
            selectedVehicleId: tour.VehicleId,
            selectedTrailerId: tour.TrailerId,
            selectedEmployeeIds: tour.EmployeeIds)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var result = dialog.Result;
        await UpdateExistingTourAsync(
            selectedTourId,
            result.RouteName,
            result.RouteDate,
            result.StartTime,
            result.VehicleId,
            result.TrailerId,
            result.EmployeeIds);
    }

    private async Task<(List<TourEmployeeOption> Employees, List<TourLookupOption> Vehicles, List<TourLookupOption> Trailers)> LoadTourDialogOptionsAsync()
    {
        var employeeTask = _employeeRepository.LoadAsync();
        var vehicleTask = _vehicleRepository.LoadAsync();
        await Task.WhenAll(employeeTask, vehicleTask);

        var employees = (await employeeTask)
            .Where(x => x.Active)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourEmployeeOption(x.Id, x.DisplayName))
            .ToList();

        var vehicleData = await vehicleTask;
        var vehicles = vehicleData.Vehicles
            .Where(x => x.Active)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourLookupOption(x.Id, $"{x.Name} [{x.LicensePlate}]"))
            .ToList();
        var trailers = vehicleData.Trailers
            .Where(x => x.Active)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourLookupOption(x.Id, $"{x.Name} [{x.LicensePlate}]"))
            .ToList();

        return (employees, vehicles, trailers);
    }

    private static (string Hour, string Minute) ParseStartTimeParts(string? startTime)
    {
        var value = (startTime ?? string.Empty).Trim();
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return (parsed.Hour.ToString("00", CultureInfo.InvariantCulture), parsed.Minute.ToString("00", CultureInfo.InvariantCulture));
        }

        return ("08", "00");
    }

    private async Task SaveRouteAsTourAsync(
        string routeName,
        string routeDate,
        string startTime,
        string? vehicleId,
        string? trailerId,
        IReadOnlyList<string> employeeIds)
    {
        if (!RouteStops.Any(x => !IsCompanyStop(x)))
        {
            return;
        }

        var tours = (await _tourRepository.LoadAsync()).ToList();
        var nextId = _mapRouteService.DetermineNextTourId(tours);
        var tour = _mapRouteService.BuildTour(
            ToMapRouteStops(),
            nextId,
            routeName,
            routeDate,
            startTime,
            _companyName,
            _companyAddress,
            _companyLocation,
            defaultServiceMinutes: 10);
        tour.VehicleId = string.IsNullOrWhiteSpace(vehicleId) ? null : vehicleId.Trim();
        tour.TrailerId = string.IsNullOrWhiteSpace(trailerId) ? null : trailerId.Trim();
        tour.EmployeeIds = (employeeIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        tours.Add(tour);
        await _tourRepository.SaveAsync(tours);

        var routeOrderIds = _mapRouteService.ExtractRouteOrderIds(ToMapRouteStops());
        foreach (var order in _allOrders.Where(o => routeOrderIds.Contains(o.Id)))
        {
            order.AssignedTourId = nextId.ToString();
        }

        await _orderRepository.SaveAllAsync(_allOrders);
        await RefreshAsync();
        await FocusTourAsync(nextId);
        SetRouteChanged(false);
        StatusText = "Route gespeichert und auf Karte geladen.";
    }

    private async Task UpdateExistingTourAsync(
        int tourId,
        string routeName,
        string routeDate,
        string startTime,
        string? vehicleId,
        string? trailerId,
        IReadOnlyList<string> employeeIds)
    {
        if (!RouteStops.Any(x => !IsCompanyStop(x)))
        {
            System.Windows.MessageBox.Show(
                "Bitte zuerst mindestens einen Auftrag zur Route hinzufuegen.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var tours = (await _tourRepository.LoadAsync()).ToList();
        var index = tours.FindIndex(x => x.Id == tourId);
        if (index < 0)
        {
            System.Windows.MessageBox.Show(
                "Die Tour konnte nicht mehr gefunden werden.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            await RefreshAsync();
            return;
        }

        var updated = _mapRouteService.BuildTour(
            ToMapRouteStops(),
            tourId,
            routeName,
            routeDate,
            startTime,
            _companyName,
            _companyAddress,
            _companyLocation,
            defaultServiceMinutes: 10);
        updated.VehicleId = string.IsNullOrWhiteSpace(vehicleId) ? null : vehicleId.Trim();
        updated.TrailerId = string.IsNullOrWhiteSpace(trailerId) ? null : trailerId.Trim();
        updated.EmployeeIds = (employeeIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        tours[index] = updated;
        await _tourRepository.SaveAsync(tours);

        var tourKey = tourId.ToString(CultureInfo.InvariantCulture);
        foreach (var order in _allOrders.Where(o => string.Equals(o.AssignedTourId, tourKey, StringComparison.OrdinalIgnoreCase)))
        {
            order.AssignedTourId = string.Empty;
        }

        var routeOrderIds = _mapRouteService.ExtractRouteOrderIds(ToMapRouteStops());
        foreach (var order in _allOrders.Where(o => routeOrderIds.Contains(o.Id)))
        {
            order.AssignedTourId = tourKey;
        }

        await _orderRepository.SaveAllAsync(_allOrders);
        await RefreshAsync();
        await FocusTourAsync(tourId);
        SetRouteChanged(false);
        StatusText = "Tour aktualisiert und auf Karte geladen.";
    }

    private async Task LoadSavedToursAsync(int? preferredTourId = null)
    {
        _savedTours.Clear();
        _savedTours.AddRange((await _tourRepository.LoadAsync())
            .OrderByDescending(t => ParseDateForSort(t.Date))
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase));

        SavedTours.Clear();
        SavedTours.Add(new SavedTourLookupItem
        {
            TourId = 0,
            Label = "Tour wählen"
        });

        foreach (var tour in _savedTours)
        {
            SavedTours.Add(new SavedTourLookupItem
            {
                TourId = tour.Id,
                Label = BuildTourLookupLabel(tour)
            });
        }

        var keepId = preferredTourId.GetValueOrDefault();
        var selected = SavedTours.FirstOrDefault(x => x.TourId == keepId) ?? SavedTours.FirstOrDefault();
        _savedTourSelectionSync = true;
        try
        {
            SelectedSavedTour = selected;
        }
        finally
        {
            _savedTourSelectionSync = false;
        }

        RaiseCommandStates();
    }

    private async Task LoadSelectedSavedTourAsync()
    {
        var targetTourId = SelectedSavedTour?.TourId ?? 0;
        if (targetTourId <= 0)
        {
            RaiseCommandStates();
            return;
        }

        await LoadTourIntoRouteAsync(targetTourId);
    }

    private async Task LoadTourIntoRouteAsync(int tourId)
    {
        var tour = _savedTours.FirstOrDefault(x => x.Id == tourId);
        if (tour is null)
        {
            await LoadSavedToursAsync(tourId);
            tour = _savedTours.FirstOrDefault(x => x.Id == tourId);
        }

        if (tour is null)
        {
            return;
        }

        _activeTourId = tour.Id;
        _suppressRouteChangeTracking = true;
        try
        {
            RouteName = string.IsNullOrWhiteSpace(tour.Name) ? $"Tour {tour.Id}" : tour.Name.Trim();
            RouteDate = (tour.Date ?? string.Empty).Trim();
            var startTime = (tour.StartTime ?? string.Empty).Trim();
            if (TimeOnly.TryParseExact(startTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
            {
                RouteStartHour = parsedTime.Hour.ToString("00", CultureInfo.InvariantCulture);
                RouteStartMinute = parsedTime.Minute.ToString("00", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _suppressRouteChangeTracking = false;
        }

        ApplyTourStopsToRoute(tour);
        SetRouteChanged(false);
        StatusText = "Tour auf Karte geladen.";
        RaiseCommandStates();
    }

    private void ApplyTourStopsToRoute(TourRecord tour)
    {
        _suppressRouteChangeTracking = true;
        try
        {
            EnsureCompanyAnchors();

            var start = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
            var end = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyEndStopId, StringComparison.OrdinalIgnoreCase));
            var middle = (tour.Stops ?? [])
                .OrderBy(x => x.Order)
                .Where(x => !IsCompanyTourStop(x))
                .Select(stop => new RouteStopItem
                {
                    OrderId = ExtractTourStopOrderId(stop),
                    Customer = NormalizeTourStopName(stop.Name),
                    Address = stop.Address ?? string.Empty,
                    Latitude = stop.Lat ?? double.NaN,
                    Longitude = stop.Lng ?? stop.Lon ?? double.NaN,
                    IsCompanyAnchor = false,
                    PlannedStayMinutes = Math.Max(0, stop.ServiceMinutes)
                })
                .ToList();

            RouteStops.Clear();
            if (start is not null)
            {
                RouteStops.Add(start);
            }

            foreach (var item in middle)
            {
                RouteStops.Add(item);
            }

            if (end is not null)
            {
                RouteStops.Add(end);
            }

            RebuildPositions();
            RebuildOrderGrid();
        }
        finally
        {
            _suppressRouteChangeTracking = false;
        }
    }

    private static string ExtractTourStopOrderId(TourStopRecord stop)
    {
        var number = (stop.Auftragsnummer ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(number))
        {
            return number;
        }

        var id = (stop.Id ?? string.Empty).Trim();
        if (id.StartsWith("auftrag:", StringComparison.OrdinalIgnoreCase))
        {
            return id["auftrag:".Length..];
        }

        return id;
    }

    private static bool IsCompanyTourStop(TourStopRecord stop)
    {
        return TourStopIdentity.IsCompanyStop(stop);
    }

    private static string NormalizeTourStopName(string? value)
    {
        return TourStopIdentity.NormalizeCompanyStopDisplayName(value);
    }

    private static string BuildTourLookupLabel(TourRecord tour)
    {
        var name = string.IsNullOrWhiteSpace(tour.Name) ? "Tour" : tour.Name.Trim();
        return name;
    }

    private static DateTime ParseDateForSort(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        var formats = new[] { "dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy" };
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.Date;
            }
        }

        return DateTime.MinValue;
    }

    private bool CanExportRoute()
    {
        return GetExportPoints().Count >= 2;
    }

    private void ExportRouteToGoogleMaps()
    {
        var points = GetExportPoints();
        if (points.Count < 2)
        {
            System.Windows.MessageBox.Show(
                "Für den Export werden mindestens zwei Stopps mit Koordinaten benötigt.",
                "Route exportieren",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var origin = $"{points[0].Latitude.ToString(CultureInfo.InvariantCulture)},{points[0].Longitude.ToString(CultureInfo.InvariantCulture)}";
        var destination = $"{points[^1].Latitude.ToString(CultureInfo.InvariantCulture)},{points[^1].Longitude.ToString(CultureInfo.InvariantCulture)}";

        var url = $"https://www.google.com/maps/dir/?api=1&origin={Uri.EscapeDataString(origin)}&destination={Uri.EscapeDataString(destination)}&travelmode=driving";

        if (points.Count > 2)
        {
            var waypointsValue = string.Join(
                "|",
                points.Skip(1).Take(points.Count - 2).Select(x =>
                    $"{x.Latitude.ToString(CultureInfo.InvariantCulture)},{x.Longitude.ToString(CultureInfo.InvariantCulture)}"));
            if (!string.IsNullOrWhiteSpace(waypointsValue))
            {
                url += $"&waypoints={Uri.EscapeDataString(waypointsValue)}";
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            StatusText = "Route in Google Maps geöffnet.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Google Maps konnte nicht geöffnet werden.\n{ex.Message}",
                "Route exportieren",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private List<GeoPoint> GetExportPoints()
    {
        return RouteStops
            .Where(x => !double.IsNaN(x.Latitude) && !double.IsNaN(x.Longitude))
            .Where(x => x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180)
            .Select(x => new GeoPoint(x.Latitude, x.Longitude))
            .ToList();
    }

    private IReadOnlyList<MapRouteStop> ToMapRouteStops()
    {
        return RouteStops
            .Where(x => !IsCompanyStop(x))
            .Select(x => new MapRouteStop(x.Position, x.OrderId, x.Customer, x.Address, x.Latitude, x.Longitude, x.PlannedStayMinutes))
            .ToList();
    }

    private void ApplyRouteStops(IReadOnlyList<MapRouteStop> routeStops, string? selectedOrderId = null)
    {
        _suppressRouteChangeTracking = true;
        try
        {
            var start = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
            var end = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyEndStopId, StringComparison.OrdinalIgnoreCase));

            RouteStops.Clear();
            if (start is not null)
            {
                RouteStops.Add(start);
            }

            foreach (var stop in routeStops)
            {
                RouteStops.Add(new RouteStopItem
                {
                    Position = stop.Position,
                    OrderId = stop.OrderId,
                    Customer = stop.Customer,
                    Address = stop.Address,
                    Latitude = stop.Latitude,
                    Longitude = stop.Longitude,
                    IsCompanyAnchor = false,
                    PlannedStayMinutes = stop.ServiceMinutes < 0 ? 10 : stop.ServiceMinutes
                });
            }

            if (end is not null)
            {
                RouteStops.Add(end);
            }

            RebuildPositions();
            if (!string.IsNullOrWhiteSpace(selectedOrderId))
            {
                SelectRouteStopByOrderId(selectedOrderId);
            }
        }
        finally
        {
            _suppressRouteChangeTracking = false;
        }

        MarkRouteChanged();
    }

    private void ClearRoute()
    {
        _activeTourId = 0;
        _suppressRouteChangeTracking = true;
        ResetRouteToCompanyAnchors();
        SelectedRouteStop = RouteStops.FirstOrDefault(x => !x.IsCompanyAnchor);
        SelectedRouteStop = RouteStops.FirstOrDefault(x => !IsCompanyStop(x));
        _savedTourSelectionSync = true;
        try
        {
            SelectedSavedTour = SavedTours.FirstOrDefault();
        }
        finally
        {
            _savedTourSelectionSync = false;
            _suppressRouteChangeTracking = false;
        }

        RebuildPositions();
        RebuildOrderGrid();
        SetRouteChanged(false);
    }

    private void ResetRouteToCompanyAnchors()
    {
        RouteStops.Clear();
        RouteStops.Add(new RouteStopItem
        {
            OrderId = CompanyStartStopId,
            Customer = string.Empty,
            Address = _companyName,
            Latitude = _companyLocation?.Latitude ?? double.NaN,
            Longitude = _companyLocation?.Longitude ?? double.NaN,
            IsCompanyAnchor = true,
            PlannedStayMinutes = 0
        });
        RouteStops.Add(new RouteStopItem
        {
            OrderId = CompanyEndStopId,
            Customer = string.Empty,
            Address = _companyName,
            Latitude = _companyLocation?.Latitude ?? double.NaN,
            Longitude = _companyLocation?.Longitude ?? double.NaN,
            IsCompanyAnchor = true,
            PlannedStayMinutes = 0
        });
    }

    private void LeaveSelectedTour()
    {
        if (ResolveCurrentTourId() <= 0)
        {
            return;
        }

        ClearRoute();
        StatusText = "Tour verlassen.";
    }

    private void RebuildPositions()
    {
        EnsureCompanyAnchorOrdering();
        for (var i = 0; i < RouteStops.Count; i++)
        {
            RouteStops[i].Position = i + 1;
        }

        foreach (var stop in RouteStops)
        {
            stop.EtaText = string.Empty;
        }

        OnPropertyChanged(nameof(RouteStops));
        var distancePoints = RouteStops
            .Where(x => !double.IsNaN(x.Latitude) && !double.IsNaN(x.Longitude))
            .ToList();
        RouteDistanceKm = _optimizationService.ComputeTotalDistanceKm(distancePoints, x => x.Latitude, x => x.Longitude);
        if (RouteStops.Count(x => !IsCompanyStop(x)) == 0)
        {
            ClearDriveTimes();
        }
        else
        {
            RouteTimingSummary = "Fahrzeiten werden berechnet...";
            DriveTimesText = "Fahrzeiten werden berechnet...";
        }
        _ = RebuildRouteGeometryAsync();
        UpdateStatus();
        RaiseCommandStates();
    }

    private void UpdateStatus()
    {
        var routeStopCount = RouteStops.Count(x => !IsCompanyStop(x));
        StatusText = $"Map orders: {MapOrders.Count} | Route stops: {routeStopCount} | Route distance: {RouteDistanceKm:0.##} km";
    }

    private void RaiseCommandStates()
    {
        if (AddToRouteCommand is DelegateCommand add)
        {
            add.RaiseCanExecuteChanged();
        }

        if (RemoveOrderFromTourCommand is AsyncCommand removeFromTour)
        {
            removeFromTour.RaiseCanExecuteChanged();
        }

        if (RemoveFromRouteCommand is DelegateCommand remove)
        {
            remove.RaiseCanExecuteChanged();
        }

        if (MoveStopUpCommand is DelegateCommand up)
        {
            up.RaiseCanExecuteChanged();
        }

        if (MoveStopDownCommand is DelegateCommand down)
        {
            down.RaiseCanExecuteChanged();
        }

        if (OptimizeRouteCommand is DelegateCommand optimize)
        {
            optimize.RaiseCanExecuteChanged();
        }

        if (OpenCreateTourDialogCommand is AsyncCommand openCreateTour)
        {
            openCreateTour.RaiseCanExecuteChanged();
        }

        if (EditSelectedTourCommand is AsyncCommand editTour)
        {
            editTour.RaiseCanExecuteChanged();
        }

        if (ExportRouteCommand is DelegateCommand exportRoute)
        {
            exportRoute.RaiseCanExecuteChanged();
        }

        if (SaveRouteAsTourCommand is AsyncCommand save)
        {
            save.RaiseCanExecuteChanged();
        }

        if (SaveCurrentTourCommand is AsyncCommand saveCurrent)
        {
            saveCurrent.RaiseCanExecuteChanged();
        }

        if (ClearRouteCommand is DelegateCommand clear)
        {
            clear.RaiseCanExecuteChanged();
        }

        if (LeaveSelectedTourCommand is DelegateCommand leave)
        {
            leave.RaiseCanExecuteChanged();
        }

        if (CloseDetailsCommand is DelegateCommand closeDetails)
        {
            closeDetails.RaiseCanExecuteChanged();
        }

        if (SendEmailCommand is DelegateCommand sendMail)
        {
            sendMail.RaiseCanExecuteChanged();
        }

        if (EditOrderCommand is AsyncCommand editOrder)
        {
            editOrder.RaiseCanExecuteChanged();
        }
    }

    private int ResolveCurrentTourId()
    {
        var selectedTourId = SelectedSavedTour?.TourId ?? 0;
        if (selectedTourId > 0)
        {
            return selectedTourId;
        }

        return _activeTourId > 0 ? _activeTourId : 0;
    }

    private bool CanEditOrLeaveSelectedTour()
    {
        return ResolveCurrentTourId() > 0;
    }

    private bool CanSaveCurrentTour()
    {
        return _hasUnsavedRouteChanges && RouteStops.Any(x => !IsCompanyStop(x));
    }

    private bool CanRemoveSelectedOrderFromTour()
    {
        return !string.IsNullOrWhiteSpace(FindSelectedOrderModel()?.AssignedTourId);
    }

    private string RouteStartTime => $"{NormalizeTimePart(RouteStartHour, 23)}:{NormalizeTimePart(RouteStartMinute, 59)}";

    private void ApplyRouteStartTime()
    {
        var normalizedHour = NormalizeTimePart(RouteStartHour, 23);
        var normalizedMinute = NormalizeTimePart(RouteStartMinute, 59);
        var hasChange = !string.Equals(RouteStartHour, normalizedHour, StringComparison.Ordinal) ||
                        !string.Equals(RouteStartMinute, normalizedMinute, StringComparison.Ordinal);
        RouteStartHour = normalizedHour;
        RouteStartMinute = normalizedMinute;
        RefreshDriveTimesFromCurrentRoute();
        UpdateStatus();
        if (hasChange)
        {
            MarkRouteChanged();
        }
    }

    private void MarkRouteChanged()
    {
        if (_suppressRouteChangeTracking)
        {
            return;
        }

        SetRouteChanged(true);
    }

    private void SetRouteChanged(bool value)
    {
        if (_hasUnsavedRouteChanges == value)
        {
            return;
        }

        _hasUnsavedRouteChanges = value;
        RaiseCommandStates();
    }

    private async Task ToggleDetailsPanelAsync()
    {
        IsDetailsPanelExpanded = !IsDetailsPanelExpanded;
        await SaveDetailsPanelStateAsync();
    }

    private void CloseDetails()
    {
        SelectedOrder = null;
    }

    private void SendEmailToSelectedOrder()
    {
        var order = FindSelectedOrderModel();
        if (order is null || string.IsNullOrWhiteSpace(order.Email))
        {
            System.Windows.MessageBox.Show(
                "Für diesen Auftrag ist keine E-Mail-Adresse hinterlegt.",
                "E-Mail senden",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var subject = BuildAvisoEmailSubject(order.Id);
        var uri = $"mailto:{Uri.EscapeDataString(order.Email.Trim())}?subject={Uri.EscapeDataString(subject)}";

        try
        {
            Process.Start(new ProcessStartInfo(uri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Das E-Mail-Programm konnte nicht geöffnet werden.\n{ex.Message}",
                "E-Mail senden",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task EditSelectedOrderAsync()
    {
        var selected = FindSelectedOrderModel();
        if (selected is null)
        {
            return;
        }

        var originalId = selected.Id;
        var dialog = new ManualOrderDialogWindow(selected)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var updated = dialog.CreatedOrder;
        updated.Type = OrderType.Map;
        updated.AssignedTourId = selected.AssignedTourId;
        updated.Location = await AddressGeocodingService.TryGeocodeOrderAsync(updated) ?? selected.Location;

        _allOrders.RemoveAll(x => string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase));
        _allOrders.RemoveAll(x => !string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(updated);

        await _orderRepository.SaveAllAsync(_allOrders);
        await RefreshAsync();
        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, updated.Id, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Auftrag {updated.Id} wurde aktualisiert.";
    }

    private Order? FindSelectedOrderModel()
    {
        if (SelectedOrder is null)
        {
            return null;
        }

        return _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.OrderId, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectOrderDetailsById(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            SelectedOrder = null;
            return;
        }

        var match = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedOrder = match;
            return;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase));
        SelectedOrder = order is null ? null : BuildMapOrderItem(order);
    }

    private async Task UpdateSelectedOrderStatusAsync(string? nextStatus)
    {
        var order = FindSelectedOrderModel();
        if (order is null)
        {
            return;
        }

        var normalizedStatus = NormalizeOrderStatus(nextStatus);
        if (string.Equals(NormalizeOrderStatus(order.OrderStatus), normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        order.OrderStatus = normalizedStatus;
        await _orderRepository.SaveAllAsync(_allOrders);

        var selectedOrderId = order.Id;
        await RefreshAsync();
        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, selectedOrderId, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Status für Auftrag {selectedOrderId} gespeichert.";
    }

    private string BuildAvisoEmailSubject(string orderId)
    {
        var template = string.IsNullOrWhiteSpace(_avisoEmailSubjectTemplate)
            ? AppSettings.DefaultAvisoEmailSubjectTemplate
            : _avisoEmailSubjectTemplate;

        if (template.Contains("X", StringComparison.Ordinal))
        {
            return template.Replace("X", orderId, StringComparison.Ordinal);
        }

        return $"{template} {orderId}".Trim();
    }

    private static string BuildCompanyAddressText(string? street, string? postalCode, string? city)
    {
        var s = (street ?? string.Empty).Trim();
        var p = (postalCode ?? string.Empty).Trim();
        var c = (city ?? string.Empty).Trim();
        return string.Join(", ", new[]
        {
            s,
            string.Join(' ', new[] { p, c }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private void EnsureCompanyAnchors()
    {
        var movable = RouteStops.Where(x => !IsCompanyStop(x)).ToList();
        RouteStops.Clear();

        RouteStops.Add(new RouteStopItem
        {
            OrderId = CompanyStartStopId,
            Customer = string.Empty,
            Address = _companyName,
            Latitude = _companyLocation?.Latitude ?? double.NaN,
            Longitude = _companyLocation?.Longitude ?? double.NaN,
            IsCompanyAnchor = true,
            PlannedStayMinutes = 0
        });

        foreach (var stop in movable)
        {
            stop.IsCompanyAnchor = false;
            RouteStops.Add(stop);
        }

        RouteStops.Add(new RouteStopItem
        {
            OrderId = CompanyEndStopId,
            Customer = string.Empty,
            Address = _companyName,
            Latitude = _companyLocation?.Latitude ?? double.NaN,
            Longitude = _companyLocation?.Longitude ?? double.NaN,
            IsCompanyAnchor = true,
            PlannedStayMinutes = 0
        });
    }

    private void EnsureCompanyAnchorOrdering()
    {
        var start = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
        var end = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyEndStopId, StringComparison.OrdinalIgnoreCase));
        if (start is null || end is null)
        {
            return;
        }

        var middle = RouteStops
            .Where(x => !IsCompanyStop(x))
            .ToList();

        RouteStops.Clear();
        RouteStops.Add(start);
        foreach (var stop in middle)
        {
            RouteStops.Add(stop);
        }

        RouteStops.Add(end);
    }

    private static bool IsCompanyStop(RouteStopItem stop)
    {
        return stop.IsCompanyAnchor || IsCompanyOrderId(stop.OrderId);
    }

    private static bool IsCompanyOrderId(string? orderId)
    {
        var id = (orderId ?? string.Empty).Trim();
        return string.Equals(id, CompanyStartStopId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, CompanyEndStopId, StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("__company_", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTimePart(string? value, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            parsed = 0;
        }

        parsed = Math.Clamp(parsed, 0, max);
        return parsed.ToString("00");
    }

    private async Task RebuildRouteGeometryAsync()
    {
        var revision = Interlocked.Increment(ref _routeGeometryRevision);
        var validStops = RouteStops
            .Where(x => !double.IsNaN(x.Latitude) && !double.IsNaN(x.Longitude))
            .Where(x => x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180)
            .ToList();
        var waypoints = validStops.Select(x => new GeoPoint(x.Latitude, x.Longitude)).ToList();

        IReadOnlyList<GeoPoint> geometry = Array.Empty<GeoPoint>();
        IReadOnlyList<OsrmRouteLeg> legs = Array.Empty<OsrmRouteLeg>();
        if (waypoints.Count >= 2)
        {
            var result = await _osrmRoutingService.TryBuildRouteWithLegsAsync(waypoints);
            geometry = result.GeometryPoints;
            legs = result.Legs;
            if (geometry.Count < 2 || legs.Count != waypoints.Count - 1)
            {
                geometry = waypoints;
                legs = BuildEstimatedLegs(waypoints);
            }
        }

        if (revision != _routeGeometryRevision)
        {
            return;
        }

        _routeGeometryPoints.Clear();
        _routeGeometryPoints.AddRange(geometry);
        _routeLegs.Clear();
        _routeLegs.AddRange(legs);
        _timedStops.Clear();
        _timedStops.AddRange(validStops);
        OnPropertyChanged(nameof(RouteGeometryPoints));
        RefreshDriveTimesFromCurrentRoute();
    }

    private void RefreshDriveTimesFromCurrentRoute()
    {
        foreach (var stop in RouteStops)
        {
            stop.EtaText = string.Empty;
        }

        if (_timedStops.Count < 2 || _routeLegs.Count == 0)
        {
            if (RouteStops.Count(x => !IsCompanyStop(x)) == 0)
            {
                ClearDriveTimes();
            }

            return;
        }

        var start = BuildStartDateTime();
        var current = start;
        var totalDriveMinutes = 0;
        var totalStayMinutes = 0;
        var sb = new StringBuilder();

        for (var i = 0; i < _routeLegs.Count && i + 1 < _timedStops.Count; i++)
        {
            var fromStop = _timedStops[i];
            var toStop = _timedStops[i + 1];
            var leg = _routeLegs[i];

            var depart = current;
            current = current.AddMinutes(leg.DurationMinutes);
            var arrive = current;
            totalDriveMinutes += leg.DurationMinutes;

            sb.AppendLine($"{BuildStopLabel(fromStop, isFrom: true)} -> {BuildStopLabel(toStop, isFrom: false)}");
            sb.AppendLine($"{leg.DurationMinutes} min | {leg.DistanceKm:0.0} km");
            sb.AppendLine($"{depart:HH:mm} -> {arrive:HH:mm}");
            if (i < _routeLegs.Count - 1)
            {
                sb.AppendLine();
            }

            if (!IsCompanyStop(toStop))
            {
                toStop.EtaText = arrive.ToString("HH:mm");
                totalStayMinutes += Math.Max(0, toStop.PlannedStayMinutes);
                current = current.AddMinutes(Math.Max(0, toStop.PlannedStayMinutes));
            }
        }

        var end = start.AddMinutes(totalDriveMinutes + totalStayMinutes);
        RouteTimingSummary = $"Start: {start:HH:mm} | Fahrt: {totalDriveMinutes} min | Aufenthalt: {totalStayMinutes} min | Warten: 0 min | Ende: {end:HH:mm}";
        DriveTimesText = sb.Length == 0 ? "Noch keine Stopps geplant." : sb.ToString().TrimEnd();
    }

    private void ClearDriveTimes()
    {
        RouteTimingSummary = "Noch keine Stopps geplant.";
        DriveTimesText = "Noch keine Stopps geplant.";
    }

    private DateTime BuildStartDateTime()
    {
        var hour = 8;
        var minute = 0;
        _ = int.TryParse(RouteStartHour, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour);
        _ = int.TryParse(RouteStartMinute, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute);
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        return DateTime.Today.AddHours(hour).AddMinutes(minute);
    }

    private static IReadOnlyList<OsrmRouteLeg> BuildEstimatedLegs(IReadOnlyList<GeoPoint> waypoints)
    {
        if (waypoints.Count < 2)
        {
            return Array.Empty<OsrmRouteLeg>();
        }

        var legs = new List<OsrmRouteLeg>();
        for (var i = 0; i < waypoints.Count - 1; i++)
        {
            var a = waypoints[i];
            var b = waypoints[i + 1];
            var distanceKm = ComputeDistanceKm(a, b);
            var durationMinutes = Math.Max(1, (int)Math.Round(distanceKm / 55d * 60d, MidpointRounding.AwayFromZero));
            legs.Add(new OsrmRouteLeg(durationMinutes, distanceKm));
        }

        return legs;
    }

    private static double ComputeDistanceKm(GeoPoint a, GeoPoint b)
    {
        const double earthRadiusKm = 6371d;
        var dLat = DegreesToRadians(b.Latitude - a.Latitude);
        var dLon = DegreesToRadians(b.Longitude - a.Longitude);
        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);
        var h =
            Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) +
            Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1d - h));
        return earthRadiusKm * c;
    }

    private static double DegreesToRadians(double value)
    {
        return value * Math.PI / 180d;
    }

    private string BuildStopLabel(RouteStopItem stop, bool isFrom)
    {
        if (IsCompanyStop(stop))
        {
            var company = string.IsNullOrWhiteSpace(_companyName) ? "Firma" : _companyName.Trim();
            return isFrom ? $"Start ({company})" : company;
        }

        return !string.IsNullOrWhiteSpace(stop.Customer) ? stop.Customer : stop.Address;
    }

    private async Task<bool> BackfillMissingLocationsAsync()
    {
        var changed = false;
        var candidates = _allOrders
            .Where(x => x.Type == OrderType.Map &&
                        (x.Location is null || AddressGeocodingService.IsLikelyCountryCentroid(x.Location)))
            .ToList();

        foreach (var order in candidates)
        {
            var location = await AddressGeocodingService.TryGeocodeOrderAsync(order);
            if (location is null)
            {
                continue;
            }

            order.Location = location;
            changed = true;
        }

        return changed;
    }

    private async Task SaveDetailsPanelStateAsync()
    {
        var settings = await _settingsRepository.LoadAsync();
        settings.MapDetailsPanelExpanded = IsDetailsPanelExpanded;
        await _settingsRepository.SaveAsync(settings);
    }

    private static MapOrderItem BuildMapOrderItem(Order order)
    {
        return new MapOrderItem
        {
            OrderId = order.Id,
            Customer = order.CustomerName,
            Address = order.Address,
            ScheduledDate = order.ScheduledDate.ToString("yyyy-MM-dd"),
            AssignedTourId = order.AssignedTourId ?? string.Empty,
            IsAssigned = !string.IsNullOrWhiteSpace(order.AssignedTourId),
            Latitude = order.Location?.Latitude ?? double.NaN,
            Longitude = order.Location?.Longitude ?? double.NaN,
            DeliveryLabel = string.IsNullOrWhiteSpace(order.DeliveryType) ? "Frei Bordsteinkante" : order.DeliveryType,
            StatusLabel = NormalizeOrderStatus(order.OrderStatus),
            TourStatusLabel = ResolveTourStatus(order)
        };
    }

    private static string NormalizeOrderStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "nicht festgelegt" : status.Trim();
        return string.Equals(normalized, "Bereits eingeplant", StringComparison.OrdinalIgnoreCase)
            ? "nicht festgelegt"
            : normalized;
    }

    private static string ResolveTourStatus(Order order)
    {
        return string.IsNullOrWhiteSpace(order.AssignedTourId) ? "Offen" : PlannedTourStatus;
    }

    public string ResolveOrderStatusColor(string? orderStatus, bool isAssigned)
    {
        if (isAssigned)
        {
            return _statusColorPlanned;
        }

        var normalized = NormalizeOrderStatus(orderStatus);
        if (string.Equals(normalized, "Bestellt", StringComparison.OrdinalIgnoreCase))
        {
            return _statusColorOrdered;
        }

        if (string.Equals(normalized, "Auf dem Weg", StringComparison.OrdinalIgnoreCase))
        {
            return _statusColorOnTheWay;
        }

        if (string.Equals(normalized, "An Lager", StringComparison.OrdinalIgnoreCase))
        {
            return _statusColorInStock;
        }

        return _statusColorNotSpecified;
    }

    private static string NormalizeStatusColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 7 && normalized.StartsWith('#') ? normalized.ToUpperInvariant() : fallback;
    }
}

public sealed class MapOrderItem
{
    public string OrderId { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ScheduledDate { get; set; } = string.Empty;
    public string AssignedTourId { get; set; } = string.Empty;
    public bool IsAssigned { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DeliveryLabel { get; set; } = "Frei Bordsteinkante";
    public string StatusLabel { get; set; } = "nicht festgelegt";
    public string TourStatusLabel { get; set; } = "Offen";
}

public sealed class RouteStopItem : ObservableObject
{
    private int _position;
    private string _orderId = string.Empty;
    private string _customer = string.Empty;
    private string _address = string.Empty;
    private double _latitude;
    private double _longitude;
    private bool _isCompanyAnchor;
    private int _plannedStayMinutes = 10;
    private string _etaText = string.Empty;

    public int Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(DisplayPosition));
                OnPropertyChanged(nameof(DisplayEta));
            }
        }
    }

    public string OrderId
    {
        get => _orderId;
        set
        {
            if (SetProperty(ref _orderId, value))
            {
                OnPropertyChanged(nameof(DisplayOrder));
                OnPropertyChanged(nameof(DisplayPosition));
                OnPropertyChanged(nameof(DisplayStay));
                OnPropertyChanged(nameof(DisplayEta));
            }
        }
    }

    public string Customer
    {
        get => _customer;
        set
        {
            if (SetProperty(ref _customer, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Address
    {
        get => _address;
        set
        {
            if (SetProperty(ref _address, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public double Latitude
    {
        get => _latitude;
        set => SetProperty(ref _latitude, value);
    }

    public double Longitude
    {
        get => _longitude;
        set => SetProperty(ref _longitude, value);
    }

    public bool IsCompanyAnchor
    {
        get => _isCompanyAnchor;
        set
        {
            if (SetProperty(ref _isCompanyAnchor, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(DisplayOrder));
                OnPropertyChanged(nameof(DisplayStay));
                OnPropertyChanged(nameof(DisplayPosition));
                OnPropertyChanged(nameof(DisplayEta));
            }
        }
    }

    public int PlannedStayMinutes
    {
        get => _plannedStayMinutes;
        set
        {
            var clamped = Math.Max(0, value);
            if (SetProperty(ref _plannedStayMinutes, clamped))
            {
                OnPropertyChanged(nameof(DisplayStay));
            }
        }
    }

    public string EtaText
    {
        get => _etaText;
        set
        {
            if (SetProperty(ref _etaText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayEta));
            }
        }
    }

    private bool IsCompanyDisplay => IsCompanyAnchor ||
                                     string.Equals(OrderId, "__company_start__", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(OrderId, "__company_end__", StringComparison.OrdinalIgnoreCase) ||
                                     (OrderId ?? string.Empty).StartsWith("__company_", StringComparison.OrdinalIgnoreCase);

    public string DisplayPosition => IsCompanyDisplay ? string.Empty : Position.ToString(CultureInfo.InvariantCulture);
    public string DisplayName => IsCompanyDisplay ? Address : (!string.IsNullOrWhiteSpace(Customer) ? Customer : Address);
    public string DisplayOrder => IsCompanyDisplay ? string.Empty : OrderId;
    public string DisplayStay => IsCompanyDisplay ? string.Empty : $"{PlannedStayMinutes} min";
    public string DisplayEta => IsCompanyDisplay ? string.Empty : EtaText;
}

public sealed record CompanyMarkerInfo(string Name, string Address, double Latitude, double Longitude);

public sealed class SavedTourLookupItem
{
    public int TourId { get; set; }
    public string Label { get; set; } = string.Empty;

    public override string ToString()
    {
        return Label;
    }
}
