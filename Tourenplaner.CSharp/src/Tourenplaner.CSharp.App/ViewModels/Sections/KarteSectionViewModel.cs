using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
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
    private const string AvisoBadgeColorNotAvisiert = "#64748B";
    private const string AvisoBadgeColorInformiert = "#F59E0B";
    private const string AvisoBadgeColorBestaetigt = "#16A34A";
    private static readonly IReadOnlyList<string> _orderStatusOptions =
    [
        Order.DefaultOrderStatus,
        Order.OrderedStatus,
        Order.InTransitStatus,
        Order.PartiallyInTransitStatus,
        Order.PartiallyReadyStatus,
        Order.ReadyToDeliverStatus
    ];
    private static readonly IReadOnlyList<string> _avisoStatusOptions =
    [
        "nicht avisiert",
        "informiert",
        "Best\u00E4tigt"
    ];
    private readonly JsonOrderRepository _orderRepository;
    private readonly JsonToursRepository _tourRepository;
    private readonly JsonEmployeesRepository _employeeRepository;
    private readonly JsonVehicleDataRepository _vehicleRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly RouteOptimizationService _optimizationService;
    private readonly MapRouteService _mapRouteService;
    private readonly OsrmRoutingService _osrmRoutingService;
    private readonly TourConflictService _conflictService;
    private readonly Dictionary<string, string> _employeeLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Order> _allOrders = new();
    private readonly List<TourRecord> _savedTours = new();
    private readonly List<GeoPoint> _routeGeometryPoints = new();
    private readonly List<OsrmRouteLeg> _routeLegs = new();
    private readonly List<RouteStopItem> _timedStops = new();
    private VehicleDataRecord _vehicleData = new();

    private string _searchText = string.Empty;
    private bool _includeOpenOrders = true;
    private bool _includePlannedOrders = true;
    private MapOrderItem? _selectedOrder;
    private RouteStopItem? _selectedRouteStop;
    private string _routeName = $"Tour {DateOnly.FromDateTime(DateTime.Today):dd.MM.yyyy}";
    private string _routeDate = DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy");
    private string _routeStartHour = "07";
    private string _routeStartMinute = "30";
    private string _defaultRouteStartHour = "07";
    private string _defaultRouteStartMinute = "30";
    private double _routeDistanceKm;
    private string _statusText = "Loading map orders...";
    private string _routeTimingSummary = "Noch keine Stopps geplant.";
    private string _driveTimesText = "Noch keine Stopps geplant.";
    private string _routeOperationalSummaryText = "Noch keine Stopps geplant.";
    private string _routeTotalWeightText = "Totalgewicht: 0 kg";
    private string _routeLoadSummaryText = string.Empty;
    private string _avisoEmailSubjectTemplate = AppSettings.DefaultAvisoEmailSubjectTemplate;
    private string _companyName = "Firma";
    private string _companyAddress = string.Empty;
    private string _statusColorNotSpecified = AppSettings.DefaultStatusColorNotSpecified;
    private string _statusColorOrdered = AppSettings.DefaultStatusColorOrdered;
    private string _statusColorOnTheWay = AppSettings.DefaultStatusColorOnTheWay;
    private string _statusColorInStock = AppSettings.DefaultStatusColorInStock;
    private string _statusColorPlanned = AppSettings.DefaultStatusColorPlanned;
    private bool _mapSearchDimNonMatchingPins;
    private GeoPoint? _companyLocation;
    private bool _isDetailsOpen;
    private bool _isDetailsPanelExpanded = true;
    private bool _isRouteNameAutoManaged = true;
    private bool _suppressRouteNameAutoDetection;
    private bool _savedTourSelectionSync;
    private bool _suppressDetailStatusSave;
    private bool _suppressDetailAvisoStatusSave;
    private bool _suppressRouteChangeTracking;
    private bool _suppressFilterRefresh;
    private bool _isUpdatingFilterOptions;
    private bool _hasUnsavedRouteChanges;
    private bool _arePinInfoCardsVisible;
    private bool _isMapFilterPanelVisible;
    private bool _isMapLegendPanelVisible;
    private double _pinInfoCardScale = 1.0;
    private bool _mapPinInfoCardShowName = true;
    private bool _mapPinInfoCardShowOrderNumber = true;
    private bool _mapPinInfoCardShowStreet = true;
    private bool _mapPinInfoCardShowPostalCodeCity = true;
    private bool _mapPinInfoCardShowNotes = true;
    private bool _mapPinInfoCardShowProducts = true;
    private bool _mapPinInfoCardShowTotalWeight = true;
    private int _routeGeometryRevision;
    private int _activeTourId;
    private string _currentRouteVehicleId = string.Empty;
    private string _currentRouteTrailerId = string.Empty;
    private string _currentRouteSecondaryVehicleId = string.Empty;
    private string _currentRouteSecondaryTrailerId = string.Empty;
    private SavedTourLookupItem? _selectedSavedTour;
    private string _detailSelectedStatus = Order.DefaultOrderStatus;
    private string _detailSelectedAvisoStatus = "nicht avisiert";
    private readonly List<MapOrderItem> _dimmedMapOrders = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private Func<Task>? _openSplitScreenAsync;

    public KarteSectionViewModel(
        string ordersJsonPath,
        string toursJsonPath,
        string settingsJsonPath,
        AppDataSyncService dataSyncService,
        Func<Task>? openSplitScreenAsync = null)
        : base("Karte", "Map order review, marker filters, route panel and save-to-tour workflow.")
    {
        _orderRepository = new JsonOrderRepository(ordersJsonPath);
        _tourRepository = new JsonToursRepository(toursJsonPath);
        var dataRoot = Path.GetDirectoryName(settingsJsonPath) ?? string.Empty;
        _employeeRepository = new JsonEmployeesRepository(Path.Combine(dataRoot, "employees.json"));
        _vehicleRepository = new JsonVehicleDataRepository(Path.Combine(dataRoot, "vehicles.json"));
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _dataSyncService = dataSyncService;
        _optimizationService = new RouteOptimizationService();
        _mapRouteService = new MapRouteService();
        _osrmRoutingService = new OsrmRoutingService();
        _conflictService = new TourConflictService();
        _openSplitScreenAsync = openSplitScreenAsync;

        RefreshCommand = new AsyncCommand(RefreshAsync);
        AddToRouteCommand = new DelegateCommand(AddSelectedOrderToRoute, () => SelectedOrder is not null);
        RemoveOrderFromTourCommand = new AsyncCommand(RemoveSelectedOrderFromTourAsync, CanRemoveSelectedOrderFromTour);
        RemoveFromRouteCommand = new DelegateCommand(RemoveSelectedRouteStop, () => SelectedRouteStop is not null && !IsCompanyStop(SelectedRouteStop));
        MoveStopUpCommand = new DelegateCommand(MoveSelectedStopUp, () => CanMoveSelectedStop(-1));
        MoveStopDownCommand = new DelegateCommand(MoveSelectedStopDown, () => CanMoveSelectedStop(1));
        OptimizeRouteCommand = new AsyncCommand(OptimizeRouteAsync, () => RouteStops.Count(x => !IsCompanyStop(x)) > 2);
        OpenCreateTourDialogCommand = new AsyncCommand(OpenCreateTourDialogAsync);
        EditSelectedTourCommand = new AsyncCommand(OpenEditSelectedTourDialogAsync, CanEditOrLeaveSelectedTour);
        ExportRouteCommand = new AsyncCommand(ExportRouteAsync, CanExportRoute);
        SaveRouteAsTourCommand = new AsyncCommand(SaveRouteAsTourAsync, () => RouteStops.Any(x => !IsCompanyStop(x)));
        SaveCurrentTourCommand = new AsyncCommand(SaveCurrentTourAsync, CanSaveCurrentTour);
        DeleteSelectedTourCommand = new AsyncCommand(DeleteSelectedTourAsync, CanEditOrLeaveSelectedTour);
        ClearRouteCommand = new DelegateCommand(ClearRoute, () => RouteStops.Any(x => !IsCompanyStop(x)));
        LeaveSelectedTourCommand = new DelegateCommand(LeaveSelectedTour, CanEditOrLeaveSelectedTour);
        ApplyStartTimeCommand = new DelegateCommand(ApplyRouteStartTime);
        ToggleDetailsPanelCommand = new AsyncCommand(ToggleDetailsPanelAsync);
        CloseDetailsCommand = new DelegateCommand(CloseDetails, () => SelectedOrder is not null);
        ResetOrderFiltersCommand = new DelegateCommand(ResetOrderFilters);
        ToggleAllOrderFiltersCommand = new DelegateCommand(ToggleAllOrderFilters);
        TogglePinInfoCardsCommand = new DelegateCommand(TogglePinInfoCards);
        OpenSplitScreenCommand = new AsyncCommand(OpenSplitScreenAsync, () => _openSplitScreenAsync is not null);
        SendEmailCommand = new DelegateCommand(SendEmailToSelectedOrder, () => SelectedOrder is not null);
        ShowSelectedOrderTourCommand = new AsyncCommand(ShowSelectedOrderTourAsync, CanShowSelectedOrderTour);
        EditOrderCommand = new AsyncCommand(EditSelectedOrderAsync, () => SelectedOrder is not null);
        _dataSyncService.OrdersChanged += OnOrdersChanged;
        _dataSyncService.DataChanged += OnDataChanged;

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

    public Func<RouteExportSnapshot, Task<RoutePdfExportResult>>? PdfExportHandler { get; set; }

    public ICommand SaveRouteAsTourCommand { get; }

    public ICommand SaveCurrentTourCommand { get; }

    public ICommand DeleteSelectedTourCommand { get; }

    public ICommand ClearRouteCommand { get; }

    public ICommand LeaveSelectedTourCommand { get; }

    public ICommand ApplyStartTimeCommand { get; }

    public ICommand ToggleDetailsPanelCommand { get; }

    public ICommand CloseDetailsCommand { get; }

    public ICommand ResetOrderFiltersCommand { get; }

    public ICommand ToggleAllOrderFiltersCommand { get; }

    public ICommand TogglePinInfoCardsCommand { get; }

    public ICommand OpenSplitScreenCommand { get; }

    public ICommand SendEmailCommand { get; }

    public ICommand ShowSelectedOrderTourCommand { get; }

    public ICommand EditOrderCommand { get; }

    public ObservableCollection<MapOrderFilterOption> OrderStatusFilters { get; } = new();

    public ObservableCollection<MapOrderFilterOption> DeliveryTypeFilters { get; } = new();

    public ObservableCollection<MapOrderFilterOption> AvisoStatusFilters { get; } = new();

    public string FilterSummaryText => BuildFilterSummaryText();

    public string PinInfoCardsButtonText => ArePinInfoCardsVisible ? "Infokarten ausblenden" : "Infokarten anzeigen";
    public string PinInfoCardsIconGlyph => ArePinInfoCardsVisible ? "\uE8A7" : "\uE7B3";
    public string PinInfoCardsImagePath => ArePinInfoCardsVisible
        ? "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/icon-infocards-off.jpg"
        : "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/icon-infocards-on.jpg";
    public string PinInfoCardScalePercentText => $"{Math.Round(PinInfoCardScale * 100d):0}%";

    public string ToggleAllFiltersButtonText => AreAllFiltersSelected() ? "Alle abwählen" : "Alle auswählen";

    public string LegendStatusColorNotSpecified => _statusColorNotSpecified;
    public string LegendStatusColorOrdered => _statusColorOrdered;
    public string LegendStatusColorOnTheWay => _statusColorOnTheWay;
    public string LegendStatusColorInStock => _statusColorInStock;
    public string LegendStatusColorPlanned => _statusColorPlanned;
    public string LegendAvisoBadgeColorNotAvisiert => AvisoBadgeColorNotAvisiert;
    public string LegendAvisoBadgeColorInformiert => AvisoBadgeColorInformiert;
    public string LegendAvisoBadgeColorBestaetigt => AvisoBadgeColorBestaetigt;

    public SavedTourLookupItem? SelectedSavedTour
    {
        get => _selectedSavedTour;
        set
        {
            var previousTourId = _selectedSavedTour?.TourId ?? 0;
            if (SetProperty(ref _selectedSavedTour, value) && !_savedTourSelectionSync)
            {
                _ = LoadSelectedSavedTourAsync(previousTourId);
                if (LeaveSelectedTourCommand is DelegateCommand leave)
                {
                    leave.RaiseCanExecuteChanged();
                }

                if (EditSelectedTourCommand is AsyncCommand editTour)
                {
                    editTour.RaiseCanExecuteChanged();
                }

                if (DeleteSelectedTourCommand is AsyncCommand deleteTour)
                {
                    deleteTour.RaiseCanExecuteChanged();
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

    public bool IncludeOpenOrders
    {
        get => _includeOpenOrders;
        set
        {
            if (SetProperty(ref _includeOpenOrders, value))
            {
                TriggerOrderFilterRefresh();
            }
        }
    }

    public bool IncludePlannedOrders
    {
        get => _includePlannedOrders;
        set
        {
            if (SetProperty(ref _includePlannedOrders, value))
            {
                TriggerOrderFilterRefresh();
            }
        }
    }

    public bool ArePinInfoCardsVisible
    {
        get => _arePinInfoCardsVisible;
        set
        {
            if (SetProperty(ref _arePinInfoCardsVisible, value))
            {
                OnPropertyChanged(nameof(PinInfoCardsButtonText));
                OnPropertyChanged(nameof(PinInfoCardsIconGlyph));
                OnPropertyChanged(nameof(PinInfoCardsImagePath));
            }
        }
    }

    public bool IsMapFilterPanelVisible
    {
        get => _isMapFilterPanelVisible;
        set
        {
            if (!SetProperty(ref _isMapFilterPanelVisible, value))
            {
                return;
            }

            if (value && _isMapLegendPanelVisible)
            {
                _isMapLegendPanelVisible = false;
                OnPropertyChanged(nameof(IsMapLegendPanelVisible));
            }
        }
    }

    public bool IsMapLegendPanelVisible
    {
        get => _isMapLegendPanelVisible;
        set
        {
            if (!SetProperty(ref _isMapLegendPanelVisible, value))
            {
                return;
            }

            if (value && _isMapFilterPanelVisible)
            {
                _isMapFilterPanelVisible = false;
                OnPropertyChanged(nameof(IsMapFilterPanelVisible));
            }
        }
    }

    public double PinInfoCardScale
    {
        get => _pinInfoCardScale;
        set
        {
            var clamped = Math.Clamp(value, 0.7d, 1.8d);
            if (SetProperty(ref _pinInfoCardScale, clamped))
            {
                OnPropertyChanged(nameof(PinInfoCardScalePercentText));
            }
        }
    }

    public bool MapPinInfoCardShowName => _mapPinInfoCardShowName;
    public bool MapPinInfoCardShowOrderNumber => _mapPinInfoCardShowOrderNumber;
    public bool MapPinInfoCardShowStreet => _mapPinInfoCardShowStreet;
    public bool MapPinInfoCardShowPostalCodeCity => _mapPinInfoCardShowPostalCodeCity;
    public bool MapPinInfoCardShowNotes => _mapPinInfoCardShowNotes;
    public bool MapPinInfoCardShowProducts => _mapPinInfoCardShowProducts;
    public bool MapPinInfoCardShowTotalWeight => _mapPinInfoCardShowTotalWeight;

    public string RouteName
    {
        get => _routeName;
        set
        {
            if (SetProperty(ref _routeName, value))
            {
                if (!_suppressRouteNameAutoDetection)
                {
                    _isRouteNameAutoManaged = string.Equals(
                        (_routeName ?? string.Empty).Trim(),
                        BuildDefaultRouteName(RouteDate),
                        StringComparison.OrdinalIgnoreCase);
                }

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
                if (_isRouteNameAutoManaged)
                {
                    SetRouteNameFromDate(value);
                }

                MarkRouteChanged();
            }
        }
    }

    public string RouteStartHour
    {
        get => _routeStartHour;
        set
        {
            var normalized = NormalizeTimeInputPartForEditing(value);
            if (SetProperty(ref _routeStartHour, normalized))
            {
                MarkRouteChanged();
                ApplyRouteStartTimeFromInput();
            }
        }
    }

    public string RouteStartMinute
    {
        get => _routeStartMinute;
        set
        {
            var normalized = NormalizeTimeInputPartForEditing(value);
            if (SetProperty(ref _routeStartMinute, normalized))
            {
                MarkRouteChanged();
                ApplyRouteStartTimeFromInput();
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

    public string RouteOperationalSummaryText
    {
        get => _routeOperationalSummaryText;
        private set => SetProperty(ref _routeOperationalSummaryText, value);
    }

    public string RouteTotalWeightText
    {
        get => _routeTotalWeightText;
        private set => SetProperty(ref _routeTotalWeightText, value);
    }

    public string RouteLoadSummaryText
    {
        get => _routeLoadSummaryText;
        private set => SetProperty(ref _routeLoadSummaryText, value);
    }

    public IReadOnlyList<string> OrderStatusOptions => _orderStatusOptions;
    public IReadOnlyList<string> AvisoStatusOptions => _avisoStatusOptions;

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

    public string DetailSelectedAvisoStatus
    {
        get => _detailSelectedAvisoStatus;
        set
        {
            if (!SetProperty(ref _detailSelectedAvisoStatus, value))
            {
                return;
            }

            if (_suppressDetailAvisoStatusSave)
            {
                return;
            }

            _ = UpdateSelectedAvisoStatusAsync(value);
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
                _suppressDetailAvisoStatusSave = true;
                try
                {
                    DetailSelectedStatus = value is null ? _orderStatusOptions[0] : DetailOrderStatus;
                    DetailSelectedAvisoStatus = value is null ? _avisoStatusOptions[0] : DetailAvisoStatus;
                }
                finally
                {
                    _suppressDetailStatusSave = false;
                    _suppressDetailAvisoStatusSave = false;
                }
                OnPropertyChanged(nameof(DetailAddress));
                OnPropertyChanged(nameof(DetailCustomer));
                OnPropertyChanged(nameof(DetailOrderNumber));
                OnPropertyChanged(nameof(DetailOrderStatus));
                OnPropertyChanged(nameof(DetailOrderStatusColor));
                OnPropertyChanged(nameof(DetailAvisoStatus));
                OnPropertyChanged(nameof(CanEditDetailAvisoStatus));
                OnPropertyChanged(nameof(DetailTourStatus));
                OnPropertyChanged(nameof(DetailProducts));
                OnPropertyChanged(nameof(DetailProductItems));
                OnPropertyChanged(nameof(DetailEmail));
                OnPropertyChanged(nameof(DetailPhone));
                OnPropertyChanged(nameof(DetailDeliveryType));
                OnPropertyChanged(nameof(DetailNotes));
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
                if (value is not null && !IsCompanyStop(value))
                {
                    SelectOrderDetailsById(value.OrderId);
                }
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        var settingsTask = _settingsRepository.LoadAsync();
        var ordersTask = _orderRepository.GetAllAsync();
        var vehiclesTask = _vehicleRepository.LoadAsync();
        var employeesTask = _employeeRepository.LoadAsync();
        await Task.WhenAll(settingsTask, ordersTask, vehiclesTask, employeesTask);

        var settings = await settingsTask;
        _vehicleData = await vehiclesTask;
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
        _mapSearchDimNonMatchingPins = settings.MapSearchDimNonMatchingPins;
        _mapPinInfoCardShowName = settings.MapPinInfoCardShowName;
        _mapPinInfoCardShowOrderNumber = settings.MapPinInfoCardShowOrderNumber;
        _mapPinInfoCardShowStreet = settings.MapPinInfoCardShowStreet;
        _mapPinInfoCardShowPostalCodeCity = settings.MapPinInfoCardShowPostalCodeCity;
        _mapPinInfoCardShowNotes = settings.MapPinInfoCardShowNotes;
        _mapPinInfoCardShowProducts = settings.MapPinInfoCardShowProducts;
        _mapPinInfoCardShowTotalWeight = settings.MapPinInfoCardShowTotalWeight;
        var (defaultHour, defaultMinute) = ParseStartTimePartsOrDefault(settings.TourDefaultStartTime);
        _defaultRouteStartHour = defaultHour;
        _defaultRouteStartMinute = defaultMinute;
        NotifyLegendColorsChanged();
        IsDetailsPanelExpanded = settings.MapDetailsPanelExpanded;
        _companyLocation = await AddressGeocodingService.TryGeocodeAddressAsync(
            settings.CompanyStreet,
            settings.CompanyPostalCode,
            settings.CompanyCity,
            _companyAddress);
        _employeeLabelsById.Clear();
        foreach (var employee in await employeesTask)
        {
            if (!string.IsNullOrWhiteSpace(employee.Id))
            {
                _employeeLabelsById[employee.Id] = employee.DisplayName;
            }
        }
        EnsureCompanyAnchors();
        OnPropertyChanged(nameof(CompanyMarker));
        _allOrders.Clear();
        _allOrders.AddRange(await ordersTask);

        var statusSynchronized = SyncDerivedOrderStatuses(_allOrders);
        var locationBackfilled = await BackfillMissingLocationsAsync();
        if (statusSynchronized || locationBackfilled)
        {
            await _orderRepository.SaveAllAsync(_allOrders);
        }

        RefreshOrderFilterOptions();
        RebuildOrderGrid();
        await LoadSavedToursAsync();
        _ = RebuildRouteGeometryAsync();
        UpdateRouteSummary();
    }

    public string DetailAddress => FormatOrderAddress(FindSelectedOrderModel());
    public string DetailCustomer => FormatDeliveryAddress(FindSelectedOrderModel());
    public string DetailOrderNumber => SelectedOrder?.OrderId ?? "n/a";
    public string DetailOrderStatus => FindSelectedOrderModel()?.OrderStatus ?? SelectedOrder?.StatusLabel ?? Order.DefaultOrderStatus;
    public string DetailOrderStatusColor => ResolveOrderStatusColor(DetailOrderStatus, isAssigned: false);
    public string DetailAvisoStatus => NormalizeAvisoStatus(FindSelectedOrderModel()?.AvisoStatus);
    public bool CanEditDetailAvisoStatus
    {
        get
        {
            var order = FindSelectedOrderModel();
            return IsOrderAssignedOrInDraftRoute(order);
        }
    }
    public string DetailTourStatus => SelectedOrder?.TourStatusLabel ?? "Offen";
    public string DetailProducts => OrderProductFormatter.BuildDetails(FindSelectedOrderModel()?.Products);
    public IReadOnlyList<DetailProductItem> DetailProductItems => BuildDetailProductItems(FindSelectedOrderModel()?.Products);
    public string DetailEmail => FindSelectedOrderModel()?.Email ?? "n/a";
    public string DetailPhone => FindSelectedOrderModel()?.Phone ?? "n/a";
    public string DetailDeliveryType => FindSelectedOrderModel()?.DeliveryType ?? SelectedOrder?.DeliveryLabel ?? "Frei Bordsteinkante";
    public string DetailNotes => NormalizeUiText(FindSelectedOrderModel()?.Notes);

    public CompanyMarkerInfo? CompanyMarker =>
        _companyLocation is null
            ? null
            : new CompanyMarkerInfo(_companyName, _companyAddress, _companyLocation.Latitude, _companyLocation.Longitude);

    public void SetOpenSplitScreenAction(Func<Task>? openSplitScreenAsync)
    {
        _openSplitScreenAsync = openSplitScreenAsync;
        if (OpenSplitScreenCommand is AsyncCommand openSplit)
        {
            openSplit.RaiseCanExecuteChanged();
        }
    }

    private async Task OpenSplitScreenAsync()
    {
        if (_openSplitScreenAsync is null)
        {
            return;
        }

        await _openSplitScreenAsync();
    }

    private static string FormatOrderAddress(Order? order)
    {
        if (order is null)
        {
            return "Keine Auswahl";
        }

        var lines = new[]
        {
            (order.OrderAddress?.Name ?? string.Empty).Trim(),
            (order.OrderAddress?.Street ?? string.Empty).Trim(),
            string.Join(' ', new[]
            {
                (order.OrderAddress?.PostalCode ?? string.Empty).Trim(),
                (order.OrderAddress?.City ?? string.Empty).Trim()
            }.Where(x => !string.IsNullOrWhiteSpace(x)))
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToList();

        return lines.Count == 0
            ? (string.IsNullOrWhiteSpace(order.Address) ? "Keine Auswahl" : NormalizeUiText(order.Address))
            : NormalizeUiText(string.Join(Environment.NewLine, lines));
    }

    private static string FormatDeliveryAddress(Order? order)
    {
        if (order is null)
        {
            return "Keine Auswahl";
        }

        var lines = new[]
        {
            (order.DeliveryAddress?.Name ?? string.Empty).Trim(),
            (order.DeliveryAddress?.ContactPerson ?? string.Empty).Trim(),
            (order.DeliveryAddress?.Street ?? string.Empty).Trim(),
            string.Join(' ', new[]
            {
                (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim(),
                (order.DeliveryAddress?.City ?? string.Empty).Trim()
            }.Where(x => !string.IsNullOrWhiteSpace(x)))
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToList();

        return lines.Count == 0
            ? (string.IsNullOrWhiteSpace(order.CustomerName) ? "Keine Auswahl" : NormalizeUiText(order.CustomerName))
            : NormalizeUiText(string.Join(Environment.NewLine, lines));
    }

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
            DisplayIndex = x.DisplayIndex,
            OrderId = x.OrderId,
            Customer = x.Customer,
            Address = x.Address,
            Latitude = x.Latitude,
            Longitude = x.Longitude
        })
            .ToList();
    }

    public MapOrderVisualInfo ResolveOrderVisualInfo(string? orderId)
    {
        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            return new MapOrderVisualInfo(
                DeliveryLabel: "Frei Bordsteinkante",
                StatusLabel: Order.DefaultOrderStatus,
                IsAssigned: false,
                AvisoStatusLabel: "nicht avisiert");
        }

        return new MapOrderVisualInfo(
            DeliveryLabel: NormalizeDeliveryType(order.DeliveryType),
            StatusLabel: NormalizeOrderStatus(order.OrderStatus),
            IsAssigned: IsOrderAssignedOrInDraftRoute(order),
            AvisoStatusLabel: NormalizeAvisoStatus(order.AvisoStatus));
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

    private void RebuildOrderGrid(string? preferredSelectedId = null)
    {
        var previousSelectedId = string.IsNullOrWhiteSpace(preferredSelectedId)
            ? SelectedOrder?.OrderId
            : preferredSelectedId;
        RefreshOrderFilterOptions();
        var routeOrderIds = RouteStops.Select(s => s.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = (_searchText ?? string.Empty).Trim();
        IEnumerable<Order> filtered = _allOrders
            .Where(o => o.Type == OrderType.Map && o.Location is not null)
            .Where(o => !routeOrderIds.Contains(o.Id));

        if (!IncludeOpenOrders)
        {
            filtered = filtered.Where(o => !string.IsNullOrWhiteSpace(o.AssignedTourId));
        }

        if (!IncludePlannedOrders)
        {
            filtered = filtered.Where(o => string.IsNullOrWhiteSpace(o.AssignedTourId));
        }

        var selectedOrderStatuses = GetSelectedFilterLabels(OrderStatusFilters);
        if (selectedOrderStatuses.Count != 0 && selectedOrderStatuses.Count != OrderStatusFilters.Count)
        {
            filtered = filtered.Where(o => selectedOrderStatuses.Contains(NormalizeOrderStatus(o.OrderStatus)));
        }

        var selectedDeliveryTypes = GetSelectedFilterLabels(DeliveryTypeFilters);
        if (selectedDeliveryTypes.Count != 0 && selectedDeliveryTypes.Count != DeliveryTypeFilters.Count)
        {
            filtered = filtered.Where(o => selectedDeliveryTypes.Contains(NormalizeDeliveryType(o.DeliveryType)));
        }

        var selectedAvisoStatuses = GetSelectedFilterLabels(AvisoStatusFilters);
        if (selectedAvisoStatuses.Count != 0 && selectedAvisoStatuses.Count != AvisoStatusFilters.Count)
        {
            filtered = filtered.Where(o => selectedAvisoStatuses.Contains(NormalizeAvisoStatus(o.AvisoStatus)));
        }

        var filteredList = filtered.ToList();
        var hasSearch = !string.IsNullOrWhiteSpace(query);
        var matching = hasSearch
            ? filteredList.Where(o => MatchesSearchQuery(o, query)).ToList()
            : filteredList;
        var nonMatching = hasSearch
            ? filteredList.Where(o => !MatchesSearchQuery(o, query)).ToList()
            : new List<Order>();

        MapOrders.Clear();
        foreach (var order in matching
                     .OrderBy(o => o.ScheduledDate)
                     .ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            MapOrders.Add(BuildMapOrderItem(order));
        }

        _dimmedMapOrders.Clear();
        if (_mapSearchDimNonMatchingPins && hasSearch)
        {
            foreach (var order in nonMatching
                         .OrderBy(o => o.ScheduledDate)
                         .ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
            {
                _dimmedMapOrders.Add(BuildMapOrderItem(order, isDimmed: true));
            }
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

    public IReadOnlyList<MapOrderItem> GetMapMarkerSnapshot()
    {
        if (!_mapSearchDimNonMatchingPins || string.IsNullOrWhiteSpace(_searchText))
        {
            return MapOrders.ToList();
        }

        return MapOrders.Concat(_dimmedMapOrders).ToList();
    }

    private void ResetOrderFilters()
    {
        _suppressFilterRefresh = true;
        try
        {
            IncludeOpenOrders = true;
            IncludePlannedOrders = true;
            SetAllFilterOptions(OrderStatusFilters, true);
            SetAllFilterOptions(DeliveryTypeFilters, true);
            SetAllFilterOptions(AvisoStatusFilters, true);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        TriggerOrderFilterRefresh();
    }

    private void TriggerOrderFilterRefresh()
    {
        OnPropertyChanged(nameof(FilterSummaryText));
        OnPropertyChanged(nameof(ToggleAllFiltersButtonText));
        if (_suppressFilterRefresh)
        {
            return;
        }

        RebuildOrderGrid();
    }

    private void RefreshOrderFilterOptions()
    {
        if (_isUpdatingFilterOptions)
        {
            return;
        }

        _isUpdatingFilterOptions = true;
        try
        {
            var mapOrders = _allOrders
                .Where(o => o.Type == OrderType.Map && o.Location is not null)
                .ToList();

            var statuses = mapOrders
                .Select(o => NormalizeOrderStatus(o.OrderStatus))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var deliveryTypes = mapOrders
                .Select(o => NormalizeDeliveryType(o.DeliveryType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var avisoStatuses = mapOrders
                .Select(o => NormalizeAvisoStatus(o.AvisoStatus))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            UpdateFilterOptions(OrderStatusFilters, statuses);
            UpdateFilterOptions(DeliveryTypeFilters, deliveryTypes);
            UpdateFilterOptions(AvisoStatusFilters, avisoStatuses);
        }
        finally
        {
            _isUpdatingFilterOptions = false;
        }

        OnPropertyChanged(nameof(FilterSummaryText));
        OnPropertyChanged(nameof(ToggleAllFiltersButtonText));
    }

    private void UpdateFilterOptions(ObservableCollection<MapOrderFilterOption> target, IReadOnlyList<string> values)
    {
        var previouslyAvailable = target
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previouslySelected = target
            .Where(x => x.IsSelected)
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepCurrentSelection = target.Count > 0;

        foreach (var option in target)
        {
            option.PropertyChanged -= OnOrderFilterOptionChanged;
        }

        target.Clear();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var isNewValue = !previouslyAvailable.Contains(value);
            var isSelected = !keepCurrentSelection || previouslySelected.Contains(value) || isNewValue;
            var option = new MapOrderFilterOption(value, isSelected);
            option.PropertyChanged += OnOrderFilterOptionChanged;
            target.Add(option);
        }
    }

    private void OnOrderFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingFilterOptions || e.PropertyName != nameof(MapOrderFilterOption.IsSelected))
        {
            return;
        }

        TriggerOrderFilterRefresh();
    }

    private static HashSet<string> GetSelectedFilterLabels(ObservableCollection<MapOrderFilterOption> options)
    {
        return options
            .Where(x => x.IsSelected)
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void SetAllFilterOptions(ObservableCollection<MapOrderFilterOption> options, bool isSelected)
    {
        foreach (var option in options)
        {
            option.IsSelected = isSelected;
        }
    }

    private void ToggleAllOrderFilters()
    {
        var selectAll = !AreAllFiltersSelected();
        _suppressFilterRefresh = true;
        try
        {
            IncludeOpenOrders = selectAll;
            IncludePlannedOrders = selectAll;
            SetAllFilterOptions(OrderStatusFilters, selectAll);
            SetAllFilterOptions(DeliveryTypeFilters, selectAll);
            SetAllFilterOptions(AvisoStatusFilters, selectAll);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        TriggerOrderFilterRefresh();
    }

    private void TogglePinInfoCards()
    {
        ArePinInfoCardsVisible = !ArePinInfoCardsVisible;
    }

    private bool AreAllFiltersSelected()
    {
        return IncludeOpenOrders &&
               IncludePlannedOrders &&
               OrderStatusFilters.All(x => x.IsSelected) &&
               DeliveryTypeFilters.All(x => x.IsSelected) &&
               AvisoStatusFilters.All(x => x.IsSelected);
    }

    private string BuildFilterSummaryText()
    {
        var parts = new List<string>();
        if (IncludeOpenOrders && !IncludePlannedOrders)
        {
            parts.Add("nur offen");
        }
        else if (!IncludeOpenOrders && IncludePlannedOrders)
        {
            parts.Add("nur eingeplant");
        }
        else if (!IncludeOpenOrders && !IncludePlannedOrders)
        {
            parts.Add("keine Tourzuordnung");
        }

        AddPartialSummary(parts, "Status", OrderStatusFilters);
        AddPartialSummary(parts, "Lieferart", DeliveryTypeFilters);
        AddPartialSummary(parts, "Aviso", AvisoStatusFilters);

        return parts.Count == 0
            ? "Filter (alle Aufträge)"
            : $"Filter ({string.Join(" | ", parts)})";
    }

    private static void AddPartialSummary(
        List<string> parts,
        string label,
        ObservableCollection<MapOrderFilterOption> options)
    {
        if (options.Count == 0)
        {
            return;
        }

        var selectedCount = options.Count(x => x.IsSelected);
        if (selectedCount == options.Count)
        {
            return;
        }

        parts.Add($"{label}: {selectedCount}/{options.Count}");
    }

    private void NotifyLegendColorsChanged()
    {
        OnPropertyChanged(nameof(LegendStatusColorNotSpecified));
        OnPropertyChanged(nameof(LegendStatusColorOrdered));
        OnPropertyChanged(nameof(LegendStatusColorOnTheWay));
        OnPropertyChanged(nameof(LegendStatusColorInStock));
        OnPropertyChanged(nameof(LegendStatusColorPlanned));
        OnPropertyChanged(nameof(LegendAvisoBadgeColorNotAvisiert));
        OnPropertyChanged(nameof(LegendAvisoBadgeColorInformiert));
        OnPropertyChanged(nameof(LegendAvisoBadgeColorBestaetigt));
        OnPropertyChanged(nameof(DetailOrderStatusColor));
        OnPropertyChanged(nameof(DetailProductItems));
    }

    private void AddSelectedOrderToRoute()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        if (!ConfirmOrderReassignmentIfNeeded(SelectedOrder))
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

    private bool ConfirmOrderReassignmentIfNeeded(MapOrderItem order)
    {
        var assignedTourIdText = (order.AssignedTourId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(assignedTourIdText))
        {
            return true;
        }

        var targetTourId = ResolveCurrentTourId();
        if (int.TryParse(assignedTourIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var assignedTourId) &&
            assignedTourId > 0 &&
            assignedTourId == targetTourId)
        {
            // Already assigned to the currently edited tour.
            return true;
        }

        var assignedTourLabel = BuildTourLabelById(assignedTourIdText);
        var targetTourLabel = targetTourId > 0
            ? BuildTourLabelById(targetTourId.ToString(CultureInfo.InvariantCulture))
            : "die aktuelle neue Route";

        var message =
            $"Der Auftrag {order.OrderId} ist bereits in {assignedTourLabel} eingeplant.\n\n" +
            $"Wenn du fortfährst, wird der Auftrag beim Speichern nach {targetTourLabel} umgeplant.\n\n" +
            "Trotzdem hinzufügen?";

        var result = MessageBox.Show(
            message,
            "Auftrag bereits eingeplant",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private string BuildTourLabelById(string? tourIdText)
    {
        var text = (tourIdText ?? string.Empty).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tourId) || tourId <= 0)
        {
            return $"Tour {text}";
        }

        var match = _savedTours.FirstOrDefault(x => x.Id == tourId);
        if (match is not null && !string.IsNullOrWhiteSpace(match.Name))
        {
            return $"Tour {tourId} ({match.Name.Trim()})";
        }

        return $"Tour {tourId}";
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
        if (order is null)
        {
            return;
        }

        var isInDraftRoute = RouteStops.Any(x =>
            !IsCompanyStop(x) &&
            string.Equals(x.OrderId, order.Id, StringComparison.OrdinalIgnoreCase));

        if (!IsOrderAssignedOrInDraftRoute(order))
        {
            if (!isInDraftRoute)
            {
                return;
            }

            var draftOrderId = order.Id;
            RemoveDraftRouteStop(draftOrderId);

            RebuildPositions();
            RebuildOrderGrid(draftOrderId);
            MarkRouteChanged();
            SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, draftOrderId, StringComparison.OrdinalIgnoreCase))
                           ?? BuildMapOrderItem(order);
            StatusText = $"Auftrag {draftOrderId} wurde aus der aktuellen Route entfernt.";
            return;
        }

        var selectedOrderId = order.Id;
        var tourKey = (order.AssignedTourId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(tourKey))
        {
            StatusText = "Auftrag ist keiner gespeicherten Tour zugeordnet.";
            return;
        }

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
            _dataSyncService.PublishTours(_instanceId, tour.Id.ToString(CultureInfo.InvariantCulture), tour.Id.ToString(CultureInfo.InvariantCulture));
        }

        order.AssignedTourId = string.Empty;
        order.AvisoStatus = NormalizeAvisoStatus(string.Empty);
        await _orderRepository.SaveAllAsync(_allOrders);
        _dataSyncService.PublishOrders(_instanceId, selectedOrderId, selectedOrderId);

        var currentTourId = ResolveCurrentTourId();
        await RefreshAsync();
        if (tour is not null && currentTourId == tour.Id)
        {
            await FocusTourAsync(tour.Id);
        }

        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, selectedOrderId, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Auftrag {selectedOrderId} wurde aus der Tour entfernt.";
    }

    private void RemoveDraftRouteStop(string orderId)
    {
        var draftStop = RouteStops.FirstOrDefault(x =>
            !IsCompanyStop(x) &&
            string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (draftStop is not null)
        {
            RouteStops.Remove(draftStop);
        }
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

    private async Task OptimizeRouteAsync()
    {
        var movableStops = RouteStops.Where(x => !IsCompanyStop(x)).ToList();
        if (movableStops.Count < 3)
        {
            return;
        }

        var start = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
        var end = RouteStops.FirstOrDefault(x => IsCompanyStop(x) && string.Equals(x.OrderId, CompanyEndStopId, StringComparison.OrdinalIgnoreCase));
        if (start is null || end is null)
        {
            return;
        }

        var optimized = await OptimizeMovableStopsByTravelTimeAsync(start, movableStops, end);

        RouteStops.Clear();
        RouteStops.Add(start);

        foreach (var stop in optimized)
        {
            RouteStops.Add(stop);
        }

        RouteStops.Add(end);

        RebuildPositions();
        MarkRouteChanged();
        StatusText = "Route optimiert (minimale Fahrzeit).";
    }

    private async Task SaveRouteAsTourAsync()
    {
        await SaveRouteAsTourAsync(
            routeName: RouteName,
            routeDate: RouteDate,
            startTime: RouteStartTime,
            vehicleId: null,
            trailerId: null,
            secondaryVehicleId: null,
            secondaryTrailerId: null,
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
                    "Die ausgewählte Tour wurde nicht gefunden. Bitte als neue Tour speichern.",
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
                tour.SecondaryVehicleId,
                tour.SecondaryTrailerId,
                tour.EmployeeIds ?? []);
            return;
        }

        await OpenCreateTourDialogAsync();
    }

    private async Task OpenCreateTourDialogAsync()
    {
        var hasRouteStops = RouteStops.Any(x => !IsCompanyStop(x));
        var (employees, vehicles, trailers) = await LoadTourDialogOptionsAsync(RouteDate);

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
            result.SecondaryVehicleId,
            result.SecondaryTrailerId,
            result.EmployeeIds);
    }

    private async Task OpenEditSelectedTourDialogAsync()
    {
        var selectedTourId = ResolveCurrentTourId();
        if (selectedTourId <= 0)
        {
            System.Windows.MessageBox.Show(
                "Bitte zuerst eine gespeicherte Tour auswählen oder auf der Karte laden.",
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
                "Die ausgewählte Tour wurde nicht gefunden.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var (editHour, editMinute) = ParseStartTimePartsOrDefault(tour.StartTime);
        var (employees, vehicles, trailers) = await LoadTourDialogOptionsAsync(tour.Date);

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
            selectedSecondaryVehicleId: tour.SecondaryVehicleId,
            selectedSecondaryTrailerId: tour.SecondaryTrailerId,
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
            result.SecondaryVehicleId,
            result.SecondaryTrailerId,
            result.EmployeeIds);
    }

    private async Task<(List<TourEmployeeOption> Employees, List<TourLookupOption> Vehicles, List<TourLookupOption> Trailers)> LoadTourDialogOptionsAsync(string? routeDate)
    {
        var employeeTask = _employeeRepository.LoadAsync();
        var vehicleTask = _vehicleRepository.LoadAsync();
        await Task.WhenAll(employeeTask, vehicleTask);
        var selectedDate = ResourceAvailabilityService.ParseDate(routeDate);

        var employees = (await employeeTask)
            .Where(x => x.Active &&
                        (!selectedDate.HasValue || !ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, selectedDate.Value)))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourEmployeeOption(x.Id, x.DisplayName))
            .ToList();
        _employeeLabelsById.Clear();
        foreach (var employee in employees)
        {
            if (!string.IsNullOrWhiteSpace(employee.Id))
            {
                _employeeLabelsById[employee.Id] = employee.Label;
            }
        }

        var vehicleData = await vehicleTask;
        var vehicles = vehicleData.Vehicles
            .Where(x => x.Active &&
                        (!selectedDate.HasValue || !ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, selectedDate.Value)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourLookupOption(x.Id, $"{x.Name} [{x.LicensePlate}]"))
            .ToList();
        var trailers = vehicleData.Trailers
            .Where(x => x.Active &&
                        (!selectedDate.HasValue || !ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, selectedDate.Value)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourLookupOption(x.Id, $"{x.Name} [{x.LicensePlate}]"))
            .ToList();

        return (employees, vehicles, trailers);
    }

    private (string Hour, string Minute) ParseStartTimePartsOrDefault(string? startTime)
    {
        var value = (startTime ?? string.Empty).Trim();
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return (parsed.Hour.ToString("00", CultureInfo.InvariantCulture), parsed.Minute.ToString("00", CultureInfo.InvariantCulture));
        }

        return (_defaultRouteStartHour, _defaultRouteStartMinute);
    }

    private async Task SaveRouteAsTourAsync(
        string routeName,
        string routeDate,
        string startTime,
        string? vehicleId,
        string? trailerId,
        string? secondaryVehicleId,
        string? secondaryTrailerId,
        IReadOnlyList<string> employeeIds)
    {
        try
        {
        if (!RouteStops.Any(x => !IsCompanyStop(x)))
        {
            return;
        }

        var availabilityError = await BuildAvailabilityErrorAsync(routeDate, vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId, employeeIds);
        if (!string.IsNullOrWhiteSpace(availabilityError))
        {
            MessageBox.Show(availabilityError, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmCapacityWarning(vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId))
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
        tour.SecondaryVehicleId = string.IsNullOrWhiteSpace(secondaryVehicleId) ? null : secondaryVehicleId.Trim();
        tour.SecondaryTrailerId = string.IsNullOrWhiteSpace(secondaryTrailerId) ? null : secondaryTrailerId.Trim();
        tour.EmployeeIds = (employeeIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var previewTours = tours.ToList();
        previewTours.Add(tour);
        if (!ConfirmAssignmentConflictWarning(previewTours, tour.Id))
        {
            return;
        }

        var routeOrderIds = _mapRouteService.ExtractRouteOrderIds(ToMapRouteStops());
        tours.Add(tour);
        RemoveRouteOrderStopsFromOtherTours(tours, routeOrderIds, nextId);

        await _tourRepository.SaveAsync(tours);
        _dataSyncService.PublishTours(_instanceId, nextId.ToString(CultureInfo.InvariantCulture), nextId.ToString(CultureInfo.InvariantCulture));

        foreach (var order in _allOrders.Where(o => routeOrderIds.Contains(o.Id)))
        {
            order.AssignedTourId = nextId.ToString();
        }

        await _orderRepository.SaveAllAsync(_allOrders);
        _dataSyncService.PublishOrders(_instanceId);
        await RefreshAsync();
        await FocusTourAsync(nextId);
        SetRouteChanged(false);
        StatusText = "Route gespeichert und auf Karte geladen.";
        ToastNotificationService.ShowInfo($"Neue Tour {nextId} wurde erstellt.");
        }
        catch (IOException ioEx)
        {
            MessageBox.Show(
                $"Die Tour konnte nicht gespeichert werden, weil eine Datendatei gerade gesperrt ist.\n\nDetails: {ioEx.Message}",
                "Speichern fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText = "Speichern fehlgeschlagen: Datendatei gesperrt.";
        }
    }

    private async Task UpdateExistingTourAsync(
        int tourId,
        string routeName,
        string routeDate,
        string startTime,
        string? vehicleId,
        string? trailerId,
        string? secondaryVehicleId,
        string? secondaryTrailerId,
        IReadOnlyList<string> employeeIds)
    {
        try
        {
        if (!RouteStops.Any(x => !IsCompanyStop(x)))
        {
            System.Windows.MessageBox.Show(
                "Bitte zuerst mindestens einen Auftrag zur Route hinzufügen.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var availabilityError = await BuildAvailabilityErrorAsync(routeDate, vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId, employeeIds);
        if (!string.IsNullOrWhiteSpace(availabilityError))
        {
            MessageBox.Show(availabilityError, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmCapacityWarning(vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId))
        {
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
        updated.SecondaryVehicleId = string.IsNullOrWhiteSpace(secondaryVehicleId) ? null : secondaryVehicleId.Trim();
        updated.SecondaryTrailerId = string.IsNullOrWhiteSpace(secondaryTrailerId) ? null : secondaryTrailerId.Trim();
        updated.EmployeeIds = (employeeIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var previewTours = tours.ToList();
        previewTours[index] = updated;
        if (!ConfirmAssignmentConflictWarning(previewTours, updated.Id))
        {
            return;
        }

        var routeOrderIds = _mapRouteService.ExtractRouteOrderIds(ToMapRouteStops());
        tours[index] = updated;
        RemoveRouteOrderStopsFromOtherTours(tours, routeOrderIds, tourId);

        await _tourRepository.SaveAsync(tours);
        _dataSyncService.PublishTours(_instanceId, tourId.ToString(CultureInfo.InvariantCulture), tourId.ToString(CultureInfo.InvariantCulture));

        var tourKey = tourId.ToString(CultureInfo.InvariantCulture);
        foreach (var order in _allOrders.Where(o => string.Equals(o.AssignedTourId, tourKey, StringComparison.OrdinalIgnoreCase)))
        {
            order.AssignedTourId = string.Empty;
            order.AvisoStatus = NormalizeAvisoStatus(string.Empty);
        }

        foreach (var order in _allOrders.Where(o => routeOrderIds.Contains(o.Id)))
        {
            order.AssignedTourId = tourKey;
        }

        await _orderRepository.SaveAllAsync(_allOrders);
        _dataSyncService.PublishOrders(_instanceId);
        await RefreshAsync();
        await FocusTourAsync(tourId);
        SetRouteChanged(false);
        StatusText = "Tour aktualisiert und auf Karte geladen.";
        }
        catch (IOException ioEx)
        {
            MessageBox.Show(
                $"Die Tour konnte nicht gespeichert werden, weil eine Datendatei gerade gesperrt ist.\n\nDetails: {ioEx.Message}",
                "Speichern fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText = "Speichern fehlgeschlagen: Datendatei gesperrt.";
        }
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
            Label = "Neue Tour"
        });

        foreach (var tour in _savedTours)
        {
            SavedTours.Add(new SavedTourLookupItem
            {
                TourId = tour.Id,
                Label = BuildTourLookupLabel(tour)
            });
        }

        var keepId = preferredTourId ?? ResolveCurrentTourId();
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

    private async Task LoadSelectedSavedTourAsync(int previousTourId)
    {
        var targetTourId = SelectedSavedTour?.TourId ?? 0;
        if (targetTourId == previousTourId)
        {
            RaiseCommandStates();
            return;
        }

        var hasUnsavedChanges = _hasUnsavedRouteChanges && RouteStops.Any(x => !IsCompanyStop(x));
        if (hasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "Die aktuelle Tour hat ungespeicherte Änderungen.\n\nMöchtest du die aktuelle Tour verlassen und zur gewählten Tour wechseln?",
                "Ungespeicherte Änderungen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                _savedTourSelectionSync = true;
                try
                {
                    SelectedSavedTour = SavedTours.FirstOrDefault(x => x.TourId == previousTourId) ?? SavedTours.FirstOrDefault();
                }
                finally
                {
                    _savedTourSelectionSync = false;
                }

                RaiseCommandStates();
                return;
            }
        }

        if (targetTourId <= 0)
        {
            ClearRoute();
            StatusText = "Tour verlassen.";
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
        _currentRouteVehicleId = (tour.VehicleId ?? string.Empty).Trim();
        _currentRouteTrailerId = (tour.TrailerId ?? string.Empty).Trim();
        _currentRouteSecondaryVehicleId = (tour.SecondaryVehicleId ?? string.Empty).Trim();
        _currentRouteSecondaryTrailerId = (tour.SecondaryTrailerId ?? string.Empty).Trim();
        _suppressRouteChangeTracking = true;
        try
        {
            RouteName = string.IsNullOrWhiteSpace(tour.Name) ? $"Tour {tour.Id}" : NormalizeUiText(tour.Name);
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
        UpdateRouteSummary();
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

    private static void RemoveRouteOrderStopsFromOtherTours(
        List<TourRecord> tours,
        IReadOnlySet<string> routeOrderIds,
        int targetTourId)
    {
        if (routeOrderIds.Count == 0)
        {
            return;
        }

        foreach (var tour in tours.Where(t => t.Id != targetTourId))
        {
            var filtered = (tour.Stops ?? [])
                .Where(stop =>
                    TourStopIdentity.IsCompanyStop(stop) ||
                    !routeOrderIds.Contains(ExtractTourStopOrderId(stop)))
                .ToList();

            if (filtered.Count == (tour.Stops?.Count ?? 0))
            {
                continue;
            }

            for (var i = 0; i < filtered.Count; i++)
            {
                filtered[i].Order = i + 1;
            }

            tour.Stops = filtered;
        }
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
        var name = string.IsNullOrWhiteSpace(tour.Name) ? "Tour" : NormalizeUiText(tour.Name);
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

    private async Task ExportRouteAsync()
    {
        if (!TryBuildRouteExportSnapshot(out var snapshot, out var error))
        {
            System.Windows.MessageBox.Show(
                error,
                "Route exportieren",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var dialog = new RouteExportOptionsDialogWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.SelectedOption is null)
        {
            return;
        }

        if (dialog.SelectedOption == RouteExportOption.GoogleMaps)
        {
            ExportRouteToGoogleMaps(snapshot);
            return;
        }

        if (PdfExportHandler is null)
        {
            System.Windows.MessageBox.Show(
                "Der PDF-Export ist momentan nicht verfügbar.",
                "Route exportieren",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var result = await PdfExportHandler(snapshot);
        if (result.Cancelled)
        {
            return;
        }

        if (result.Succeeded)
        {
            StatusText = result.Message;
            return;
        }

        System.Windows.MessageBox.Show(
            result.Message,
            "Route exportieren",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    private void ExportRouteToGoogleMaps(RouteExportSnapshot snapshot)
    {
        if (!GoogleMapsRouteExportService.TryBuildUrl(snapshot.GoogleMapsPoints, out var url, out var error))
        {
            System.Windows.MessageBox.Show(
                error,
                "Route exportieren",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
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

    private bool TryBuildRouteExportSnapshot(out RouteExportSnapshot snapshot, out string error)
    {
        error = string.Empty;
        snapshot = default!;

        var googleMapsPoints = GetExportPoints();
        if (googleMapsPoints.Count < 2)
        {
            error = "Für den Export werden mindestens zwei Stopps mit Koordinaten benötigt.";
            return false;
        }

        var routeStops = RouteStops.Where(x => !IsCompanyStop(x)).ToList();
        if (routeStops.Count == 0)
        {
            error = "Es ist aktuell keine gültige Tour geladen.";
            return false;
        }

        var stops = routeStops.Select((stop, index) => new RouteExportStopInfo(
            stop.Position,
            ToAlphaLabel(index + 1),
            string.IsNullOrWhiteSpace(stop.Customer) ? stop.Address : stop.Customer,
            stop.Address,
            stop.OrderId,
            stop.Latitude,
            stop.Longitude,
            ResolveTimeWindow(stop.OrderId),
            stop.EtaText,
            ResolveWeightText(stop.OrderId)))
            .ToList();

        snapshot = new RouteExportSnapshot(
            BuildExportTourNameWithEmployees(),
            string.IsNullOrWhiteSpace(RouteDate) ? string.Empty : RouteDate.Trim(),
            $"{NormalizeTimePart(RouteStartHour, 23)}:{NormalizeTimePart(RouteStartMinute, 59)}",
            BuildVehicleTrailerLinesForExport(
                _currentRouteVehicleId,
                _currentRouteTrailerId,
                _currentRouteSecondaryVehicleId,
                _currentRouteSecondaryTrailerId),
            null,
            stops,
            googleMapsPoints,
            _routeGeometryPoints.ToList(),
            _companyLocation is null
                ? null
                : new RouteExportCompanyInfo(_companyName, _companyAddress, _companyLocation.Latitude, _companyLocation.Longitude));

        return true;
    }

    private string BuildExportTourNameWithEmployees()
    {
        var baseName = string.IsNullOrWhiteSpace(RouteName) ? "Aktuelle Route" : RouteName.Trim();
        var tourId = ResolveCurrentTourId();
        if (tourId <= 0)
        {
            return baseName;
        }

        var selectedTour = _savedTours.FirstOrDefault(x => x.Id == tourId);
        if (selectedTour is null || selectedTour.EmployeeIds is null || selectedTour.EmployeeIds.Count == 0)
        {
            return baseName;
        }

        var employees = selectedTour.EmployeeIds
            .Select(ResolveEmployeeLabel)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (employees.Count == 0)
        {
            return baseName;
        }

        var suffix = $"({string.Join(", ", employees)})";
        return baseName.Contains(suffix, StringComparison.OrdinalIgnoreCase)
            ? baseName
            : $"{baseName} {suffix}";
    }

    private string ResolveTimeWindow(string orderId)
    {
        var stop = _savedTours
            .SelectMany(x => x.Stops ?? [])
            .FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(x.Auftragsnummer, orderId, StringComparison.OrdinalIgnoreCase));

        if (stop is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(stop.TimeWindowStart) && !string.IsNullOrWhiteSpace(stop.TimeWindowEnd))
        {
            return $"{stop.TimeWindowStart} - {stop.TimeWindowEnd}";
        }

        return stop.TimeWindowStart ?? stop.TimeWindowEnd ?? string.Empty;
    }

    private string ResolveWeightText(string orderId)
    {
        var stop = _savedTours
            .SelectMany(x => x.Stops ?? [])
            .FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(x.Auftragsnummer, orderId, StringComparison.OrdinalIgnoreCase));

        return stop?.Gewicht ?? string.Empty;
    }

    private string? ResolveVehicleLabel(string? vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            return null;
        }

        var vehicle = _vehicleData.Vehicles.FirstOrDefault(x => string.Equals(x.Id, vehicleId, StringComparison.OrdinalIgnoreCase));
        if (vehicle is null)
        {
            return vehicleId;
        }

        return string.IsNullOrWhiteSpace(vehicle.LicensePlate)
            ? vehicle.Name
            : $"{vehicle.Name} [{vehicle.LicensePlate}]";
    }

    private string? ResolveTrailerLabel(string? trailerId)
    {
        if (string.IsNullOrWhiteSpace(trailerId))
        {
            return null;
        }

        var trailer = _vehicleData.Trailers.FirstOrDefault(x => string.Equals(x.Id, trailerId, StringComparison.OrdinalIgnoreCase));
        if (trailer is null)
        {
            return trailerId;
        }

        return string.IsNullOrWhiteSpace(trailer.LicensePlate)
            ? trailer.Name
            : $"{trailer.Name} [{trailer.LicensePlate}]";
    }

    private static string ToAlphaLabel(int index)
    {
        var value = Math.Max(1, index);
        var label = string.Empty;
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            label = (char)('A' + remainder) + label;
            value = (value - 1) / 26;
        }

        return label;
    }

    private IReadOnlyList<MapRouteStop> ToMapRouteStops()
    {
        return RouteStops
            .Where(x => !IsCompanyStop(x))
            .Select((x, index) => new MapRouteStop(index + 1, x.OrderId, x.Customer, x.Address, x.Latitude, x.Longitude, x.PlannedStayMinutes))
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
        _currentRouteVehicleId = string.Empty;
        _currentRouteTrailerId = string.Empty;
        _currentRouteSecondaryVehicleId = string.Empty;
        _currentRouteSecondaryTrailerId = string.Empty;
        _suppressRouteChangeTracking = true;
        RouteDate = DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy");
        RouteStartHour = _defaultRouteStartHour;
        RouteStartMinute = _defaultRouteStartMinute;
        _isRouteNameAutoManaged = true;
        SetRouteNameFromDate(RouteDate);
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
        UpdateRouteSummary();
    }

    private static string BuildDefaultRouteName(string? routeDate)
    {
        var normalizedDate = (routeDate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedDate))
        {
            normalizedDate = DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy");
        }

        return $"Tour {normalizedDate}";
    }

    private void SetRouteNameFromDate(string? routeDate)
    {
        var autoName = BuildDefaultRouteName(routeDate);
        _suppressRouteNameAutoDetection = true;
        try
        {
            SetProperty(ref _routeName, autoName, nameof(RouteName));
        }
        finally
        {
            _suppressRouteNameAutoDetection = false;
        }
    }

    private void ResetRouteToCompanyAnchors()
    {
        RouteStops.Clear();
        RouteStops.Add(new RouteStopItem
        {
            Position = 1,
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
            Position = 2,
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
        ToastNotificationService.ShowInfo("Tour wurde verlassen.");
    }

    private async Task DeleteSelectedTourAsync()
    {
        var selectedTourId = ResolveCurrentTourId();
        if (selectedTourId <= 0)
        {
            return;
        }

        var tours = (await _tourRepository.LoadAsync()).ToList();
        var target = tours.FirstOrDefault(x => x.Id == selectedTourId);
        if (target is null)
        {
            await LoadSavedToursAsync();
            return;
        }

        var tourLabel = string.IsNullOrWhiteSpace(target.Name)
            ? $"Tour {target.Id.ToString(CultureInfo.InvariantCulture)}"
            : target.Name.Trim();
        var confirmDelete = MessageBox.Show(
            $"Soll {tourLabel} wirklich gelöscht werden?",
            "Tour löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmDelete != MessageBoxResult.Yes)
        {
            return;
        }

        tours.Remove(target);
        await _tourRepository.SaveAsync(tours);

        var tourKey = target.Id.ToString(CultureInfo.InvariantCulture);
        foreach (var order in _allOrders.Where(x => string.Equals(x.AssignedTourId, tourKey, StringComparison.OrdinalIgnoreCase)))
        {
            order.AssignedTourId = string.Empty;
            order.AvisoStatus = NormalizeAvisoStatus(string.Empty);
        }

        await _orderRepository.SaveAllAsync(_allOrders);
        _dataSyncService.PublishTours(_instanceId, tourKey, null);
        _dataSyncService.PublishOrders(_instanceId);

        await RefreshAsync();
        ClearRoute();

        StatusText = $"Tour {tourLabel} wurde gelöscht.";
        ToastNotificationService.ShowInfo($"Tour {tourLabel} wurde gelöscht.");
    }

    private void RebuildPositions()
    {
        EnsureCompanyAnchorOrdering();
        var displayIndex = 0;
        for (var i = 0; i < RouteStops.Count; i++)
        {
            var stop = RouteStops[i];
            stop.Position = i + 1;
            stop.DisplayIndex = IsCompanyStop(stop) ? 0 : ++displayIndex;
        }

        ClearRouteStopEtaValues();

        OnPropertyChanged(nameof(RouteStops));
        UpdateRouteDistanceFromStops();
        UpdateDriveTimePlaceholderState();
        UpdateRouteSummary();
        _ = RebuildRouteGeometryAsync();
        UpdateStatus();
        RaiseCommandStates();
    }

    private void ClearRouteStopEtaValues()
    {
        foreach (var stop in RouteStops)
        {
            stop.EtaText = string.Empty;
            stop.ClearNextLeg();
        }
    }

    private void UpdateRouteDistanceFromStops()
    {
        var distancePoints = RouteStops
            .Where(x => !double.IsNaN(x.Latitude) && !double.IsNaN(x.Longitude))
            .ToList();
        RouteDistanceKm = _optimizationService.ComputeTotalDistanceKm(distancePoints, x => x.Latitude, x => x.Longitude);
    }

    private void UpdateDriveTimePlaceholderState()
    {
        if (RouteStops.Count(x => !IsCompanyStop(x)) == 0)
        {
            ClearDriveTimes();
            return;
        }

        RouteTimingSummary = "Fahrzeiten werden berechnet...";
        DriveTimesText = "Fahrzeiten werden berechnet...";
    }

    private void UpdateStatus()
    {
        var routeStopCount = RouteStops.Count(x => !IsCompanyStop(x));
        StatusText = $"Map orders: {MapOrders.Count} | Route stops: {routeStopCount} | Route distance: {RouteDistanceKm:0.##} km";
    }

    private void UpdateRouteSummary()
    {
        var totalWeightKg = RouteStops
            .Where(x => !IsCompanyStop(x))
            .Select(x => FindOrderWeightKg(x.OrderId))
            .Sum();
        RouteTotalWeightText = $"Totalgewicht: {totalWeightKg} kg";

        var assignments = BuildVehicleAssignments(
            _currentRouteVehicleId,
            _currentRouteTrailerId,
            _currentRouteSecondaryVehicleId,
            _currentRouteSecondaryTrailerId);
        var loadSummaryParts = new List<string>();
        for (var i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            var display = VehicleCombinationDisplayResolver.Resolve(_vehicleData, assignment.VehicleId, assignment.TrailerId);
            var prefix = assignments.Count > 1 ? $"Fahrzeug {i + 1}: " : string.Empty;
            var details = new List<string>();
            if (display.HasVehiclePayload)
            {
                details.Add($"Ladegewicht: {display.VehiclePayloadKg} kg");
            }

            if (display.TrailerLoadKg.HasValue)
            {
                details.Add($"Anhängelast: {display.TrailerLoadKg} kg");
            }

            if (details.Count > 0)
            {
                loadSummaryParts.Add($"{prefix}{string.Join(" | ", details)}");
            }
        }

        RouteLoadSummaryText = string.Join(Environment.NewLine, loadSummaryParts);
    }

    private bool ConfirmAssignmentConflictWarning(IEnumerable<TourRecord> tours, int targetTourId)
    {
        var conflicts = _conflictService.FindSameDayAssignmentConflicts(tours)
            .Where(c => c.TourIdA == targetTourId || c.TourIdB == targetTourId)
            .ToList();
        if (conflicts.Count == 0)
        {
            return true;
        }

        var grouped = conflicts
            .Select(c => new
            {
                Group = GetAssignmentConflictGroupLabel(c),
                Text = BuildAssignmentConflictDisplayText(c)
            })
            .GroupBy(x => x.Group)
            .OrderBy(g => g.Key switch
            {
                "Fahrzeug" => 0,
                "Anhänger" => 1,
                "Mitarbeiter" => 2,
                _ => 3
            })
            .ToList();

        var lines = new List<string>();
        var shown = 0;
        const int maxEntries = 12;
        foreach (var group in grouped)
        {
            if (shown >= maxEntries)
            {
                break;
            }

            lines.Add($"{group.Key}:");
            foreach (var item in group.Select(x => x.Text).Distinct())
            {
                if (shown >= maxEntries)
                {
                    break;
                }

                lines.Add($"- {item}");
                shown++;
            }

            lines.Add(string.Empty);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var totalDistinct = conflicts.Select(BuildAssignmentConflictDisplayText).Distinct().Count();
        var remaining = Math.Max(0, totalDistinct - shown);
        if (remaining > 0)
        {
            lines.Add($"... und {remaining} weitere Konflikte");
        }

        var listText = string.Join(Environment.NewLine, lines);
        var message =
            "Es wurden Doppelbelegungen am selben Tag erkannt:" + Environment.NewLine + Environment.NewLine +
            listText + Environment.NewLine + Environment.NewLine +
            "Trotzdem speichern?";

        return MessageBox.Show(
                   message,
                   "Konfliktwarnung",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private string BuildAssignmentConflictDisplayText(TourAssignmentConflict conflict)
    {
        var resourceType = (conflict.ResourceType ?? string.Empty).Trim().ToLowerInvariant();
        var resourceName = resourceType switch
        {
            "vehicle" or "fahrzeug" => ResolveVehicleLabel(conflict.ResourceId),
            "trailer" or "anhänger" or "anhaenger" => ResolveTrailerLabel(conflict.ResourceId),
            "employee" or "mitarbeiter" => ResolveEmployeeLabel(conflict.ResourceId),
            _ => conflict.ResourceId
        };
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            resourceName = conflict.ResourceId;
        }

        var label = resourceType switch
        {
            "vehicle" or "fahrzeug" => "Fahrzeug",
            "trailer" or "anhänger" or "anhaenger" => "Anhänger",
            "employee" or "mitarbeiter" => "Mitarbeiter",
            _ => "Ressource"
        };

        return $"{label}: {resourceName} (Tour {conflict.TourIdA} / {conflict.TourIdB}, {conflict.StartA:dd.MM.yyyy})";
    }

    private static string GetAssignmentConflictGroupLabel(TourAssignmentConflict conflict)
    {
        var resourceType = (conflict.ResourceType ?? string.Empty).Trim().ToLowerInvariant();
        return resourceType switch
        {
            "vehicle" or "fahrzeug" => "Fahrzeug",
            "trailer" or "anhänger" or "anhaenger" => "Anhänger",
            "employee" or "mitarbeiter" => "Mitarbeiter",
            _ => "Andere"
        };
    }

    private string ResolveEmployeeLabel(string? employeeId)
    {
        var id = (employeeId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        return _employeeLabelsById.TryGetValue(id, out var label) ? label : id;
    }

    private bool ConfirmCapacityWarning(string? vehicleId, string? trailerId, string? secondaryVehicleId, string? secondaryTrailerId)
    {
        var totalWeightKg = RouteStops
            .Where(x => !IsCompanyStop(x))
            .Select(x => FindOrderWeightKg(x.OrderId))
            .Sum();
        var assignments = BuildVehicleAssignments(vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId);
        var warning = TourCapacityWarningService.EvaluateFleet(_vehicleData, assignments, totalWeightKg);
        if (!warning.IsOverCapacity)
        {
            return true;
        }

        return MessageBox.Show(
                   warning.BuildWarningMessage(),
                   "Kapazitätswarnung",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static List<(string VehicleId, string TrailerId)> BuildVehicleAssignments(
        string? vehicleId,
        string? trailerId,
        string? secondaryVehicleId,
        string? secondaryTrailerId)
    {
        var items = new List<(string VehicleId, string TrailerId)>
        {
            ((vehicleId ?? string.Empty).Trim(), (trailerId ?? string.Empty).Trim()),
            ((secondaryVehicleId ?? string.Empty).Trim(), (secondaryTrailerId ?? string.Empty).Trim())
        };

        return items
            .Where(x => !string.IsNullOrWhiteSpace(x.VehicleId) || !string.IsNullOrWhiteSpace(x.TrailerId))
            .GroupBy(x => $"{x.VehicleId}|{x.TrailerId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<string?> BuildAvailabilityErrorAsync(
        string routeDate,
        string? vehicleId,
        string? trailerId,
        string? secondaryVehicleId,
        string? secondaryTrailerId,
        IReadOnlyList<string> employeeIds)
    {
        var date = ResourceAvailabilityService.ParseDate(routeDate);
        if (!date.HasValue)
        {
            return null;
        }

        var employeesTask = _employeeRepository.LoadAsync();
        var vehiclesTask = _vehicleRepository.LoadAsync();
        await Task.WhenAll(employeesTask, vehiclesTask);

        var employees = await employeesTask;
        var vehicleData = await vehiclesTask;
        var blocked = new List<string>();

        var normalizedEmployeeIds = (employeeIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var employee in employees.Where(x => normalizedEmployeeIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase)))
        {
            if (ResourceAvailabilityService.IsUnavailableOnDate(employee.UnavailabilityPeriods, date.Value))
            {
                blocked.Add($"Mitarbeiter: {employee.DisplayName}");
            }
        }

        foreach (var assignment in BuildVehicleAssignments(vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId))
        {
            var vehicle = vehicleData.Vehicles.FirstOrDefault(x => string.Equals(x.Id, assignment.VehicleId, StringComparison.OrdinalIgnoreCase));
            if (vehicle is not null && ResourceAvailabilityService.IsUnavailableOnDate(vehicle.UnavailabilityPeriods, date.Value))
            {
                blocked.Add($"Fahrzeug: {vehicle.Name}");
            }

            var trailer = vehicleData.Trailers.FirstOrDefault(x => string.Equals(x.Id, assignment.TrailerId, StringComparison.OrdinalIgnoreCase));
            if (trailer is not null && ResourceAvailabilityService.IsUnavailableOnDate(trailer.UnavailabilityPeriods, date.Value))
            {
                blocked.Add($"Anhänger: {trailer.Name}");
            }
        }

        if (blocked.Count == 0)
        {
            return null;
        }

        return $"Für {routeDate} sind folgende Ressourcen nicht verfügbar:{Environment.NewLine}{string.Join(Environment.NewLine, blocked.Distinct(StringComparer.OrdinalIgnoreCase))}";
    }

    private string? BuildVehicleTrailerLinesForExport(
        string? primaryVehicleId,
        string? primaryTrailerId,
        string? secondaryVehicleId,
        string? secondaryTrailerId)
    {
        var lines = BuildVehicleAssignments(primaryVehicleId, primaryTrailerId, secondaryVehicleId, secondaryTrailerId)
            .Select(x =>
            {
                var vehicleLabel = ResolveVehicleLabel(x.VehicleId);
                var trailerLabel = ResolveTrailerLabel(x.TrailerId);
                return $"{(string.IsNullOrWhiteSpace(vehicleLabel) ? "-" : vehicleLabel)} & {(string.IsNullOrWhiteSpace(trailerLabel) ? "-" : trailerLabel)}";
            })
            .ToList();

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private int FindOrderWeightKg(string? orderId)
    {
        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
        {
            return 0;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, normalizedOrderId, StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            return 0;
        }

        return (int)Math.Max(0, Math.Round((order.Products ?? []).Sum(x => Math.Max(0d, x.WeightKg)), MidpointRounding.AwayFromZero));
    }

    private void RaiseCommandStates()
    {
        RaiseCanExecuteChangedIfSupported(AddToRouteCommand);
        RaiseCanExecuteChangedIfSupported(RemoveOrderFromTourCommand);
        RaiseCanExecuteChangedIfSupported(RemoveFromRouteCommand);
        RaiseCanExecuteChangedIfSupported(MoveStopUpCommand);
        RaiseCanExecuteChangedIfSupported(MoveStopDownCommand);
        RaiseCanExecuteChangedIfSupported(OptimizeRouteCommand);
        RaiseCanExecuteChangedIfSupported(OpenCreateTourDialogCommand);
        RaiseCanExecuteChangedIfSupported(EditSelectedTourCommand);
        RaiseCanExecuteChangedIfSupported(ExportRouteCommand);
        RaiseCanExecuteChangedIfSupported(SaveRouteAsTourCommand);
        RaiseCanExecuteChangedIfSupported(SaveCurrentTourCommand);
        RaiseCanExecuteChangedIfSupported(ClearRouteCommand);
        RaiseCanExecuteChangedIfSupported(LeaveSelectedTourCommand);
        RaiseCanExecuteChangedIfSupported(DeleteSelectedTourCommand);
        RaiseCanExecuteChangedIfSupported(CloseDetailsCommand);
        RaiseCanExecuteChangedIfSupported(SendEmailCommand);
        RaiseCanExecuteChangedIfSupported(EditOrderCommand);
        RaiseCanExecuteChangedIfSupported(ShowSelectedOrderTourCommand);
    }

    private static void RaiseCanExecuteChangedIfSupported(ICommand command)
    {
        switch (command)
        {
            case DelegateCommand sync:
                sync.RaiseCanExecuteChanged();
                break;
            case AsyncCommand async:
                async.RaiseCanExecuteChanged();
                break;
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
        return IsOrderAssignedOrInDraftRoute(FindSelectedOrderModel());
    }

    private bool CanShowSelectedOrderTour()
    {
        return IsOrderAssignedOrInDraftRoute(FindSelectedOrderModel());
    }

    private string RouteStartTime => $"{NormalizeTimePart(RouteStartHour, 23)}:{NormalizeTimePart(RouteStartMinute, 59)}";

    private void ApplyRouteStartTimeFromInput()
    {
        RefreshDriveTimesFromCurrentRoute();
        UpdateStatus();
    }

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

    private async Task ShowSelectedOrderTourAsync()
    {
        var order = FindSelectedOrderModel();
        if (order is null)
        {
            return;
        }

        if (!IsOrderAssignedOrInDraftRoute(order))
        {
            if (RouteStops.Any(x => !IsCompanyStop(x) && string.Equals(x.OrderId, order.Id, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = $"Auftrag {order.Id} ist in der aktuellen Route enthalten.";
            }

            return;
        }

        var assignedTourId = (order.AssignedTourId ?? string.Empty).Trim();
        if (!int.TryParse(assignedTourId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tourId) || tourId <= 0)
        {
            System.Windows.MessageBox.Show(
                "Für diesen Auftrag ist keine gültige Tour zugeordnet.",
                "Tour anzeigen",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        await FocusTourAsync(tourId);
        SelectOrderDetailsById(order.Id);
        StatusText = $"Tour {tourId} für Auftrag {order.Id} angezeigt.";
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
        PublishOrderChange(originalId, updated.Id);
        StatusText = $"Auftrag {updated.Id} wurde aktualisiert.";
    }

    public async Task EditDetailProductAsync(DetailProductItem? detailItem)
    {
        var order = FindSelectedOrderModel();
        if (order is null || detailItem is null)
        {
            return;
        }

        var products = order.Products ?? [];
        var productIndex = detailItem.ProductIndex;
        if (productIndex < 0 || productIndex >= products.Count)
        {
            return;
        }

        var dialog = new OrderProductDialogWindow(ProductLineInput.FromOrderProductInfo(products[productIndex]))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        products[productIndex] = dialog.Result.ToOrderProductInfo();
        order.OrderStatus = Order.ResolveOrderStatusFromProducts(products);
        await _orderRepository.SaveAllAsync(_allOrders);
        RebuildOrderGrid(order.Id);
        OnPropertyChanged(nameof(DetailProducts));
        OnPropertyChanged(nameof(DetailProductItems));
        OnPropertyChanged(nameof(DetailOrderStatus));
        OnPropertyChanged(nameof(DetailOrderStatusColor));
        PublishOrderChange(order.Id, order.Id);
        StatusText = $"Produkt in Auftrag {order.Id} wurde aktualisiert.";
    }

    private Order? FindSelectedOrderModel()
    {
        if (SelectedOrder is null)
        {
            return null;
        }

        return _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.OrderId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsOrderAssignedOrInDraftRoute(Order? order)
    {
        if (order is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(order.AssignedTourId))
        {
            return true;
        }

        return RouteStops.Any(x =>
            !IsCompanyStop(x) &&
            string.Equals(x.OrderId, order.Id, StringComparison.OrdinalIgnoreCase));
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

        if ((order.Products ?? []).Count > 0)
        {
            var derivedStatus = Order.ResolveOrderStatusFromProducts(order.Products);
            var currentStatus = NormalizeOrderStatus(order.OrderStatus);
            if (!string.Equals(currentStatus, derivedStatus, StringComparison.OrdinalIgnoreCase))
            {
                order.OrderStatus = derivedStatus;
                await _orderRepository.SaveAllAsync(_allOrders);
                RebuildOrderGrid(order.Id);
                OnPropertyChanged(nameof(RouteStops));
                OnPropertyChanged(nameof(DetailOrderStatus));
                OnPropertyChanged(nameof(DetailOrderStatusColor));
                PublishOrderChange(order.Id, order.Id);
            }

            _suppressDetailStatusSave = true;
            try
            {
                DetailSelectedStatus = derivedStatus;
            }
            finally
            {
                _suppressDetailStatusSave = false;
            }

            StatusText = $"Status für Auftrag {order.Id} wird automatisch aus den Produktstatuswerten abgeleitet ({derivedStatus}).";
            return;
        }

        var normalizedStatus = NormalizeOrderStatus(nextStatus);
        if (string.Equals(NormalizeOrderStatus(order.OrderStatus), normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        order.OrderStatus = normalizedStatus;
        await _orderRepository.SaveAllAsync(_allOrders);

        _suppressDetailStatusSave = true;
        try
        {
            DetailSelectedStatus = normalizedStatus;
        }
        finally
        {
            _suppressDetailStatusSave = false;
        }

        RebuildOrderGrid(order.Id);
        OnPropertyChanged(nameof(RouteStops));
        OnPropertyChanged(nameof(DetailOrderStatus));
        OnPropertyChanged(nameof(DetailOrderStatusColor));
        StatusText = $"Status für Auftrag {order.Id} gespeichert.";
    }

    private static bool SyncDerivedOrderStatuses(IEnumerable<Order> orders)
    {
        var changed = false;
        foreach (var order in orders)
        {
            if (order is null)
            {
                continue;
            }

            var derivedStatus = Order.ResolveOrderStatusFromProducts(order.Products);
            if (string.Equals(NormalizeOrderStatus(order.OrderStatus), derivedStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            order.OrderStatus = derivedStatus;
            changed = true;
        }

        return changed;
    }

    private void OnOrdersChanged(object? sender, OrderChangedEventArgs args)
    {
        if (args.SourceId == _instanceId)
        {
            return;
        }

        _ = ApplyExternalOrderChangeAsync(args);
    }

    private async Task UpdateSelectedAvisoStatusAsync(string? nextStatus)
    {
        var order = FindSelectedOrderModel();
        if (order is null)
        {
            return;
        }

        if (!IsOrderAssignedOrInDraftRoute(order))
        {
            _suppressDetailAvisoStatusSave = true;
            try
            {
                DetailSelectedAvisoStatus = NormalizeAvisoStatus(order.AvisoStatus);
            }
            finally
            {
                _suppressDetailAvisoStatusSave = false;
            }

            StatusText = "Avisierungsstatus kann nur für eingeplante Aufträge geändert werden.";
            return;
        }

        var normalizedStatus = NormalizeAvisoStatus(nextStatus);
        if (string.Equals(NormalizeAvisoStatus(order.AvisoStatus), normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        order.AvisoStatus = normalizedStatus;
        if (SelectedOrder is not null &&
            string.Equals(SelectedOrder.OrderId, order.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedOrder.AvisoStatusLabel = normalizedStatus;
        }

        await _orderRepository.SaveAllAsync(_allOrders);

        _suppressDetailAvisoStatusSave = true;
        try
        {
            DetailSelectedAvisoStatus = normalizedStatus;
        }
        finally
        {
            _suppressDetailAvisoStatusSave = false;
        }

        RebuildOrderGrid(order.Id);
        OnPropertyChanged(nameof(RouteStops));
        OnPropertyChanged(nameof(DetailAvisoStatus));
        PublishOrderChange(order.Id, order.Id);
        StatusText = $"Avisierungsstatus f\u00FCr Auftrag {order.Id} gespeichert.";
    }

    private async Task ApplyExternalOrderChangeAsync(OrderChangedEventArgs args)
    {
        var preferredSelectedOrderId = ResolvePreferredOrderId(args, SelectedOrder?.OrderId);
        var preferredRouteStopOrderId = ResolvePreferredOrderId(args, SelectedRouteStop?.OrderId);

        _allOrders.Clear();
        _allOrders.AddRange(await _orderRepository.GetAllAsync());

        RefreshOrderFilterOptions();
        RefreshRouteStopsFromOrders(args);
        RebuildOrderGrid(preferredSelectedOrderId);

        if (string.IsNullOrWhiteSpace(preferredRouteStopOrderId))
        {
            SelectedRouteStop = null;
        }
        else
        {
            SelectedRouteStop = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, preferredRouteStopOrderId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void RefreshRouteStopsFromOrders(OrderChangedEventArgs args)
    {
        foreach (var stop in RouteStops.Where(x => !IsCompanyStop(x)))
        {
            if (!string.IsNullOrWhiteSpace(args.PreviousOrderId) &&
                string.Equals(stop.OrderId, args.PreviousOrderId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args.CurrentOrderId))
            {
                stop.OrderId = args.CurrentOrderId;
            }

            var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, stop.OrderId, StringComparison.OrdinalIgnoreCase));
            if (order is null)
            {
                continue;
            }

            stop.Customer = order.CustomerName;
            stop.Address = order.Address;
            stop.Latitude = order.Location?.Latitude ?? stop.Latitude;
            stop.Longitude = order.Location?.Longitude ?? stop.Longitude;
        }

        _ = RebuildRouteGeometryAsync();
    }

    private static string? ResolvePreferredOrderId(OrderChangedEventArgs args, string? currentOrderId)
    {
        if (!string.IsNullOrWhiteSpace(currentOrderId) &&
            string.Equals(currentOrderId, args.PreviousOrderId, StringComparison.OrdinalIgnoreCase))
        {
            return args.CurrentOrderId;
        }

        return currentOrderId;
    }

    private void PublishOrderChange(string? previousOrderId, string? currentOrderId)
    {
        _dataSyncService.PublishOrders(_instanceId, previousOrderId, currentOrderId);
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId || args.Kinds == AppDataKind.Orders)
        {
            return;
        }

        var relevantKinds = AppDataKind.Tours | AppDataKind.Vehicles | AppDataKind.Employees | AppDataKind.Settings;
        if ((args.Kinds & relevantKinds) == AppDataKind.None)
        {
            return;
        }

        _ = RefreshAsync();
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

        var position = 1;
        RouteStops.Add(new RouteStopItem
        {
            Position = position++,
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
            stop.Position = position++;
            stop.IsCompanyAnchor = false;
            RouteStops.Add(stop);
        }

        RouteStops.Add(new RouteStopItem
        {
            Position = position,
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

    private static string NormalizeTimeInputPartForEditing(string? value)
    {
        return new string((value ?? string.Empty).Where(char.IsDigit).Take(2).ToArray());
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
        ClearRouteStopEtaValues();

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
        var totalDistanceKm = 0d;
        var sb = new StringBuilder();

        _timedStops[0].EtaText = start.ToString("HH:mm");

        for (var i = 0; i < _routeLegs.Count && i + 1 < _timedStops.Count; i++)
        {
            var fromStop = _timedStops[i];
            var toStop = _timedStops[i + 1];
            var leg = _routeLegs[i];

            var depart = current;
            current = current.AddMinutes(leg.DurationMinutes);
            var arrive = current;
            totalDriveMinutes += leg.DurationMinutes;
            totalDistanceKm += leg.DistanceKm;
            fromStop.SetNextLeg(
                durationText: FormatDuration(leg.DurationMinutes),
                distanceText: $"{leg.DistanceKm:0.0} km",
                departureText: depart.ToString("HH:mm"),
                arrivalText: arrive.ToString("HH:mm"),
                accentColorHex: i % 2 == 0 ? "#7BC6A4" : "#8EC6E8");

            sb.AppendLine($"{BuildStopLabel(fromStop, isFrom: true)} -> {BuildStopLabel(toStop, isFrom: false)}");
            sb.AppendLine($"{leg.DurationMinutes} min | {leg.DistanceKm:0.0} km");
            sb.AppendLine($"{depart:HH:mm} -> {arrive:HH:mm}");
            if (i < _routeLegs.Count - 1)
            {
                sb.AppendLine();
            }

            toStop.EtaText = arrive.ToString("HH:mm");
            if (!IsCompanyStop(toStop))
            {
                totalStayMinutes += Math.Max(0, toStop.PlannedStayMinutes);
                current = current.AddMinutes(Math.Max(0, toStop.PlannedStayMinutes));
            }
        }

        var end = start.AddMinutes(totalDriveMinutes + totalStayMinutes);
        _timedStops[^1].EtaText = end.ToString("HH:mm");
        RouteTimingSummary = $"Start: {start:HH:mm} | Fahrt: {totalDriveMinutes} min | Aufenthalt: {totalStayMinutes} min | Ende: {end:HH:mm}";
        RouteOperationalSummaryText = $"Gesamt Fahrzeit: {FormatDuration(totalDriveMinutes)} | Gesamt Distanz: {totalDistanceKm:0.0} km | Start: {start:HH:mm} | Ende: {end:HH:mm}";
        DriveTimesText = sb.Length == 0 ? "Noch keine Stopps geplant." : sb.ToString().TrimEnd();
    }

    private void ClearDriveTimes()
    {
        RouteTimingSummary = "Noch keine Stopps geplant.";
        DriveTimesText = "Noch keine Stopps geplant.";
        RouteOperationalSummaryText = "Noch keine Stopps geplant.";
    }

    private static string FormatDuration(int totalMinutes)
    {
        var safeMinutes = Math.Max(0, totalMinutes);
        var hours = safeMinutes / 60;
        var minutes = safeMinutes % 60;
        return hours > 0 ? $"{hours}h {minutes}min" : $"{minutes}min";
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

    private async Task<IReadOnlyList<RouteStopItem>> OptimizeMovableStopsByTravelTimeAsync(
        RouteStopItem start,
        IReadOnlyList<RouteStopItem> movableStops,
        RouteStopItem end)
    {
        var matrixStops = new List<RouteStopItem>(movableStops.Count + 2) { start };
        matrixStops.AddRange(movableStops);
        matrixStops.Add(end);

        IReadOnlyList<IReadOnlyList<int>>? matrix = null;
        if (matrixStops.All(HasValidCoordinate))
        {
            var points = matrixStops.Select(x => new GeoPoint(x.Latitude, x.Longitude)).ToList();
            matrix = await _osrmRoutingService.TryBuildDurationMatrixMinutesAsync(points);
        }

        var indexByStop = new Dictionary<RouteStopItem, int>();
        for (var i = 0; i < matrixStops.Count; i++)
        {
            indexByStop[matrixStops[i]] = i;
        }

        double TravelCost(RouteStopItem from, RouteStopItem to)
        {
            if (matrix is not null &&
                indexByStop.TryGetValue(from, out var fromIndex) &&
                indexByStop.TryGetValue(to, out var toIndex) &&
                fromIndex < matrix.Count &&
                toIndex < matrix[fromIndex].Count)
            {
                var duration = matrix[fromIndex][toIndex];
                if (duration >= 0 && duration < int.MaxValue / 8)
                {
                    return duration;
                }
            }

            return EstimateTravelDurationMinutes(from, to);
        }

        return _optimizationService.OptimizeWithFixedEndpoints(start, movableStops, end, TravelCost);
    }

    private static bool HasValidCoordinate(RouteStopItem stop)
    {
        return !double.IsNaN(stop.Latitude) &&
               !double.IsNaN(stop.Longitude) &&
               stop.Latitude is >= -90 and <= 90 &&
               stop.Longitude is >= -180 and <= 180;
    }

    private static double EstimateTravelDurationMinutes(RouteStopItem from, RouteStopItem to)
    {
        if (!HasValidCoordinate(from) || !HasValidCoordinate(to))
        {
            return 999999d;
        }

        var distanceKm = ComputeDistanceKm(new GeoPoint(from.Latitude, from.Longitude), new GeoPoint(to.Latitude, to.Longitude));
        return Math.Max(1d, Math.Round(distanceKm / 55d * 60d, MidpointRounding.AwayFromZero));
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

    private MapOrderItem BuildMapOrderItem(Order order, bool isDimmed = false)
    {
        var totalWeightKg = Math.Max(0d, (order.Products ?? []).Sum(OrderProductFormatter.ResolveTotalWeightKg));
        return new MapOrderItem
        {
            OrderId = order.Id,
            Customer = NormalizeUiText(order.CustomerName),
            Address = NormalizeUiText(order.Address),
            Street = ResolveStreet(order),
            PostalCodeCity = ResolvePostalCodeCity(order),
            Notes = NormalizeUiText(order.Notes),
            ProductLines = BuildProductLineItems(order.Products),
            TotalWeightKgText = totalWeightKg.ToString("0.##", CultureInfo.CurrentCulture),
            ScheduledDate = order.ScheduledDate.ToString("yyyy-MM-dd"),
            AssignedTourId = order.AssignedTourId ?? string.Empty,
            IsAssigned = IsOrderAssignedOrInDraftRoute(order),
            Latitude = order.Location?.Latitude ?? double.NaN,
            Longitude = order.Location?.Longitude ?? double.NaN,
            DeliveryLabel = NormalizeDeliveryType(order.DeliveryType),
            StatusLabel = NormalizeOrderStatus(order.OrderStatus),
            AvisoStatusLabel = NormalizeAvisoStatus(order.AvisoStatus),
            TourStatusLabel = ResolveTourStatus(order),
            IsDimmed = isDimmed
        };
    }

    private static bool MatchesSearchQuery(Order order, string query)
    {
        return order.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               order.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               order.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               NormalizeDeliveryType(order.DeliveryType).Contains(query, StringComparison.OrdinalIgnoreCase) ||
               NormalizeOrderStatus(order.OrderStatus).Contains(query, StringComparison.OrdinalIgnoreCase) ||
               NormalizeAvisoStatus(order.AvisoStatus).Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (order.AssignedTourId ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveStreet(Order order)
    {
        var street = (order.DeliveryAddress?.Street ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(street))
        {
            return NormalizeUiText(street);
        }

        street = (order.OrderAddress?.Street ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(street))
        {
            return NormalizeUiText(street);
        }

        var fallback = (order.Address ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return string.Empty;
        }

        var commaIndex = fallback.IndexOf(',');
        return NormalizeUiText(commaIndex < 0 ? fallback : fallback[..commaIndex].Trim());
    }

    private static string ResolvePostalCodeCity(Order order)
    {
        var delivery = string.Join(' ', new[]
        {
            (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim(),
            (order.DeliveryAddress?.City ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(delivery))
        {
            return NormalizeUiText(delivery);
        }

        var orderAddress = string.Join(' ', new[]
        {
            (order.OrderAddress?.PostalCode ?? string.Empty).Trim(),
            (order.OrderAddress?.City ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(orderAddress))
        {
            return NormalizeUiText(orderAddress);
        }

        var fallback = (order.Address ?? string.Empty).Trim();
        var commaIndex = fallback.IndexOf(',');
        if (commaIndex >= 0 && commaIndex + 1 < fallback.Length)
        {
            return NormalizeUiText(fallback[(commaIndex + 1)..].Trim());
        }

        return string.Empty;
    }

    private static List<string> BuildProductLineItems(IEnumerable<OrderProductInfo>? products)
    {
        var lines = new List<string>();
        foreach (var product in products ?? [])
        {
            if (product is null || string.IsNullOrWhiteSpace(product.Name))
            {
                continue;
            }

            var quantity = Math.Max(1, product.Quantity);
            var supplier = NormalizeUiText(product.Supplier);
            var supplierSuffix = string.IsNullOrWhiteSpace(supplier) ? string.Empty : $" [{supplier}]";
            lines.Add($"{quantity}x {NormalizeUiText(product.Name)}{supplierSuffix}");
        }

        return lines;
    }

    private static string NormalizeUiText(string? value)
    {
        var current = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(current))
        {
            return string.Empty;
        }

        for (var i = 0; i < 2; i++)
        {
            if (!LooksLikeMojibake(current))
            {
                break;
            }

            var bytes = Encoding.GetEncoding(1252).GetBytes(current);
            var decoded = Encoding.UTF8.GetString(bytes);
            if (string.Equals(decoded, current, StringComparison.Ordinal))
            {
                break;
            }

            current = decoded;
        }

        return current;
    }

    private static bool LooksLikeMojibake(string value)
    {
        return value.Contains("\u00C3", StringComparison.Ordinal) ||
               value.Contains("\u00C2", StringComparison.Ordinal) ||
               value.Contains("\u00E2", StringComparison.Ordinal);
    }

    private static string NormalizeOrderStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? Order.DefaultOrderStatus : status.Trim();
        return string.Equals(normalized, "Bereits eingeplant", StringComparison.OrdinalIgnoreCase)
            ? Order.DefaultOrderStatus
            : Order.NormalizeOrderStatus(normalized);
    }

    private static string NormalizeDeliveryType(string? deliveryType)
    {
        var normalized = string.IsNullOrWhiteSpace(deliveryType) ? "Frei Bordsteinkante" : deliveryType.Trim();
        return NormalizeUiText(normalized);
    }

    private static string NormalizeAvisoStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "nicht avisiert" : status.Trim();
        if (string.Equals(normalized, "-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "nicht avisiert";
        }

        if (string.Equals(normalized, "Best\u00E4tigt", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Best\u00E4tigt";
        }

        return _avisoStatusOptions.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? _avisoStatusOptions.First(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase))
            : "nicht avisiert";
    }

    private static string ResolveTourStatus(Order order)
    {
        return string.IsNullOrWhiteSpace(order.AssignedTourId) ? "Offen" : PlannedTourStatus;
    }

    public string ResolveOrderStatusColor(string? orderStatus, bool isAssigned)
    {
        var rawStatus = (orderStatus ?? string.Empty).Trim();
        if (string.Equals(rawStatus, PlannedTourStatus, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawStatus, "Bereits eingeplant", StringComparison.OrdinalIgnoreCase))
        {
            return _statusColorNotSpecified;
        }

        var normalized = NormalizeOrderStatus(orderStatus);
        if (string.Equals(normalized, Order.OrderedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return _statusColorOrdered;
        }

        if (string.Equals(normalized, Order.InTransitStatus, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, Order.PartiallyInTransitStatus, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, Order.PartiallyReadyStatus, StringComparison.OrdinalIgnoreCase))
        {
            return _statusColorOnTheWay;
        }

        if (string.Equals(normalized, Order.ReadyToDeliverStatus, StringComparison.OrdinalIgnoreCase))
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

    private IReadOnlyList<DetailProductItem> BuildDetailProductItems(IEnumerable<OrderProductInfo>? products)
    {
        var culture = CultureInfo.CurrentCulture;
        var items = new List<DetailProductItem>();
        var sourceProducts = (products ?? []).ToList();
        for (var i = 0; i < sourceProducts.Count; i++)
        {
            var product = sourceProducts[i];
            if (product is null || string.IsNullOrWhiteSpace(product.Name))
            {
                continue;
            }

            var quantity = Math.Max(1, product.Quantity);
            var unitWeightKg = OrderProductFormatter.ResolveUnitWeightKg(product);
            var totalWeightKg = OrderProductFormatter.ResolveTotalWeightKg(product);
            var dimensionsLine = string.IsNullOrWhiteSpace(product.Dimensions)
                ? string.Empty
                : $"Masse: {product.Dimensions.Trim()}";
            var weightLine = $"Gewicht: {unitWeightKg.ToString("0.##", culture)} kg/Stk";
            var totalLine = $"Gesamt: {totalWeightKg.ToString("0.##", culture)} kg";
            var deliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(product.DeliveryStatus);
            var borderColor = ResolveOrderStatusColor(deliveryStatus, isAssigned: false);
            var backgroundColor = CreateSoftStatusBackground(deliveryStatus, borderColor);

            items.Add(new DetailProductItem(
                i,
                $"{quantity}x {product.Name.Trim()}",
                (product.Supplier ?? string.Empty).Trim(),
                dimensionsLine,
                weightLine,
                totalLine,
                deliveryStatus,
                backgroundColor,
                borderColor));
        }

        return items;
    }

    private static string CreateSoftStatusBackground(string? deliveryStatus, string? statusColor)
    {
        if (string.Equals(
                OrderProductInfo.NormalizeDeliveryStatus(deliveryStatus),
                OrderProductInfo.DefaultDeliveryStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            return "#FFFFFFFF";
        }

        var normalized = (statusColor ?? string.Empty).Trim();
        if (normalized.Length == 7 && normalized.StartsWith('#'))
        {
            return $"#33{normalized[1..]}";
        }

        return "#FFF8FAFC";
    }
}

public sealed class DetailProductItem
{
    public DetailProductItem(
        int productIndex,
        string title,
        string supplier,
        string dimensionsLine,
        string weightLine,
        string totalLine,
        string deliveryStatus,
        string backgroundColor,
        string borderColor)
    {
        ProductIndex = productIndex;
        Title = title;
        Supplier = supplier ?? string.Empty;
        DimensionsLine = dimensionsLine ?? string.Empty;
        WeightLine = weightLine ?? string.Empty;
        TotalLine = totalLine ?? string.Empty;
        DeliveryStatus = deliveryStatus ?? OrderProductInfo.DefaultDeliveryStatus;
        BackgroundColor = backgroundColor ?? "#FFF8FAFC";
        BorderColor = borderColor ?? "#D7E0EC";
    }

    public int ProductIndex { get; }
    public string Title { get; }
    public string Supplier { get; }
    public string DimensionsLine { get; }
    public string WeightLine { get; }
    public string TotalLine { get; }
    public string DeliveryStatus { get; }
    public string BackgroundColor { get; }
    public string BorderColor { get; }
    public bool HasSupplier => !string.IsNullOrWhiteSpace(Supplier);
    public bool HasDimensions => !string.IsNullOrWhiteSpace(DimensionsLine);
}

public sealed class MapOrderItem
{
    public string OrderId { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string PostalCodeCity { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<string> ProductLines { get; set; } = new();
    public string TotalWeightKgText { get; set; } = string.Empty;
    public string ScheduledDate { get; set; } = string.Empty;
    public string AssignedTourId { get; set; } = string.Empty;
    public bool IsAssigned { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DeliveryLabel { get; set; } = "Frei Bordsteinkante";
    public string StatusLabel { get; set; } = Order.DefaultOrderStatus;
    public string AvisoStatusLabel { get; set; } = "nicht avisiert";
    public string TourStatusLabel { get; set; } = "Offen";
    public bool IsDimmed { get; set; }
}

public sealed class MapOrderFilterOption : ObservableObject
{
    private bool _isSelected;

    public MapOrderFilterOption(string label, bool isSelected = true)
    {
        Label = (label ?? string.Empty).Trim();
        _isSelected = isSelected;
    }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class RouteStopItem : ObservableObject
{
    private int _position;
    private int _displayIndex;
    private string _orderId = string.Empty;
    private string _customer = string.Empty;
    private string _address = string.Empty;
    private double _latitude;
    private double _longitude;
    private bool _isCompanyAnchor;
    private int _plannedStayMinutes = 10;
    private string _etaText = string.Empty;
    private string _nextLegDurationText = string.Empty;
    private string _nextLegDistanceText = string.Empty;
    private string _nextLegDepartureText = string.Empty;
    private string _nextLegArrivalText = string.Empty;
    private string _nextLegAccentColor = "#8EC6E8";

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

    public int DisplayIndex
    {
        get => _displayIndex;
        set
        {
            if (SetProperty(ref _displayIndex, value))
            {
                OnPropertyChanged(nameof(DisplayPosition));
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
                OnPropertyChanged(nameof(IsRouteStart));
                OnPropertyChanged(nameof(IsRouteEnd));
                OnPropertyChanged(nameof(RouteBadgeText));
                OnPropertyChanged(nameof(DisplayNameWithOrder));
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
                OnPropertyChanged(nameof(DisplayNameWithOrder));
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
                OnPropertyChanged(nameof(DisplayNameWithOrder));
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
                OnPropertyChanged(nameof(IsRouteStart));
                OnPropertyChanged(nameof(IsRouteEnd));
                OnPropertyChanged(nameof(RouteBadgeText));
                OnPropertyChanged(nameof(DisplayNameWithOrder));
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

    public string NextLegDurationText
    {
        get => _nextLegDurationText;
        private set
        {
            if (SetProperty(ref _nextLegDurationText, value))
            {
                OnPropertyChanged(nameof(HasNextLeg));
            }
        }
    }

    public string NextLegDistanceText
    {
        get => _nextLegDistanceText;
        private set
        {
            if (SetProperty(ref _nextLegDistanceText, value))
            {
                OnPropertyChanged(nameof(HasNextLeg));
            }
        }
    }

    public string NextLegDepartureText
    {
        get => _nextLegDepartureText;
        private set => SetProperty(ref _nextLegDepartureText, value);
    }

    public string NextLegArrivalText
    {
        get => _nextLegArrivalText;
        private set => SetProperty(ref _nextLegArrivalText, value);
    }

    public string NextLegAccentColor
    {
        get => _nextLegAccentColor;
        private set => SetProperty(ref _nextLegAccentColor, value);
    }

    private bool IsCompanyDisplay => IsCompanyAnchor ||
                                     string.Equals(OrderId, "__company_start__", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(OrderId, "__company_end__", StringComparison.OrdinalIgnoreCase) ||
                                     (OrderId ?? string.Empty).StartsWith("__company_", StringComparison.OrdinalIgnoreCase);

    public bool IsRouteStart => string.Equals(OrderId, "__company_start__", StringComparison.OrdinalIgnoreCase);
    public bool IsRouteEnd => string.Equals(OrderId, "__company_end__", StringComparison.OrdinalIgnoreCase);
    public string RouteBadgeText => IsRouteStart ? "Start" : IsRouteEnd ? "Ende" : string.Empty;
    public bool HasNextLeg => !string.IsNullOrWhiteSpace(NextLegDurationText) && !string.IsNullOrWhiteSpace(NextLegDistanceText);
    public string DisplayPosition => ToAlphaLabel(DisplayIndex > 0 ? DisplayIndex : Position);
    public string DisplayName => IsCompanyDisplay ? Address : (!string.IsNullOrWhiteSpace(Customer) ? Customer : Address);
    public string DisplayNameWithOrder =>
        IsCompanyDisplay || string.IsNullOrWhiteSpace(DisplayOrder) || string.Equals(DisplayOrder, "-", StringComparison.Ordinal)
            ? DisplayName
            : $"{DisplayName} ({DisplayOrder})";
    public string DisplayAddress => string.Equals(DisplayName, Address, StringComparison.OrdinalIgnoreCase) ? string.Empty : Address;
    public string DisplayWindow => "--";
    public string DisplayOrder => string.IsNullOrWhiteSpace(OrderId) ? "-" : OrderId;
    public string DisplayStay => IsCompanyDisplay ? string.Empty : $"{PlannedStayMinutes} min";
    public string DisplayEta => string.IsNullOrWhiteSpace(EtaText) ? "--:--" : EtaText;

    public void SetNextLeg(string durationText, string distanceText, string departureText, string arrivalText, string accentColorHex)
    {
        NextLegDurationText = durationText ?? string.Empty;
        NextLegDistanceText = distanceText ?? string.Empty;
        NextLegDepartureText = departureText ?? string.Empty;
        NextLegArrivalText = arrivalText ?? string.Empty;
        NextLegAccentColor = string.IsNullOrWhiteSpace(accentColorHex) ? "#8EC6E8" : accentColorHex;
    }

    public void ClearNextLeg()
    {
        NextLegDurationText = string.Empty;
        NextLegDistanceText = string.Empty;
        NextLegDepartureText = string.Empty;
        NextLegArrivalText = string.Empty;
        NextLegAccentColor = "#8EC6E8";
    }

    private static string ToAlphaLabel(int position)
    {
        var value = Math.Max(1, position);
        var label = string.Empty;
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            label = (char)('A' + remainder) + label;
            value = (value - 1) / 26;
        }

        return label;
    }
}

public sealed record MapOrderVisualInfo(string DeliveryLabel, string StatusLabel, bool IsAssigned, string AvisoStatusLabel);

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




