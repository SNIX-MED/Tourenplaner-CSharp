using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
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
    private const string PauseStopKind = "pause";
    private const string PauseStopIdPrefix = "pause:";
    private const int DefaultPauseMinutes = 15;
    private const int MaxDraftRouteUndoEntries = 30;
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
    private TomTomRoutingService _tomTomRoutingService;
    private readonly TourConflictService _conflictService;
    private readonly Dictionary<string, string> _employeeLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Order> _allOrders = new();
    private readonly List<TourRecord> _savedTours = new();
    private readonly List<GeoPoint> _routeGeometryPoints = new();
    private readonly List<OsrmRouteLeg> _routeLegs = new();
    private readonly List<OsrmRouteTrafficSegment> _routeTrafficSegments = new();
    private readonly List<RouteStopItem> _timedStops = new();
    private readonly List<PlannedTourRouteOverlay> _plannedTourRouteOverlays = new();
    private VehicleDataRecord _vehicleData = new();

    private string _searchText = string.Empty;
    private bool _includeOpenOrders = true;
    private bool _includePlannedOrders = true;
    private MapOrderItem? _selectedOrder;
    private RouteStopItem? _selectedRouteStop;
    private bool _preferLegSelectionVisual;
    private string _routeName = $"Tour {DateOnly.FromDateTime(DateTime.Today):dd.MM.yyyy}";
    private string _routeDate = DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy");
    private string _routeStartHour = "07";
    private string _routeStartMinute = "30";
    private string _defaultRouteStartHour = "07";
    private string _defaultRouteStartMinute = "30";
    private double _routeDistanceKm;
    private string _statusText = "Loading map orders...";
    private string _routeTimingSummary = "Noch keine Stopps geplant.";
    private string _routingProviderStatusText = "Routing: Noch nicht berechnet";
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
    private string _tomTomApiKey = string.Empty;
    private string _tomTomMapStyle = AppSettings.DefaultTomTomMapStyle;
    private bool _tomTomShowTrafficFlow = true;
    private bool _tomTomShowTrafficIncidents;
    private bool _tomTomShowRoadLabels = true;
    private bool _tomTomShowPoi = true;
    private bool _tomTomUseVehicleDimensions;
    private bool _tomTomUseVehicleWeightRestrictions;
    private bool _tomTomUseDepartAtTraffic = true;
    private string _tomTomMapOverlayStyle = AppSettings.DefaultMapOverlayStyle;
    private int _tomTomTrafficRefreshSeconds = AppSettings.DefaultTomTomTrafficRefreshSeconds;
    private int _tomTomRouteRecalcDebounceMs = AppSettings.DefaultTomTomRouteRecalcDebounceMs;
    private string _tomTomRoutingMode = AppSettings.DefaultTomTomRoutingMode;
    private double _tomTomVehicleHeightMeters = AppSettings.DefaultTomTomVehicleHeightMeters;
    private bool _tomTomEnableTileCache = true;
    private string _geocodeCachePath = string.Empty;
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
    private double _pinInfoCardZoomBehaviorStrength = AppSettings.DefaultPinInfoCardZoomBehaviorStrength;
    private bool _isRouteCalculating;
    private int _routeGeometryInFlightCount;
    private int _routeGeometryRevision;
    private int _activeTourId;
    private string _currentRouteVehicleId = string.Empty;
    private string _currentRouteTrailerId = string.Empty;
    private string _currentRouteSecondaryVehicleId = string.Empty;
    private string _currentRouteSecondaryTrailerId = string.Empty;
    private int _mapRouteCapacityWarningThresholdPercent = AppSettings.DefaultMapRouteCapacityWarningThresholdPercent;
    private bool _isAllPlannedToursVisible;
    private int _plannedTourOverlayRevision;
    private int _routeVisualRevision;
    private DateTime _lastRouteProviderCallUtc = DateTime.MinValue;
    private string _lastRouteSignature = string.Empty;
    private CancellationTokenSource? _pinInfoCardScaleAutoSaveCts;
    private CancellationTokenSource? _routeRebuildDebounceCts;
    private readonly string _routeComputationCachePath;
    private SavedTourLookupItem? _selectedSavedTour;
    private SavedTourOverviewItem? _selectedTourOverviewItem;
    private int _selectedTourOverviewId;
    private int _hoveredTourOverviewId;
    private string _detailSelectedStatus = Order.DefaultOrderStatus;
    private string _detailSelectedAvisoStatus = "nicht avisiert";
    private readonly List<MapOrderItem> _dimmedMapOrders = new();
    private readonly HashSet<string> _selectedBatchOrderIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _selectedDetailProductIndices = new();
    private readonly Stack<RouteStopRemovalUndoSnapshot> _draftRouteStopRemovalUndoStack = new();
    private CancellationTokenSource? _tourOverviewStartTimeAutoSaveCts;
    private string? _detailSelectedProductStatus;
    private bool _suppressDetailSelectedProductStatusApply;
    private bool _hasTemporarySearchPin;
    private double _temporarySearchPinLatitude;
    private double _temporarySearchPinLongitude;
    private string _temporarySearchPinLabel = string.Empty;
    private int _temporarySearchPinRevision;
    private int _searchFocusRevision;
    private readonly Guid _instanceId = Guid.NewGuid();

    public KarteSectionViewModel(
        string ordersJsonPath,
        string toursJsonPath,
        string settingsJsonPath,
        AppDataSyncService dataSyncService)
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
        _tomTomRoutingService = CreateTomTomRoutingService(null, AppSettings.DefaultTomTomRoutingMode, AppSettings.DefaultTomTomVehicleHeightMeters);
        _conflictService = new TourConflictService();
        _geocodeCachePath = Path.Combine(dataRoot, "geocode-cache.json");
        _routeComputationCachePath = Path.Combine(dataRoot, "route-computation-cache.json");

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SearchCommand = new AsyncCommand(SearchAsync);
        AddToRouteCommand = new DelegateCommand(AddSelectedOrderToRoute, () => SelectedOrder is not null);
        AddSelectedOrdersToRouteCommand = new DelegateCommand(AddSelectedOrdersToRoute, CanAddSelectedOrdersToRoute);
        RemoveOrderFromTourCommand = new AsyncCommand(RemoveSelectedOrderFromTourAsync, CanRemoveSelectedOrderFromTour);
        RemoveSelectedOrdersFromTourCommand = new AsyncCommand(RemoveSelectedOrdersFromTourAsync, CanRemoveSelectedOrdersFromTour);
        ClearSelectedOrdersCommand = new DelegateCommand(ClearSelectedBatchOrders, () => SelectedBatchOrderCount > 0);
        RemoveFromRouteCommand = new DelegateCommand(RemoveSelectedRouteStop, () => SelectedRouteStop is not null && !IsCompanyStop(SelectedRouteStop));
        MoveStopUpCommand = new DelegateCommand(MoveSelectedStopUp, () => CanMoveSelectedStop(-1));
        MoveStopDownCommand = new DelegateCommand(MoveSelectedStopDown, () => CanMoveSelectedStop(1));
        OptimizeRouteCommand = new AsyncCommand(OptimizeRouteAsync, CanOptimizeRoute);
        OpenCreateTourDialogCommand = new AsyncCommand(OpenCreateTourDialogAsync);
        EditSelectedTourCommand = new AsyncCommand(OpenEditSelectedTourDialogAsync, CanEditOrLeaveSelectedTour);
        ExportRouteCommand = new AsyncCommand(ExportRouteAsync, CanExportRoute);
        SaveRouteAsTourCommand = new AsyncCommand(SaveRouteAsTourAsync, () => RouteStops.Any(x => !IsCompanyStop(x)));
        SaveCurrentTourCommand = new AsyncCommand(SaveCurrentTourAsync, CanSaveCurrentTour);
        DeleteSelectedTourCommand = new AsyncCommand(DeleteSelectedTourAsync, CanEditOrLeaveSelectedTour);
        OpenSelectedTourOverviewCommand = new AsyncCommand(OpenSelectedTourOverviewAsync, CanOpenSelectedTourOverview);
        ClearRouteCommand = new DelegateCommand(ClearRoute, () => RouteStops.Any(x => !IsCompanyStop(x)));
        LeaveSelectedTourCommand = new DelegateCommand(LeaveSelectedTour, CanLeaveSelectedTour);
        PreviousTourCommand = new DelegateCommand(SwitchToPreviousTour, CanSwitchToPreviousTour);
        NextTourCommand = new DelegateCommand(SwitchToNextTour, CanSwitchToNextTour);
        ApplyStartTimeCommand = new DelegateCommand(ApplyRouteStartTime);
        ToggleDetailsPanelCommand = new AsyncCommand(ToggleDetailsPanelAsync);
        CloseDetailsCommand = new DelegateCommand(CloseDetails, () => SelectedOrder is not null);
        ResetOrderFiltersCommand = new DelegateCommand(ResetOrderFilters);
        ToggleAllOrderFiltersCommand = new DelegateCommand(ToggleAllOrderFilters);
        TogglePinInfoCardsCommand = new DelegateCommand(TogglePinInfoCards);
        ToggleAllPlannedToursCommand = new AsyncCommand(ToggleAllPlannedToursAsync);
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
    public ObservableCollection<SavedTourOverviewItem> TourOverviewItems { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }

    public ICommand AddToRouteCommand { get; }

    public ICommand AddSelectedOrdersToRouteCommand { get; }

    public ICommand RemoveOrderFromTourCommand { get; }

    public ICommand RemoveSelectedOrdersFromTourCommand { get; }

    public ICommand ClearSelectedOrdersCommand { get; }

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

    public ICommand OpenSelectedTourOverviewCommand { get; }

    public ICommand ClearRouteCommand { get; }

    public ICommand LeaveSelectedTourCommand { get; }

    public ICommand PreviousTourCommand { get; }

    public ICommand NextTourCommand { get; }

    public ICommand ApplyStartTimeCommand { get; }

    public ICommand ToggleDetailsPanelCommand { get; }

    public ICommand CloseDetailsCommand { get; }

    public ICommand ResetOrderFiltersCommand { get; }

    public ICommand ToggleAllOrderFiltersCommand { get; }

    public ICommand TogglePinInfoCardsCommand { get; }

    public ICommand ToggleAllPlannedToursCommand { get; }

    public ICommand SendEmailCommand { get; }

    public ICommand ShowSelectedOrderTourCommand { get; }

    public ICommand EditOrderCommand { get; }

    public bool CanUndoDraftRouteStopRemoval => _draftRouteStopRemovalUndoStack.Count > 0;

    public ObservableCollection<MapOrderFilterOption> OrderStatusFilters { get; } = new();

    public ObservableCollection<MapOrderFilterOption> DeliveryTypeFilters { get; } = new();

    public ObservableCollection<MapOrderFilterOption> AvisoStatusFilters { get; } = new();

    public string FilterSummaryText => BuildFilterSummaryText();
    public int SelectedBatchOrderCount => _selectedBatchOrderIds.Count;
    public bool HasSelectedBatchOrders => SelectedBatchOrderCount > 0;
    public string SelectedBatchOrderSummary => $"{SelectedBatchOrderCount} Auftrag/Aufträge markiert";

    public string PinInfoCardsButtonText => ArePinInfoCardsVisible ? "Infokarten ausblenden" : "Infokarten anzeigen";
    public string PinInfoCardsIconGlyph => ArePinInfoCardsVisible ? "\uE8A7" : "\uE7B3";
    public string PinInfoCardsImagePath => ArePinInfoCardsVisible
        ? "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/icon-infocards-off.jpg"
        : "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/icon-infocards-on.jpg";
    public string PinInfoCardScalePercentText => $"{Math.Round(PinInfoCardScale * 100d):0}%";
    public string PinInfoCardZoomBehaviorStrengthText => $"{PinInfoCardZoomBehaviorStrength:0.00}x";
    public string ToggleAllPlannedToursButtonText => IsAllPlannedToursVisible ? "Touren ausblenden" : "Alle Touren anzeigen";
    public string ToggleAllPlannedToursImagePath => IsAllPlannedToursVisible
        ? "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/Touren-ausblenden.png"
        : "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/Touren-einblenden.png";

    public string ToggleAllFiltersButtonText => AreAllFiltersSelected() ? "Alle abwählen" : "Alle auswählen";

    public string LegendStatusColorNotSpecified => _statusColorNotSpecified;
    public string LegendStatusColorOrdered => _statusColorOrdered;
    public string LegendStatusColorOnTheWay => _statusColorOnTheWay;
    public string LegendStatusColorInStock => _statusColorInStock;
    public string LegendStatusColorPlanned => _statusColorPlanned;
    public string LegendAvisoBadgeColorNotAvisiert => AvisoBadgeColorNotAvisiert;
    public string LegendAvisoBadgeColorInformiert => AvisoBadgeColorInformiert;
    public string LegendAvisoBadgeColorBestaetigt => AvisoBadgeColorBestaetigt;
    public bool ShowRouteStopsPanel => _activeTourId > 0 || RouteStops.Any(x => !IsCompanyStop(x));
    public bool ShowTourOverviewPanel => !ShowRouteStopsPanel;
    public bool IsRouteCalculating
    {
        get => _isRouteCalculating;
        private set
        {
            if (SetProperty(ref _isRouteCalculating, value))
            {
                RaiseCommandStates();
            }
        }
    }
    public string CurrentStopViewTourName => ResolveCurrentStopViewTourName();

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

                NotifyRoutePanelVisibilityChanged();
            }
        }
    }

    public SavedTourOverviewItem? SelectedTourOverviewItem
    {
        get => _selectedTourOverviewItem;
        set
        {
            if (!SetProperty(ref _selectedTourOverviewItem, value))
            {
                return;
            }

            ApplyTourOverviewSelection(value);
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

    public bool IsAllPlannedToursVisible
    {
        get => _isAllPlannedToursVisible;
        private set
        {
            if (SetProperty(ref _isAllPlannedToursVisible, value))
            {
                OnPropertyChanged(nameof(ToggleAllPlannedToursButtonText));
                OnPropertyChanged(nameof(ToggleAllPlannedToursImagePath));
            }
        }
    }

    public int PlannedTourOverlayRevision
    {
        get => _plannedTourOverlayRevision;
        private set => SetProperty(ref _plannedTourOverlayRevision, value);
    }

    public int PlannedTourOverlayHighlightTourId => ShowTourOverviewPanel
        ? (_hoveredTourOverviewId > 0 ? _hoveredTourOverviewId : _selectedTourOverviewId)
        : 0;

    public int RouteVisualRevision
    {
        get => _routeVisualRevision;
        private set => SetProperty(ref _routeVisualRevision, value);
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
                RequestPinInfoCardScaleSave();
            }
        }
    }

    public double PinInfoCardZoomBehaviorStrength
    {
        get => _pinInfoCardZoomBehaviorStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.2d, 4.0d);
            if (SetProperty(ref _pinInfoCardZoomBehaviorStrength, clamped))
            {
                OnPropertyChanged(nameof(PinInfoCardZoomBehaviorStrengthText));
                RequestPinInfoCardScaleSave();
            }
        }
    }

    private async Task SearchAsync()
    {
        var query = (_searchText ?? string.Empty).Trim();
        RebuildOrderGrid();
        if (string.IsNullOrWhiteSpace(query))
        {
            ClearTemporarySearchPin();
            return;
        }

        var localExactMatch = FindExactLocalOrderMatch(query);
        if (localExactMatch is not null)
        {
            SelectedOrder = localExactMatch;
            _searchFocusRevision++;
            OnPropertyChanged(nameof(SearchFocusRevision));
            ClearTemporarySearchPin();
            StatusText = $"Auftrag {localExactMatch.OrderId} gefunden.";
            return;
        }

        var geocoded = await AddressGeocodingService.TryGeocodeAddressAsync(
            street: string.Empty,
            postalCode: string.Empty,
            city: string.Empty,
            fallbackAddress: query,
            tomTomApiKey: _tomTomApiKey,
            cacheFilePath: _geocodeCachePath);

        if (geocoded is not null)
        {
            SetTemporarySearchPin(geocoded.Latitude, geocoded.Longitude, query);
            StatusText = $"Adresse gefunden: {query}";
            return;
        }

        var localContainsMatch = FindBestLocalSearchMatch(query);
        if (localContainsMatch is not null)
        {
            SetTemporarySearchPin(localContainsMatch.Latitude, localContainsMatch.Longitude, localContainsMatch.Address);
            StatusText = $"Lokaler Treffer auf Karte markiert: {localContainsMatch.OrderId}";
            return;
        }

        ClearTemporarySearchPin();
        StatusText = $"Kein Treffer für \"{query}\" gefunden.";
    }

    private MapOrderItem? FindExactLocalOrderMatch(string query)
    {
        var normalized = query.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var exact = MapOrders.FirstOrDefault(x =>
            string.Equals((x.OrderId ?? string.Empty).Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var orderModel = _allOrders.FirstOrDefault(x =>
            string.Equals((x.Id ?? string.Empty).Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        if (orderModel is null)
        {
            return null;
        }

        return MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, orderModel.Id, StringComparison.OrdinalIgnoreCase))
            ?? BuildMapOrderItem(orderModel);
    }

    private MapOrderItem? FindBestLocalSearchMatch(string query)
    {
        var normalized = query.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var exact = MapOrders.FirstOrDefault(x =>
            string.Equals((x.OrderId ?? string.Empty).Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var contains = MapOrders.FirstOrDefault(x =>
            (x.OrderId ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (x.Customer ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (x.Address ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase));
        if (contains is not null)
        {
            return contains;
        }

        var orderModel = _allOrders.FirstOrDefault(x =>
            (x.Id ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (x.CustomerName ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (x.Address ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (x.DeliveryAddress?.Street ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (x.DeliveryAddress?.PostalCode ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            (x.DeliveryAddress?.City ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase));
        if (orderModel is null)
        {
            return null;
        }

        return MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, orderModel.Id, StringComparison.OrdinalIgnoreCase))
            ?? BuildMapOrderItem(orderModel);
    }

    private void SetTemporarySearchPin(double latitude, double longitude, string label)
    {
        _hasTemporarySearchPin = true;
        _temporarySearchPinLatitude = latitude;
        _temporarySearchPinLongitude = longitude;
        _temporarySearchPinLabel = (label ?? string.Empty).Trim();
        _temporarySearchPinRevision++;
        OnPropertyChanged(nameof(HasTemporarySearchPin));
        OnPropertyChanged(nameof(TemporarySearchPinLatitude));
        OnPropertyChanged(nameof(TemporarySearchPinLongitude));
        OnPropertyChanged(nameof(TemporarySearchPinLabel));
        OnPropertyChanged(nameof(TemporarySearchPinRevision));
    }

    private void ClearTemporarySearchPin()
    {
        if (!_hasTemporarySearchPin && _temporarySearchPinRevision == 0)
        {
            return;
        }

        _hasTemporarySearchPin = false;
        _temporarySearchPinLatitude = 0d;
        _temporarySearchPinLongitude = 0d;
        _temporarySearchPinLabel = string.Empty;
        _temporarySearchPinRevision++;
        OnPropertyChanged(nameof(HasTemporarySearchPin));
        OnPropertyChanged(nameof(TemporarySearchPinLatitude));
        OnPropertyChanged(nameof(TemporarySearchPinLongitude));
        OnPropertyChanged(nameof(TemporarySearchPinLabel));
        OnPropertyChanged(nameof(TemporarySearchPinRevision));
    }

    public bool HasTemporarySearchPin => _hasTemporarySearchPin;
    public double TemporarySearchPinLatitude => _temporarySearchPinLatitude;
    public double TemporarySearchPinLongitude => _temporarySearchPinLongitude;
    public string TemporarySearchPinLabel => _temporarySearchPinLabel;
    public int TemporarySearchPinRevision => _temporarySearchPinRevision;
    public int SearchFocusRevision => _searchFocusRevision;

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
                ScheduleTourOverviewStartTimeAutoSave();
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
                ScheduleTourOverviewStartTimeAutoSave();
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

    public string RoutingProviderStatusText
    {
        get => _routingProviderStatusText;
        private set => SetProperty(ref _routingProviderStatusText, value);
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
                ClearDetailProductSelection(raiseDetailItemsChanged: false);
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
                UpdateRouteSelectionVisuals(value, _preferLegSelectionVisual);
                _preferLegSelectionVisual = false;
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
        _pinInfoCardScale = settings.PinInfoCardScale is >= 0.7d and <= 1.8d
            ? settings.PinInfoCardScale
            : AppSettings.DefaultPinInfoCardScale;
        _pinInfoCardZoomBehaviorStrength = settings.PinInfoCardZoomBehaviorStrength is >= 0.2d and <= 4.0d
            ? settings.PinInfoCardZoomBehaviorStrength
            : AppSettings.DefaultPinInfoCardZoomBehaviorStrength;
        _mapRouteCapacityWarningThresholdPercent = settings.MapRouteCapacityWarningThresholdPercent is < 0 or > 100
            ? AppSettings.DefaultMapRouteCapacityWarningThresholdPercent
            : settings.MapRouteCapacityWarningThresholdPercent;
        _tomTomApiKey = (settings.TomTomApiKey ?? string.Empty).Trim();
        _tomTomMapStyle = NormalizeTomTomMapStyle(settings.TomTomMapStyle);
        _tomTomShowTrafficFlow = settings.TomTomShowTrafficFlow;
        _tomTomTrafficRefreshSeconds = settings.TomTomTrafficRefreshSeconds < 15 ? AppSettings.DefaultTomTomTrafficRefreshSeconds : settings.TomTomTrafficRefreshSeconds;
        _tomTomRouteRecalcDebounceMs = settings.TomTomRouteRecalcDebounceMs is < 100 or > 10000
            ? AppSettings.DefaultTomTomRouteRecalcDebounceMs
            : settings.TomTomRouteRecalcDebounceMs;
        _tomTomRoutingMode = NormalizeTomTomRoutingMode(settings.TomTomRoutingMode);
        _tomTomVehicleHeightMeters = settings.TomTomVehicleHeightMeters is < 0d or > 20d
            ? AppSettings.DefaultTomTomVehicleHeightMeters
            : settings.TomTomVehicleHeightMeters;
        _tomTomEnableTileCache = settings.TomTomEnableTileCache;
        var currentUserName = ResolveCurrentSettingsUserName(settings);
        settings.MapOverlayPreferencesByUser ??= new Dictionary<string, MapOverlayUserPreference>(StringComparer.OrdinalIgnoreCase);
        if (settings.MapOverlayPreferencesByUser.TryGetValue(currentUserName, out var userPreference) && userPreference is not null)
        {
            _tomTomMapOverlayStyle = NormalizeMapOverlayStyle(userPreference.Style);
            _tomTomShowTrafficFlow = userPreference.ShowTrafficFlow;
            _tomTomShowTrafficIncidents = userPreference.ShowTrafficIncidents;
            _tomTomShowRoadLabels = userPreference.ShowRoadLabels;
            _tomTomShowPoi = userPreference.ShowPoi;
            _tomTomUseVehicleDimensions = userPreference.UseVehicleDimensions;
            _tomTomUseVehicleWeightRestrictions = userPreference.UseVehicleWeightRestrictions;
            _tomTomUseDepartAtTraffic = userPreference.UseDepartAtTraffic;
        }
        else
        {
            _tomTomMapOverlayStyle = _tomTomShowTrafficFlow ? "standard" : AppSettings.DefaultMapOverlayStyle;
            _tomTomShowTrafficIncidents = false;
            _tomTomShowRoadLabels = true;
            _tomTomShowPoi = true;
            _tomTomUseVehicleDimensions = false;
            _tomTomUseVehicleWeightRestrictions = false;
            _tomTomUseDepartAtTraffic = true;
        }
        _tomTomRoutingService = CreateTomTomRoutingService(_tomTomApiKey, _tomTomRoutingMode, _tomTomVehicleHeightMeters);
        OnPropertyChanged(nameof(TomTomApiKey));
        OnPropertyChanged(nameof(TomTomMapStyle));
        OnPropertyChanged(nameof(TomTomShowTrafficFlow));
        OnPropertyChanged(nameof(TomTomShowTrafficIncidents));
        OnPropertyChanged(nameof(TomTomShowRoadLabels));
        OnPropertyChanged(nameof(TomTomShowPoi));
        OnPropertyChanged(nameof(TomTomUseVehicleDimensions));
        OnPropertyChanged(nameof(TomTomUseVehicleWeightRestrictions));
        OnPropertyChanged(nameof(TomTomUseDepartAtTraffic));
        OnPropertyChanged(nameof(TomTomMapOverlayStyle));
        OnPropertyChanged(nameof(TomTomTrafficRefreshSeconds));
        OnPropertyChanged(nameof(TomTomRouteRecalcDebounceMs));
        OnPropertyChanged(nameof(TomTomRoutingMode));
        OnPropertyChanged(nameof(TomTomVehicleHeightMeters));
        OnPropertyChanged(nameof(TomTomEnableTileCache));
        OnPropertyChanged(nameof(MapPinInfoCardShowName));
        OnPropertyChanged(nameof(MapPinInfoCardShowOrderNumber));
        OnPropertyChanged(nameof(MapPinInfoCardShowStreet));
        OnPropertyChanged(nameof(MapPinInfoCardShowPostalCodeCity));
        OnPropertyChanged(nameof(MapPinInfoCardShowNotes));
        OnPropertyChanged(nameof(MapPinInfoCardShowProducts));
        OnPropertyChanged(nameof(MapPinInfoCardShowTotalWeight));
        OnPropertyChanged(nameof(PinInfoCardScale));
        OnPropertyChanged(nameof(PinInfoCardScalePercentText));
        OnPropertyChanged(nameof(PinInfoCardZoomBehaviorStrength));
        OnPropertyChanged(nameof(PinInfoCardZoomBehaviorStrengthText));
        var (defaultHour, defaultMinute) = ParseStartTimePartsOrDefault(settings.TourDefaultStartTime);
        _defaultRouteStartHour = defaultHour;
        _defaultRouteStartMinute = defaultMinute;
        NotifyLegendColorsChanged();
        IsDetailsPanelExpanded = settings.MapDetailsPanelExpanded;
        _companyLocation = await AddressGeocodingService.TryGeocodeAddressAsync(
            settings.CompanyStreet,
            settings.CompanyPostalCode,
            settings.CompanyCity,
            _companyAddress,
            _tomTomApiKey,
            _geocodeCachePath);
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
        RebuildPlannedTourRouteOverlays();
        RequestRouteGeometryRebuild();
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
    public IReadOnlyList<string> DetailProductDeliveryStatusOptions => OrderProductInfo.DeliveryStatusOptions;
    public string? DetailSelectedProductStatus
    {
        get => _detailSelectedProductStatus;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? null
                : OrderProductInfo.NormalizeDeliveryStatus(value);
            if (!SetProperty(ref _detailSelectedProductStatus, normalized))
            {
                return;
            }

            if (_suppressDetailSelectedProductStatusApply)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(normalized) || _selectedDetailProductIndices.Count == 0)
            {
                return;
            }

            _ = ApplySelectedDetailProductStatusAsync(normalized);
        }
    }
    public bool HasSelectedDetailProducts => _selectedDetailProductIndices.Count > 0;
    public string DetailSelectedProductsSummary => HasSelectedDetailProducts
        ? $"{_selectedDetailProductIndices.Count} Produkt(e) ausgewählt"
        : "Ctrl + Linksklick für Mehrfachauswahl";
    public string DetailEmail => FindSelectedOrderModel()?.Email ?? "n/a";
    public string DetailPhone => FindSelectedOrderModel()?.Phone ?? "n/a";
    public string DetailDeliveryType => FindSelectedOrderModel()?.DeliveryType ?? SelectedOrder?.DeliveryLabel ?? "Frei Bordsteinkante";
    public string DetailNotes => NormalizeUiText(FindSelectedOrderModel()?.Notes);

    public CompanyMarkerInfo? CompanyMarker =>
        _companyLocation is null
            ? null
            : new CompanyMarkerInfo(_companyName, _companyAddress, _companyLocation.Latitude, _companyLocation.Longitude);

public string TomTomApiKey => _tomTomApiKey;
public string TomTomMapStyle => _tomTomMapStyle;
public bool TomTomShowTrafficFlow => _tomTomShowTrafficFlow;
public bool TomTomShowTrafficIncidents => _tomTomShowTrafficIncidents;
public bool TomTomShowRoadLabels => _tomTomShowRoadLabels;
public bool TomTomShowPoi => _tomTomShowPoi;
public bool TomTomUseVehicleDimensions => _tomTomUseVehicleDimensions;
public bool TomTomUseVehicleWeightRestrictions => _tomTomUseVehicleWeightRestrictions;
public bool TomTomUseDepartAtTraffic => _tomTomUseDepartAtTraffic;
public string TomTomMapOverlayStyle => _tomTomMapOverlayStyle;
public int TomTomTrafficRefreshSeconds => _tomTomTrafficRefreshSeconds;
    public int TomTomRouteRecalcDebounceMs => _tomTomRouteRecalcDebounceMs;
    public string TomTomRoutingMode => _tomTomRoutingMode;
    public double TomTomVehicleHeightMeters => _tomTomVehicleHeightMeters;
    public bool TomTomEnableTileCache => _tomTomEnableTileCache;

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
            .Where(IsOrderStop)
            .Select(x => new RouteStopItem
        {
            Position = x.Position,
            DisplayIndex = x.DisplayIndex,
            OrderId = x.OrderId,
            Customer = x.Customer,
            Address = x.Address,
            Latitude = x.Latitude,
            Longitude = x.Longitude,
            PlannedStayMinutes = x.PlannedStayMinutes,
            EmployeeInfoText = x.EmployeeInfoText
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

    public IReadOnlyList<OsrmRouteTrafficSegment> GetRouteTrafficSegmentSnapshot()
    {
        return _routeTrafficSegments.ToList();
    }

    public string GetActiveRoutePolylineColor()
    {
        var totalWeightKg = RouteStops
            .Where(x => !IsCompanyStop(x))
            .Select(x => FindOrderWeightKg(x.OrderId))
            .Sum();
        var assignments = BuildVehicleAssignments(
            _currentRouteVehicleId,
            _currentRouteTrailerId,
            _currentRouteSecondaryVehicleId,
            _currentRouteSecondaryTrailerId);
        return ResolveRoutePolylineColorHex(totalWeightKg, assignments);
    }

    public IReadOnlyList<PlannedTourRouteOverlay> GetPlannedTourRouteOverlaySnapshot()
    {
        if (!IsAllPlannedToursVisible)
        {
            return [];
        }

        return _plannedTourRouteOverlays
            .Select(x => x.Clone())
            .ToList();
    }

    public void SelectRouteStopByOrderId(string orderId)
    {
        var resolvedOrderId = ResolveCanonicalOrderId(orderId);
        var match = RouteStops.FirstOrDefault(x =>
            string.Equals(x.OrderId, resolvedOrderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _preferLegSelectionVisual = false;
            SelectedRouteStop = match;
            if (ReferenceEquals(SelectedRouteStop, match))
            {
                UpdateRouteSelectionVisuals(match, selectLeg: false);
            }
        }
        else
        {
            UpdateRouteSelectionVisuals(null, selectLeg: false);
        }

        SelectOrderDetailsById(resolvedOrderId);
    }

    public void SelectRouteLegByOrderId(string orderId)
    {
        var resolvedOrderId = ResolveCanonicalOrderId(orderId);
        var match = RouteStops.FirstOrDefault(x =>
            string.Equals(x.OrderId, resolvedOrderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _preferLegSelectionVisual = true;
            SelectedRouteStop = match;
            UpdateRouteSelectionVisuals(match, selectLeg: true);
        }
        else
        {
            UpdateRouteSelectionVisuals(null, selectLeg: true);
        }
    }

    public void SelectOrderDetailsByOrderId(string? orderId)
    {
        SelectOrderDetailsById(orderId);
    }

    public async Task FocusTourAsync(int tourId)
    {
        await LoadSavedToursAsync(tourId);
        await LoadTourIntoRouteAsync(tourId);
    }

    public void ToggleBatchOrderSelectionById(string? orderId)
    {
        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
        {
            return;
        }

        var mapOrder = GetMapMarkerSnapshot()
            .FirstOrDefault(x => string.Equals(x.OrderId, normalizedOrderId, StringComparison.OrdinalIgnoreCase));
        if (mapOrder is null)
        {
            return;
        }

        if (_selectedBatchOrderIds.Contains(normalizedOrderId))
        {
            _selectedBatchOrderIds.Remove(normalizedOrderId);
        }
        else
        {
            _selectedBatchOrderIds.Add(normalizedOrderId);
        }

        if (SelectedOrder is null ||
            !string.Equals(SelectedOrder.OrderId, normalizedOrderId, StringComparison.OrdinalIgnoreCase))
        {
            SelectOrderDetailsById(normalizedOrderId);
        }

        NotifyBatchOrderSelectionChanged();
        RebuildOrderGrid(normalizedOrderId);
    }

    public void SetHoveredTourOverviewId(int tourId)
    {
        var normalized = tourId > 0 ? tourId : 0;
        if (_hoveredTourOverviewId == normalized)
        {
            return;
        }

        _hoveredTourOverviewId = normalized;
        OnPropertyChanged(nameof(PlannedTourOverlayHighlightTourId));
    }

    public void SelectTourOverviewById(int tourId)
    {
        if (tourId <= 0)
        {
            return;
        }

        var target = TourOverviewItems.FirstOrDefault(x => x.TourId == tourId);
        if (target is null)
        {
            return;
        }

        SelectedTourOverviewItem = target;
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

    public async Task EditSelectedRouteStopStayMinutesAsync()
    {
        if (SelectedRouteStop is null || IsCompanyStop(SelectedRouteStop))
        {
            return;
        }

        var order = IsPauseStop(SelectedRouteStop)
            ? null
            : _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedRouteStop.OrderId, StringComparison.OrdinalIgnoreCase));
        var currentAvisoStatus = NormalizeAvisoStatus(order?.AvisoStatus);
        var dialog = new RouteStopStayMinutesDialogWindow(
            SelectedRouteStop.PlannedStayMinutes,
            _avisoStatusOptions,
            currentAvisoStatus,
            SelectedRouteStop.EmployeeInfoText)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.StayMinutes is null)
        {
            return;
        }

        SelectedRouteStop.PlannedStayMinutes = dialog.StayMinutes.Value;
        SelectedRouteStop.EmployeeInfoText = dialog.EmployeeInfoText;

        var selectedAvisoStatus = NormalizeAvisoStatus(dialog.SelectedAvisoStatus);
        if (order is not null &&
            !string.Equals(NormalizeAvisoStatus(order.AvisoStatus), selectedAvisoStatus, StringComparison.OrdinalIgnoreCase))
        {
            order.AvisoStatus = selectedAvisoStatus;
            if (SelectedOrder is not null &&
                string.Equals(SelectedOrder.OrderId, order.Id, StringComparison.OrdinalIgnoreCase))
            {
                SelectedOrder.AvisoStatusLabel = selectedAvisoStatus;
            }

            await _orderRepository.SaveAllAsync(_allOrders);
            RebuildOrderGrid(order.Id);
            OnPropertyChanged(nameof(RouteStops));
            OnPropertyChanged(nameof(DetailAvisoStatus));
            PublishOrderChange(order.Id, order.Id);
        }
        RefreshDriveTimesFromCurrentRoute();
        MarkRouteChanged();
        StatusText = IsPauseStop(SelectedRouteStop)
            ? $"Pausendauer gespeichert: {SelectedRouteStop.PlannedStayMinutes} min."
            : $"Stoppdaten für Auftrag {SelectedRouteStop.OrderId} gespeichert (Aufenthalt {SelectedRouteStop.PlannedStayMinutes} min, Aviso {selectedAvisoStatus}).";

    }

    public Task AddPauseAfterSelectedRouteStopAsync()
    {
        if (SelectedRouteStop is null || IsCompanyStop(SelectedRouteStop))
        {
            return Task.CompletedTask;
        }

        var dialog = new RouteStopStayMinutesDialogWindow(DefaultPauseMinutes)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            Title = "Pause einfügen"
        };

        if (dialog.ShowDialog() != true || dialog.StayMinutes is null)
        {
            return Task.CompletedTask;
        }

        var sourceIndex = RouteStops.IndexOf(SelectedRouteStop);
        if (sourceIndex < 0)
        {
            return Task.CompletedTask;
        }

        var insertIndex = Math.Min(sourceIndex + 1, Math.Max(0, RouteStops.Count - 1));
        var pauseStop = new RouteStopItem
        {
            OrderId = $"{PauseStopIdPrefix}{Guid.NewGuid():N}",
            Customer = "Pause",
            Address = string.Empty,
            Latitude = double.NaN,
            Longitude = double.NaN,
            PlannedStayMinutes = Math.Max(0, dialog.StayMinutes.Value),
            IsPauseStop = true,
            EmployeeInfoText = string.Empty
        };

        RouteStops.Insert(insertIndex, pauseStop);
        SelectedRouteStop = RouteStops[Math.Max(0, sourceIndex)];
        RebuildPositions();
        MarkRouteChanged();
        StatusText = $"Pause mit {pauseStop.PlannedStayMinutes} min eingefügt.";
        return Task.CompletedTask;
    }

    public Task EditPauseAfterSelectedRouteStopAsync()
    {
        if (SelectedRouteStop is null || IsCompanyStop(SelectedRouteStop))
        {
            return Task.CompletedTask;
        }

        var pauses = GetPauseStopsAfter(SelectedRouteStop);
        if (pauses.Count == 0)
        {
            return Task.CompletedTask;
        }

        var currentMinutes = pauses.Sum(x => Math.Max(0, x.PlannedStayMinutes));
        var dialog = new RouteStopStayMinutesDialogWindow(currentMinutes)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            Title = "Pause bearbeiten"
        };

        if (dialog.ShowDialog() != true || dialog.StayMinutes is null)
        {
            return Task.CompletedTask;
        }

        var targetMinutes = Math.Max(0, dialog.StayMinutes.Value);
        pauses[0].PlannedStayMinutes = targetMinutes;
        for (var i = pauses.Count - 1; i >= 1; i--)
        {
            RouteStops.Remove(pauses[i]);
        }

        RebuildPositions();
        MarkRouteChanged();
        StatusText = $"Pause auf {targetMinutes} min geändert.";
        return Task.CompletedTask;
    }

    public void RemovePauseAfterSelectedRouteStop()
    {
        if (SelectedRouteStop is null || IsCompanyStop(SelectedRouteStop))
        {
            return;
        }

        var pauses = GetPauseStopsAfter(SelectedRouteStop);
        if (pauses.Count == 0)
        {
            return;
        }

        foreach (var pause in pauses)
        {
            RouteStops.Remove(pause);
        }

        RebuildPositions();
        MarkRouteChanged();
        StatusText = "Pause entfernt.";
    }

    public bool HasPauseAfterSelectedRouteStop()
    {
        return SelectedRouteStop is not null &&
               !IsCompanyStop(SelectedRouteStop) &&
               GetPauseStopsAfter(SelectedRouteStop).Count > 0;
    }

    private List<RouteStopItem> GetPauseStopsAfter(RouteStopItem routeStop)
    {
        var pauses = new List<RouteStopItem>();
        var startIndex = RouteStops.IndexOf(routeStop);
        if (startIndex < 0)
        {
            return pauses;
        }

        for (var i = startIndex + 1; i < RouteStops.Count; i++)
        {
            var candidate = RouteStops[i];
            if (IsPauseStop(candidate))
            {
                pauses.Add(candidate);
                continue;
            }

            break;
        }

        return pauses;
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
            .Where(o => !o.IsArchived)
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

        var validOrderIds = _allOrders
            .Select(x => (x.Id ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_selectedBatchOrderIds.RemoveWhere(x => !validOrderIds.Contains(x)) > 0)
        {
            NotifyBatchOrderSelectionChanged(raiseCommandStates: false);
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
                .Where(o => !o.IsArchived)
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

        if (!TryAddOrderToDraftRoute(SelectedOrder))
        {
            var existing = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, SelectedOrder.OrderId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SelectedRouteStop = existing;
            }
            return;
        }

        RebuildPositions();
        RebuildOrderGrid();
        MarkRouteChanged();
    }

    private bool TryAddOrderToDraftRoute(MapOrderItem order)
    {
        var existing = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, order.OrderId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return false;
        }

        var item = new RouteStopItem
        {
            Position = 0,
            OrderId = order.OrderId,
            Customer = order.Customer,
            Address = order.Address,
            Latitude = order.Latitude,
            Longitude = order.Longitude,
            PlannedStayMinutes = 10
        };

        var endIndex = RouteStops
            .Select((stop, index) => new { stop, index })
            .FirstOrDefault(x => IsCompanyStop(x.stop) && string.Equals(x.stop.OrderId, CompanyEndStopId, StringComparison.OrdinalIgnoreCase))
            ?.index ?? RouteStops.Count;

        RouteStops.Insert(endIndex, item);
        return true;
    }

    private IReadOnlyList<MapOrderItem> GetSelectedBatchMapOrders()
    {
        if (_selectedBatchOrderIds.Count == 0)
        {
            return [];
        }

        return GetMapMarkerSnapshot()
            .Where(x => _selectedBatchOrderIds.Contains(x.OrderId))
            .GroupBy(x => x.OrderId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private bool CanAddSelectedOrdersToRoute()
    {
        return GetSelectedBatchMapOrders().Count > 0;
    }

    private bool CanRemoveSelectedOrdersFromTour()
    {
        if (_selectedBatchOrderIds.Count == 0)
        {
            return false;
        }

        foreach (var orderId in _selectedBatchOrderIds)
        {
            var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase));
            if (order is not null && IsOrderAssignedOrInDraftRoute(order))
            {
                return true;
            }

            if (RouteStops.Any(x => !IsCompanyStop(x) && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private void ClearSelectedBatchOrders()
    {
        if (_selectedBatchOrderIds.Count == 0)
        {
            return;
        }

        _selectedBatchOrderIds.Clear();
        NotifyBatchOrderSelectionChanged();
        RebuildOrderGrid(SelectedOrder?.OrderId);
    }

    private void NotifyBatchOrderSelectionChanged(bool raiseCommandStates = true)
    {
        OnPropertyChanged(nameof(SelectedBatchOrderCount));
        OnPropertyChanged(nameof(HasSelectedBatchOrders));
        OnPropertyChanged(nameof(SelectedBatchOrderSummary));
        if (raiseCommandStates)
        {
            RaiseCommandStates();
        }
    }

    private bool ConfirmBatchReassignmentIfNeeded(IReadOnlyList<MapOrderItem> orders)
    {
        var targetTourId = ResolveCurrentTourId();
        var reassignedOrders = orders
            .Where(order =>
            {
                var assignedTourIdText = (order.AssignedTourId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(assignedTourIdText))
                {
                    return false;
                }

                if (!int.TryParse(assignedTourIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var assignedTourId) || assignedTourId <= 0)
                {
                    return true;
                }

                return assignedTourId != targetTourId;
            })
            .ToList();

        if (reassignedOrders.Count == 0)
        {
            return true;
        }

        var targetTourLabel = targetTourId > 0
            ? BuildTourLabelById(targetTourId.ToString(CultureInfo.InvariantCulture))
            : "die aktuelle neue Route";
        var message =
            $"{reassignedOrders.Count} markierte Aufträge sind bereits anderen Touren zugeordnet.\n\n" +
            $"Beim Speichern werden diese nach {targetTourLabel} umgeplant.\n\n" +
            "Trotzdem alle markierten Aufträge hinzufügen?";

        var result = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            message,
            "Aufträge bereits eingeplant",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void AddSelectedOrdersToRoute()
    {
        var selectedOrders = GetSelectedBatchMapOrders();
        if (selectedOrders.Count == 0)
        {
            return;
        }

        if (!ConfirmBatchReassignmentIfNeeded(selectedOrders))
        {
            return;
        }

        var added = 0;
        var alreadyInRoute = 0;
        foreach (var order in selectedOrders)
        {
            if (TryAddOrderToDraftRoute(order))
            {
                added++;
            }
            else
            {
                alreadyInRoute++;
            }
        }

        if (added > 0)
        {
            RebuildPositions();
            RebuildOrderGrid();
            MarkRouteChanged();
        }

        StatusText = added > 0
            ? $"{added} Auftrag/Aufträge zur Route hinzugefügt. Bereits vorhanden: {alreadyInRoute}."
            : "Alle markierten Aufträge sind bereits in der aktuellen Route.";
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

        var result = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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

        PushDraftRouteStopRemovalSnapshot(SelectedRouteStop);

        RouteStops.Remove(SelectedRouteStop);
        SelectedRouteStop = RouteStops.FirstOrDefault(x => !IsCompanyStop(x));
        RebuildPositions();
        RebuildOrderGrid();
        MarkRouteChanged();
    }

    public bool TryUndoDraftRouteStopRemoval()
    {
        if (_draftRouteStopRemovalUndoStack.Count == 0)
        {
            return false;
        }

        var snapshot = _draftRouteStopRemovalUndoStack.Pop();
        ApplyRouteStops(snapshot.RouteStops, snapshot.SelectedOrderId, markRouteChanged: false);
        RebuildOrderGrid(snapshot.SelectedOrderId);
        SetRouteChanged(snapshot.HadUnsavedRouteChanges);
        StatusText = $"Stopp {snapshot.SelectedOrderId} wurde wiederhergestellt.";
        RaiseDraftRouteStopUndoStateChanged();
        return true;
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
            if (_selectedBatchOrderIds.Remove(draftOrderId))
            {
                NotifyBatchOrderSelectionChanged();
            }
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
        if (_selectedBatchOrderIds.Remove(selectedOrderId))
        {
            NotifyBatchOrderSelectionChanged();
        }
        StatusText = $"Auftrag {selectedOrderId} wurde aus der Tour entfernt.";
    }

    private async Task RemoveSelectedOrdersFromTourAsync()
    {
        var selectedOrderIds = _selectedBatchOrderIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedOrderIds.Count == 0)
        {
            return;
        }

        var removedFromDraftRoute = 0;
        foreach (var orderId in selectedOrderIds)
        {
            var draftStop = RouteStops.FirstOrDefault(x =>
                !IsCompanyStop(x) &&
                string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (draftStop is null)
            {
                continue;
            }

            RouteStops.Remove(draftStop);
            removedFromDraftRoute++;
        }

        if (removedFromDraftRoute > 0)
        {
            RebuildPositions();
            MarkRouteChanged();
        }

        var assignedOrders = _allOrders
            .Where(x => selectedOrderIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace((x.AssignedTourId ?? string.Empty).Trim()))
            .ToList();

        var touchedTourIds = new HashSet<int>();
        if (assignedOrders.Count > 0)
        {
            var tours = (await _tourRepository.LoadAsync()).ToList();
            var selectedOrderIdSet = new HashSet<string>(selectedOrderIds, StringComparer.OrdinalIgnoreCase);
            var toursChanged = false;
            foreach (var tour in tours)
            {
                var stops = tour.Stops ?? [];
                var filteredStops = stops
                    .Where(x => !selectedOrderIdSet.Contains((x.Auftragsnummer ?? string.Empty).Trim()))
                    .ToList();
                if (filteredStops.Count == stops.Count)
                {
                    continue;
                }

                for (var i = 0; i < filteredStops.Count; i++)
                {
                    filteredStops[i].Order = i + 1;
                }

                tour.Stops = filteredStops;
                toursChanged = true;
                touchedTourIds.Add(tour.Id);
            }

            if (toursChanged)
            {
                await _tourRepository.SaveAsync(tours);
                foreach (var touchedTourId in touchedTourIds)
                {
                    var tourKey = touchedTourId.ToString(CultureInfo.InvariantCulture);
                    _dataSyncService.PublishTours(_instanceId, tourKey, tourKey);
                }
            }

            foreach (var order in assignedOrders)
            {
                order.AssignedTourId = string.Empty;
                order.AvisoStatus = NormalizeAvisoStatus(string.Empty);
            }

            await _orderRepository.SaveAllAsync(_allOrders);
            _dataSyncService.PublishOrders(_instanceId);
        }

        if (removedFromDraftRoute > 0 || assignedOrders.Count > 0)
        {
            var currentTourId = ResolveCurrentTourId();
            await RefreshAsync();
            if (currentTourId > 0 && touchedTourIds.Contains(currentTourId))
            {
                await FocusTourAsync(currentTourId);
            }
        }
        else
        {
            RebuildOrderGrid();
        }

        if (_selectedBatchOrderIds.RemoveWhere(x => selectedOrderIds.Contains(x, StringComparer.OrdinalIgnoreCase)) > 0)
        {
            NotifyBatchOrderSelectionChanged();
        }

        StatusText = $"Batch-Aktion abgeschlossen: {removedFromDraftRoute} aus aktueller Route entfernt, {assignedOrders.Count} aus gespeicherten Touren entfernt.";
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

        if (_activeTourId <= 0 && _selectedTourOverviewId > 0 && !RouteStops.Any(x => !IsCompanyStop(x)))
        {
            await SaveSelectedTourOverviewStartTimeAsync(_selectedTourOverviewId);
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
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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

    private async Task SaveSelectedTourOverviewStartTimeAsync(
        int tourId,
        bool suppressNoChangeStatus = false,
        bool suppressSuccessStatus = false)
    {
        try
        {
            var tours = (await _tourRepository.LoadAsync()).ToList();
            var tour = tours.FirstOrDefault(x => x.Id == tourId);
            if (tour is null)
            {
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                    "Die ausgewählte Tour wurde nicht gefunden.",
                    "Tour speichern",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                await RefreshAsync();
                return;
            }

            var updatedStartTime = RouteStartTime;
            if (string.Equals((tour.StartTime ?? string.Empty).Trim(), updatedStartTime, StringComparison.Ordinal))
            {
                SetRouteChanged(false);
                if (!suppressNoChangeStatus)
                {
                    StatusText = "Keine Änderungen zum Speichern.";
                }
                return;
            }

            tour.StartTime = updatedStartTime;
            await _tourRepository.SaveAsync(tours);
            _dataSyncService.PublishTours(_instanceId, tourId.ToString(CultureInfo.InvariantCulture), tourId.ToString(CultureInfo.InvariantCulture));
            await LoadSavedToursAsync(tourId);
            SetRouteChanged(false);
            if (!suppressSuccessStatus)
            {
                StatusText = $"Startzeit für {BuildTourLookupLabel(tour)} gespeichert.";
            }
        }
        catch (IOException ioEx)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"Die Tour konnte nicht gespeichert werden, weil eine Datendatei gerade gesperrt ist.\n\nDetails: {ioEx.Message}",
                "Speichern fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText = "Speichern fehlgeschlagen: Datendatei gesperrt.";
        }
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(availabilityError, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        ClearDraftRouteStopRemovalUndoHistory();
        StatusText = "Route gespeichert und auf Karte geladen.";
        ToastNotificationService.ShowInfo($"Neue Tour {nextId} wurde erstellt.");
        }
        catch (IOException ioEx)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
        var hasDraftRouteStops = RouteStops.Any(x => !IsCompanyStop(x));
        var isOverviewEdit = !hasDraftRouteStops && _activeTourId <= 0 && _selectedTourOverviewId == tourId;

        var tours = (await _tourRepository.LoadAsync()).ToList();
        var index = tours.FindIndex(x => x.Id == tourId);
        if (index < 0)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Die Tour konnte nicht mehr gefunden werden.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            await RefreshAsync();
            return;
        }

        var existingTour = tours[index];
        if (!hasDraftRouteStops && !isOverviewEdit)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Bitte zuerst mindestens einen Auftrag zur Route hinzufügen.",
                "Tour bearbeiten",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var availabilityError = await BuildAvailabilityErrorAsync(routeDate, vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId, employeeIds);
        if (!string.IsNullOrWhiteSpace(availabilityError))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(availabilityError, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmCapacityWarning(vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId))
        {
            return;
        }

        TourRecord updated;
        if (hasDraftRouteStops)
        {
            updated = _mapRouteService.BuildTour(
                ToMapRouteStops(),
                tourId,
                routeName,
                routeDate,
                startTime,
                _companyName,
                _companyAddress,
                _companyLocation,
                defaultServiceMinutes: 10);
            new TourScheduleService().ApplySchedule(updated);
        }
        else
        {
            updated = CloneTourRecord(existingTour);
            updated.Name = string.IsNullOrWhiteSpace(routeName) ? $"Tour {tourId}" : routeName.Trim();
            updated.Date = (routeDate ?? string.Empty).Trim();
            updated.StartTime = string.IsNullOrWhiteSpace(startTime) ? "08:00" : startTime.Trim();
        }

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

        var routeOrderIds = hasDraftRouteStops
            ? _mapRouteService.ExtractRouteOrderIds(ToMapRouteStops())
            : ExtractNonCompanyTourOrderIds(existingTour);
        tours[index] = updated;
        RemoveRouteOrderStopsFromOtherTours(tours, routeOrderIds, tourId);

        await _tourRepository.SaveAsync(tours);
        _dataSyncService.PublishTours(_instanceId, tourId.ToString(CultureInfo.InvariantCulture), tourId.ToString(CultureInfo.InvariantCulture));

        var tourKey = tourId.ToString(CultureInfo.InvariantCulture);
        foreach (var order in _allOrders.Where(o => string.Equals(o.AssignedTourId, tourKey, StringComparison.OrdinalIgnoreCase)))
        {
            if (routeOrderIds.Contains(order.Id))
            {
                continue;
            }

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
        SetRouteChanged(false);
        ClearDraftRouteStopRemovalUndoHistory();
        if (isOverviewEdit)
        {
            SelectTourOverviewById(tourId);
            StatusText = "Tour aktualisiert.";
        }
        else
        {
            await FocusTourAsync(tourId);
            StatusText = "Tour aktualisiert und auf Karte geladen.";
        }
        }
        catch (IOException ioEx)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            .Where(t => !t.IsArchived)
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
        RebuildTourOverviewItems();

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

        var selectedOverview = TourOverviewItems.FirstOrDefault(x => x.TourId == keepId);
        SelectedTourOverviewItem = selectedOverview;

        RebuildPlannedTourRouteOverlays();
        RaiseCommandStates();
        NotifyRoutePanelVisibilityChanged();
    }

    private async Task ToggleAllPlannedToursAsync()
    {
        if (IsAllPlannedToursVisible)
        {
            IsAllPlannedToursVisible = false;
            RebuildPlannedTourRouteOverlays();
            StatusText = "Alle geplanten Tourlinien wurden ausgeblendet.";
            return;
        }

        if (_savedTours.Count == 0)
        {
            await LoadSavedToursAsync();
        }

        IsAllPlannedToursVisible = true;
        RebuildPlannedTourRouteOverlays();
        if (_plannedTourRouteOverlays.Count == 0)
        {
            StatusText = "Keine geplanten Touren mit gültigen Koordinaten gefunden.";
        }
        else
        {
            StatusText = $"{_plannedTourRouteOverlays.Count} geplante Tourlinie(n) auf der Karte angezeigt.";
        }
    }

    private static HashSet<string> ExtractNonCompanyTourOrderIds(TourRecord tour)
    {
        return (tour.Stops ?? [])
            .Where(IsCustomerTourStop)
            .Select(ExtractTourStopOrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static TourRecord CloneTourRecord(TourRecord source)
    {
        return new TourRecord
        {
            Id = source.Id,
            Date = (source.Date ?? string.Empty).Trim(),
            Name = (source.Name ?? string.Empty).Trim(),
            Stops = (source.Stops ?? [])
                .Select(stop => new TourStopRecord
                {
                    Id = (stop.Id ?? string.Empty).Trim(),
                    StopKind = (stop.StopKind ?? string.Empty).Trim(),
                    Name = (stop.Name ?? string.Empty).Trim(),
                    Address = (stop.Address ?? string.Empty).Trim(),
                    Auftragsnummer = (stop.Auftragsnummer ?? string.Empty).Trim(),
                    Lat = stop.Lat,
                    Lon = stop.Lon,
                    Lng = stop.Lng,
                    Order = stop.Order,
                    TimeWindowStart = (stop.TimeWindowStart ?? string.Empty).Trim(),
                    TimeWindowEnd = (stop.TimeWindowEnd ?? string.Empty).Trim(),
                    ServiceMinutes = stop.ServiceMinutes,
                    PlannedArrival = (stop.PlannedArrival ?? string.Empty).Trim(),
                    PlannedDeparture = (stop.PlannedDeparture ?? string.Empty).Trim(),
                    WaitMinutes = stop.WaitMinutes,
                    ScheduleConflict = stop.ScheduleConflict,
                    ScheduleConflictText = (stop.ScheduleConflictText ?? string.Empty).Trim(),
                    Gewicht = (stop.Gewicht ?? string.Empty).Trim(),
                    EmployeeInfoText = (stop.EmployeeInfoText ?? string.Empty).Trim()
                })
                .ToList(),
            EmployeeIds = (source.EmployeeIds ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList(),
            StartTime = string.IsNullOrWhiteSpace(source.StartTime) ? "08:00" : source.StartTime.Trim(),
            RouteMode = string.IsNullOrWhiteSpace(source.RouteMode) ? "car" : source.RouteMode.Trim(),
            VehicleId = string.IsNullOrWhiteSpace(source.VehicleId) ? null : source.VehicleId.Trim(),
            TrailerId = string.IsNullOrWhiteSpace(source.TrailerId) ? null : source.TrailerId.Trim(),
            SecondaryVehicleId = string.IsNullOrWhiteSpace(source.SecondaryVehicleId) ? null : source.SecondaryVehicleId.Trim(),
            SecondaryTrailerId = string.IsNullOrWhiteSpace(source.SecondaryTrailerId) ? null : source.SecondaryTrailerId.Trim(),
            IsArchived = source.IsArchived,
            TravelTimeCache = (source.TravelTimeCache ?? new Dictionary<string, int>())
                .ToDictionary(x => (x.Key ?? string.Empty).Trim(), x => x.Value, StringComparer.OrdinalIgnoreCase)
        };
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
            var result = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
        ClearDraftRouteStopRemovalUndoHistory();
        StatusText = "Tour auf Karte geladen.";
        RaiseCommandStates();
        UpdateRouteSummary();
    }

    private void PushDraftRouteStopRemovalSnapshot(RouteStopItem routeStop)
    {
        var selectedOrderId = (routeStop.OrderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(selectedOrderId))
        {
            return;
        }

        _draftRouteStopRemovalUndoStack.Push(new RouteStopRemovalUndoSnapshot(
            ToMapRouteStops(),
            selectedOrderId,
            _hasUnsavedRouteChanges));

        if (_draftRouteStopRemovalUndoStack.Count > MaxDraftRouteUndoEntries)
        {
            var kept = _draftRouteStopRemovalUndoStack.Take(MaxDraftRouteUndoEntries).Reverse().ToArray();
            _draftRouteStopRemovalUndoStack.Clear();
            foreach (var item in kept)
            {
                _draftRouteStopRemovalUndoStack.Push(item);
            }
        }

        RaiseDraftRouteStopUndoStateChanged();
    }

    private void ClearDraftRouteStopRemovalUndoHistory()
    {
        if (_draftRouteStopRemovalUndoStack.Count == 0)
        {
            return;
        }

        _draftRouteStopRemovalUndoStack.Clear();
        RaiseDraftRouteStopUndoStateChanged();
    }

    private void RaiseDraftRouteStopUndoStateChanged()
    {
        OnPropertyChanged(nameof(CanUndoDraftRouteStopRemoval));
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
                .Select(stop =>
                {
                    var isPause = IsPauseTourStop(stop);
                    return new RouteStopItem
                    {
                        OrderId = isPause
                            ? (string.IsNullOrWhiteSpace(stop.Id) ? $"{PauseStopIdPrefix}{Guid.NewGuid():N}" : stop.Id.Trim())
                            : ExtractTourStopOrderId(stop),
                        Customer = isPause ? "Pause" : NormalizeTourStopName(stop.Name),
                        Address = isPause ? string.Empty : (stop.Address ?? string.Empty),
                        Latitude = isPause ? double.NaN : stop.Lat ?? double.NaN,
                        Longitude = isPause ? double.NaN : stop.Lng ?? stop.Lon ?? double.NaN,
                        IsCompanyAnchor = false,
                        IsPauseStop = isPause,
                        PlannedStayMinutes = Math.Max(0, stop.ServiceMinutes),
                        EmployeeInfoText = isPause ? string.Empty : stop.EmployeeInfoText ?? string.Empty
                    };
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
        if (IsPauseTourStop(stop))
        {
            return string.Empty;
        }

        var id = (stop.Id ?? string.Empty).Trim();
        if (id.StartsWith("auftrag:", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = id["auftrag:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return parsed;
            }
        }

        var number = (stop.Auftragsnummer ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(number))
        {
            return number;
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

    private static bool IsPauseTourStop(TourStopRecord stop)
    {
        var stopKind = (stop.StopKind ?? string.Empty).Trim();
        var id = (stop.Id ?? string.Empty).Trim();
        return string.Equals(stopKind, PauseStopKind, StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith(PauseStopIdPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCustomerTourStop(TourStopRecord stop)
    {
        return !IsCompanyTourStop(stop) && !IsPauseTourStop(stop);
    }

    private static void MergeManualPauseStops(TourRecord updatedTour, TourRecord existingTour)
    {
        var existingStops = (existingTour.Stops ?? [])
            .OrderBy(x => x.Order)
            .ToList();
        if (!existingStops.Any(IsPauseTourStop))
        {
            return;
        }

        const string startAnchorKey = "__start__";
        var pausesByAnchor = new Dictionary<string, List<TourStopRecord>>(StringComparer.OrdinalIgnoreCase);
        string currentAnchor = startAnchorKey;

        foreach (var stop in existingStops)
        {
            if (IsCustomerTourStop(stop))
            {
                currentAnchor = ExtractTourStopOrderId(stop);
                continue;
            }

            if (!IsPauseTourStop(stop))
            {
                continue;
            }

            if (!pausesByAnchor.TryGetValue(currentAnchor, out var pauses))
            {
                pauses = new List<TourStopRecord>();
                pausesByAnchor[currentAnchor] = pauses;
            }

            pauses.Add(CloneTourStop(stop));
        }

        var start = updatedTour.Stops.FirstOrDefault(x => string.Equals((x.Id ?? string.Empty).Trim(), CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
        var end = updatedTour.Stops.FirstOrDefault(x => string.Equals((x.Id ?? string.Empty).Trim(), CompanyEndStopId, StringComparison.OrdinalIgnoreCase));
        var customerStops = updatedTour.Stops.Where(IsCustomerTourStop).ToList();
        var mergedStops = new List<TourStopRecord>();

        if (pausesByAnchor.TryGetValue(startAnchorKey, out var leadingPauses))
        {
            mergedStops.AddRange(leadingPauses.Select(CloneTourStop));
        }

        foreach (var customerStop in customerStops)
        {
            mergedStops.Add(customerStop);
            var anchor = ExtractTourStopOrderId(customerStop);
            if (string.IsNullOrWhiteSpace(anchor) || !pausesByAnchor.TryGetValue(anchor, out var anchoredPauses))
            {
                continue;
            }

            mergedStops.AddRange(anchoredPauses.Select(CloneTourStop));
        }

        updatedTour.Stops = [
            .. (start is null ? [] : new[] { start }),
            .. mergedStops,
            .. (end is null ? [] : new[] { end })
        ];

        for (var i = 0; i < updatedTour.Stops.Count; i++)
        {
            updatedTour.Stops[i].Order = i + 1;
        }
    }

    private static TourStopRecord CloneTourStop(TourStopRecord stop)
    {
        return new TourStopRecord
        {
            Id = (stop.Id ?? string.Empty).Trim(),
            StopKind = (stop.StopKind ?? string.Empty).Trim(),
            Name = (stop.Name ?? string.Empty).Trim(),
            Address = (stop.Address ?? string.Empty).Trim(),
            Auftragsnummer = (stop.Auftragsnummer ?? string.Empty).Trim(),
            Lat = stop.Lat,
            Lon = stop.Lon,
            Lng = stop.Lng,
            Order = stop.Order,
            TimeWindowStart = (stop.TimeWindowStart ?? string.Empty).Trim(),
            TimeWindowEnd = (stop.TimeWindowEnd ?? string.Empty).Trim(),
            ServiceMinutes = stop.ServiceMinutes,
            PlannedArrival = (stop.PlannedArrival ?? string.Empty).Trim(),
            PlannedDeparture = (stop.PlannedDeparture ?? string.Empty).Trim(),
            WaitMinutes = stop.WaitMinutes,
            ScheduleConflict = stop.ScheduleConflict,
            ScheduleConflictText = (stop.ScheduleConflictText ?? string.Empty).Trim(),
            Gewicht = (stop.Gewicht ?? string.Empty).Trim(),
            EmployeeInfoText = (stop.EmployeeInfoText ?? string.Empty).Trim()
        };
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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

        Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            result.Message,
            "Route exportieren",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    private void ExportRouteToGoogleMaps(RouteExportSnapshot snapshot)
    {
        if (!GoogleMapsRouteExportService.TryBuildUrl(snapshot.GoogleMapsPoints, out var url, out var error))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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

        var routeStops = RouteStops.Where(IsOrderStop).ToList();
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
            ResolveDeliveryTypeText(stop.OrderId),
            stop.OrderId,
            stop.Latitude,
            stop.Longitude,
            ResolveTimeWindow(stop.OrderId),
            stop.EtaText,
            ResolveWeightText(stop.OrderId),
            stop.EmployeeInfoText,
            GetPauseStopsAfter(stop).Sum(x => Math.Max(0, x.PlannedStayMinutes))))
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
            .Select((x, index) => new MapRouteStop(
                index + 1,
                x.OrderId,
                x.Customer,
                x.Address,
                x.Latitude,
                x.Longitude,
                x.PlannedStayMinutes,
                x.EmployeeInfoText,
                x.IsPauseStop ? PauseStopKind : string.Empty))
            .ToList();
    }

    private void ApplyRouteStops(IReadOnlyList<MapRouteStop> routeStops, string? selectedOrderId = null, bool markRouteChanged = true)
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
                    OrderId = stop.OrderId ?? string.Empty,
                    Customer = stop.Customer ?? string.Empty,
                    Address = stop.Address ?? string.Empty,
                    Latitude = stop.Latitude,
                    Longitude = stop.Longitude,
                    IsCompanyAnchor = false,
                    IsPauseStop = string.Equals((stop.StopKind ?? string.Empty).Trim(), PauseStopKind, StringComparison.OrdinalIgnoreCase) ||
                                  (stop.OrderId ?? string.Empty).StartsWith(PauseStopIdPrefix, StringComparison.OrdinalIgnoreCase),
                    PlannedStayMinutes = stop.ServiceMinutes < 0 ? 10 : stop.ServiceMinutes,
                    EmployeeInfoText = stop.EmployeeInfoText
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

        if (markRouteChanged)
        {
            MarkRouteChanged();
        }
    }

    private void ClearRoute()
    {
        ClearDraftRouteStopRemovalUndoHistory();
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
        SelectedTourOverviewItem = null;

        RebuildPositions();
        RebuildOrderGrid();
        SetRouteChanged(false);
        UpdateRouteSummary();
        RebuildPlannedTourRouteOverlays();
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
        if (!ShowRouteStopsPanel)
        {
            return;
        }

        var hasUnsavedChanges = _hasUnsavedRouteChanges && RouteStops.Any(x => !IsCompanyStop(x));
        if (hasUnsavedChanges)
        {
            var confirmLeave = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Die aktuelle Tour hat ungespeicherte Änderungen.\n\nMöchtest du die Tour wirklich verlassen und zur Routenübersicht wechseln?",
                "Ungespeicherte Änderungen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmLeave != MessageBoxResult.Yes)
            {
                return;
            }
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
        var confirmDelete = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            stop.DisplayIndex = IsCompanyStop(stop) || IsPauseStop(stop) ? 0 : ++displayIndex;
        }

        ClearRouteStopEtaValues();

        OnPropertyChanged(nameof(RouteStops));
        UpdateRouteDistanceFromStops();
        UpdateDriveTimePlaceholderState();
        UpdateRouteSummary();
        RebuildPlannedTourRouteOverlays();
        RequestRouteGeometryRebuild();
        UpdateStatus();
        RaiseCommandStates();
        NotifyRoutePanelVisibilityChanged();
    }

    private void ClearRouteStopEtaValues()
    {
        foreach (var stop in RouteStops)
        {
            stop.EtaText = string.Empty;
            stop.ClearNextLeg();
            stop.ClearPauseAfter();
        }
    }

    private void UpdateRouteDistanceFromStops()
    {
        var distancePoints = RouteStops
            .Where(x => !IsPauseStop(x))
            .Where(x => !double.IsNaN(x.Latitude) && !double.IsNaN(x.Longitude))
            .ToList();
        RouteDistanceKm = _optimizationService.ComputeTotalDistanceKm(distancePoints, x => x.Latitude, x => x.Longitude);
    }

    private void UpdateDriveTimePlaceholderState()
    {
        if (!RouteStops.Any(IsOrderStop))
        {
            ClearDriveTimes();
            return;
        }

        RouteTimingSummary = "Fahrzeiten werden berechnet...";
        DriveTimesText = "Fahrzeiten werden berechnet...";
    }

    private void UpdateStatus()
    {
        var routeStopCount = RouteStops.Count(IsOrderStop);
        StatusText = $"Map orders: {MapOrders.Count} | Route stops: {routeStopCount} | Route distance: {RouteDistanceKm:0.##} km";
    }

    private void UpdateRouteSummary()
    {
        var totalWeightKg = RouteStops
            .Where(IsOrderStop)
            .Select(x => FindOrderWeightKg(x.OrderId))
            .Sum();
        var summaryVehicleId = _currentRouteVehicleId;
        var summaryTrailerId = _currentRouteTrailerId;
        var summarySecondaryVehicleId = _currentRouteSecondaryVehicleId;
        var summarySecondaryTrailerId = _currentRouteSecondaryTrailerId;

        var hasRouteStops = RouteStops.Any(IsOrderStop);
        if (!hasRouteStops && _activeTourId <= 0 && _selectedTourOverviewId > 0)
        {
            var selectedOverviewTour = _savedTours.FirstOrDefault(x => x.Id == _selectedTourOverviewId);
            if (selectedOverviewTour is not null)
            {
                summaryVehicleId = (selectedOverviewTour.VehicleId ?? string.Empty).Trim();
                summaryTrailerId = (selectedOverviewTour.TrailerId ?? string.Empty).Trim();
                summarySecondaryVehicleId = (selectedOverviewTour.SecondaryVehicleId ?? string.Empty).Trim();
                summarySecondaryTrailerId = (selectedOverviewTour.SecondaryTrailerId ?? string.Empty).Trim();

                var overviewOrderIds = (selectedOverviewTour.Stops ?? [])
                    .Where(IsCustomerTourStop)
                    .Select(ExtractTourStopOrderId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (overviewOrderIds.Count > 0)
                {
                    totalWeightKg = overviewOrderIds.Sum(FindOrderWeightKg);
                }
                else
                {
                    var overviewTourId = selectedOverviewTour.Id.ToString(CultureInfo.InvariantCulture);
                    totalWeightKg = _allOrders
                        .Where(x => !x.IsArchived && string.Equals((x.AssignedTourId ?? string.Empty).Trim(), overviewTourId, StringComparison.OrdinalIgnoreCase))
                        .Select(x => FindOrderWeightKg(x.Id))
                        .Sum();
                }
            }
        }

        RouteTotalWeightText = $"Totalgewicht: {totalWeightKg} kg";

        var assignments = BuildVehicleAssignments(
            summaryVehicleId,
            summaryTrailerId,
            summarySecondaryVehicleId,
            summarySecondaryTrailerId);
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
        RouteVisualRevision++;
    }

    private void RebuildPlannedTourRouteOverlays()
    {
        _plannedTourRouteOverlays.Clear();
        if (IsAllPlannedToursVisible)
        {
            foreach (var tour in _savedTours.Where(x => !x.IsArchived))
            {
                var points = (tour.Stops ?? [])
                    .OrderBy(x => x.Order)
                    .Where(x => !IsCompanyEndTourStop(x))
                    .Select(TryMapStopToPoint)
                    .Where(x => x is not null)
                    .Select(x => x!)
                    .ToList();
                if (points.Count < 2)
                {
                    continue;
                }

                var assignments = BuildVehicleAssignments(
                    tour.VehicleId,
                    tour.TrailerId,
                    tour.SecondaryVehicleId,
                    tour.SecondaryTrailerId);
                var totalWeightKg = _allOrders
                    .Where(x => !x.IsArchived && string.Equals((x.AssignedTourId ?? string.Empty).Trim(), tour.Id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                    .Select(x => FindOrderWeightKg(x.Id))
                    .Sum();
                var colorHex = ResolveRoutePolylineColorHex(totalWeightKg, assignments);
                var label = BuildTourLookupLabel(tour);

                _plannedTourRouteOverlays.Add(new PlannedTourRouteOverlay(
                    tour.Id,
                    label,
                    colorHex,
                    points));
            }
        }

        PlannedTourOverlayRevision++;
    }

    private static GeoPoint? TryMapStopToPoint(TourStopRecord? stop)
    {
        if (stop is null)
        {
            return null;
        }

        var lat = stop.Lat;
        var lon = stop.Lon ?? stop.Lng;
        if (!lat.HasValue || !lon.HasValue || double.IsNaN(lat.Value) || double.IsNaN(lon.Value))
        {
            return null;
        }

        return new GeoPoint(lat.Value, lon.Value);
    }

    private static bool IsCompanyEndTourStop(TourStopRecord stop)
    {
        return string.Equals((stop.Id ?? string.Empty).Trim(), TourStopIdentity.CompanyEndStopId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals((stop.Auftragsnummer ?? string.Empty).Trim(), TourStopIdentity.CompanyEndOrderNumber, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveRoutePolylineColorHex(int totalWeightKg, IReadOnlyList<(string VehicleId, string TrailerId)> assignments)
    {
        var warning = TourCapacityWarningService.EvaluateFleet(_vehicleData, assignments, totalWeightKg);
        if (!warning.AllowedWeightKg.HasValue || warning.AllowedWeightKg.Value <= 0)
        {
            return "#2563EB";
        }

        var remainingPercent = ((warning.AllowedWeightKg.Value - totalWeightKg) / (double)warning.AllowedWeightKg.Value) * 100d;
        return remainingPercent <= _mapRouteCapacityWarningThresholdPercent
            ? "#DC2626"
            : "#2563EB";
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

        return Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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

        return Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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

        return (int)Math.Max(
            0,
            Math.Round(
                (order.Products ?? []).Sum(OrderProductFormatter.ResolveTotalWeightKg),
                MidpointRounding.AwayFromZero));
    }

    private string ResolveDeliveryTypeText(string? orderId)
    {
        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
        {
            return string.Empty;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, normalizedOrderId, StringComparison.OrdinalIgnoreCase));
        return order is null
            ? string.Empty
            : NormalizeDeliveryType(order.DeliveryType);
    }

    private void RaiseCommandStates()
    {
        RaiseCanExecuteChangedIfSupported(AddToRouteCommand);
        RaiseCanExecuteChangedIfSupported(AddSelectedOrdersToRouteCommand);
        RaiseCanExecuteChangedIfSupported(RemoveOrderFromTourCommand);
        RaiseCanExecuteChangedIfSupported(RemoveSelectedOrdersFromTourCommand);
        RaiseCanExecuteChangedIfSupported(ClearSelectedOrdersCommand);
        RaiseCanExecuteChangedIfSupported(RemoveFromRouteCommand);
        RaiseCanExecuteChangedIfSupported(MoveStopUpCommand);
        RaiseCanExecuteChangedIfSupported(MoveStopDownCommand);
        RaiseCanExecuteChangedIfSupported(OptimizeRouteCommand);
        RaiseCanExecuteChangedIfSupported(OpenCreateTourDialogCommand);
        RaiseCanExecuteChangedIfSupported(EditSelectedTourCommand);
        RaiseCanExecuteChangedIfSupported(ExportRouteCommand);
        RaiseCanExecuteChangedIfSupported(SaveRouteAsTourCommand);
        RaiseCanExecuteChangedIfSupported(SaveCurrentTourCommand);
        RaiseCanExecuteChangedIfSupported(OpenSelectedTourOverviewCommand);
        RaiseCanExecuteChangedIfSupported(ClearRouteCommand);
        RaiseCanExecuteChangedIfSupported(LeaveSelectedTourCommand);
        RaiseCanExecuteChangedIfSupported(PreviousTourCommand);
        RaiseCanExecuteChangedIfSupported(NextTourCommand);
        RaiseCanExecuteChangedIfSupported(DeleteSelectedTourCommand);
        RaiseCanExecuteChangedIfSupported(CloseDetailsCommand);
        RaiseCanExecuteChangedIfSupported(SendEmailCommand);
        RaiseCanExecuteChangedIfSupported(EditOrderCommand);
        RaiseCanExecuteChangedIfSupported(ShowSelectedOrderTourCommand);
        OnPropertyChanged(nameof(CurrentStopViewTourName));
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

        if (_selectedTourOverviewId > 0)
        {
            return _selectedTourOverviewId;
        }

        return _activeTourId > 0 ? _activeTourId : 0;
    }

    private bool CanEditOrLeaveSelectedTour()
    {
        return ResolveCurrentTourId() > 0;
    }

    private bool CanLeaveSelectedTour()
    {
        return ShowRouteStopsPanel;
    }

    private bool CanSaveCurrentTour()
    {
        if (IsRouteCalculating)
        {
            return false;
        }

        if (!_hasUnsavedRouteChanges)
        {
            return false;
        }

        if (RouteStops.Any(x => !IsCompanyStop(x)))
        {
            return true;
        }

        return _activeTourId <= 0 && _selectedTourOverviewId > 0;
    }

    private bool CanOpenSelectedTourOverview()
    {
        if (IsRouteCalculating)
        {
            return false;
        }

        if (!ShowTourOverviewPanel)
        {
            return false;
        }

        return (SelectedTourOverviewItem?.TourId ?? 0) > 0;
    }

    private async Task OpenSelectedTourOverviewAsync()
    {
        var selectedTourId = SelectedTourOverviewItem?.TourId ?? 0;
        if (selectedTourId <= 0)
        {
            return;
        }

        await FocusTourAsync(selectedTourId);
    }

    private static bool HasNeighbour(IReadOnlyList<int> tourIds, int currentTourId, int offset)
    {
        if (tourIds.Count == 0 || currentTourId <= 0)
        {
            return false;
        }

        var index = FindTourIndex(tourIds, currentTourId);
        if (index < 0)
        {
            return false;
        }

        var targetIndex = index + offset;
        return targetIndex >= 0 && targetIndex < tourIds.Count;
    }

    private static int FindTourIndex(IReadOnlyList<int> tourIds, int tourId)
    {
        for (var i = 0; i < tourIds.Count; i++)
        {
            if (tourIds[i] == tourId)
            {
                return i;
            }
        }

        return -1;
    }

    private IReadOnlyList<int> GetTourIdsForStopNavigation()
    {
        if (TourOverviewItems.Count > 0)
        {
            return TourOverviewItems
                .Select(x => x.TourId)
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }

        return _savedTours
            .OrderBy(t => ParseDateForSort(t.Date))
            .ThenBy(t => BuildTourLookupLabel(t), StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Id)
            .Where(x => x > 0)
            .Distinct()
            .ToList();
    }

    private bool CanSwitchToPreviousTour()
    {
        if (IsRouteCalculating)
        {
            return false;
        }

        if (!ShowRouteStopsPanel)
        {
            return false;
        }

        var tourIds = GetTourIdsForStopNavigation();
        return HasNeighbour(tourIds, ResolveCurrentTourId(), -1);
    }

    private bool CanSwitchToNextTour()
    {
        if (IsRouteCalculating)
        {
            return false;
        }

        if (!ShowRouteStopsPanel)
        {
            return false;
        }

        var tourIds = GetTourIdsForStopNavigation();
        return HasNeighbour(tourIds, ResolveCurrentTourId(), 1);
    }

    private bool CanOptimizeRoute()
    {
        if (IsRouteCalculating)
        {
            return false;
        }

        return RouteStops.Count(x => !IsCompanyStop(x)) > 2;
    }

    private void SwitchToPreviousTour()
    {
        SwitchToRelativeTour(-1);
    }

    private void SwitchToNextTour()
    {
        SwitchToRelativeTour(1);
    }

    private void SwitchToRelativeTour(int offset)
    {
        var tourIds = GetTourIdsForStopNavigation();
        var currentTourId = ResolveCurrentTourId();
        var currentIndex = FindTourIndex(tourIds, currentTourId);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= tourIds.Count)
        {
            return;
        }

        var targetTourId = tourIds[targetIndex];
        var target = SavedTours.FirstOrDefault(x => x.TourId == targetTourId);
        if (target is null)
        {
            return;
        }

        SelectedSavedTour = target;
    }

    private string ResolveCurrentStopViewTourName()
    {
        if (!ShowRouteStopsPanel)
        {
            return string.Empty;
        }

        var tourId = ResolveCurrentTourId();
        if (tourId > 0)
        {
            var tour = _savedTours.FirstOrDefault(x => x.Id == tourId);
            if (tour is not null && !string.IsNullOrWhiteSpace(tour.Name))
            {
                return NormalizeUiText(tour.Name);
            }

            return $"Tour {tourId}";
        }

        return string.IsNullOrWhiteSpace(RouteName) ? "Aktuelle Tour" : RouteName.Trim();
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

    private bool IsTourOverviewStartTimeAutoSaveMode()
    {
        return !_suppressRouteChangeTracking &&
               _activeTourId <= 0 &&
               _selectedTourOverviewId > 0 &&
               !RouteStops.Any(x => !IsCompanyStop(x));
    }

    private void ScheduleTourOverviewStartTimeAutoSave()
    {
        if (!IsTourOverviewStartTimeAutoSaveMode() || !_hasUnsavedRouteChanges)
        {
            return;
        }

        if (RouteStartHour.Length < 2 || RouteStartMinute.Length < 2)
        {
            return;
        }

        _tourOverviewStartTimeAutoSaveCts?.Cancel();
        _tourOverviewStartTimeAutoSaveCts = new CancellationTokenSource();
        var token = _tourOverviewStartTimeAutoSaveCts.Token;
        _ = AutoSaveTourOverviewStartTimeAsync(_selectedTourOverviewId, token);
    }

    private async Task AutoSaveTourOverviewStartTimeAsync(int tourId, CancellationToken token)
    {
        try
        {
            await Task.Delay(450, token);
            if (token.IsCancellationRequested || tourId <= 0)
            {
                return;
            }

            if (!IsTourOverviewStartTimeAutoSaveMode() ||
                _selectedTourOverviewId != tourId ||
                !_hasUnsavedRouteChanges)
            {
                return;
            }

            await SaveSelectedTourOverviewStartTimeAsync(
                tourId,
                suppressNoChangeStatus: true,
                suppressSuccessStatus: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
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

        if (!await ConfirmManualArchiveForAssignedActiveTourAsync(selected, updated))
        {
            return;
        }

        updated.Location = await AddressGeocodingService.TryGeocodeOrderAsync(updated, _tomTomApiKey, _geocodeCachePath) ?? selected.Location;

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

    private async Task<bool> ConfirmManualArchiveForAssignedActiveTourAsync(Order existing, Order updated)
    {
        if (existing.IsArchived || !updated.IsArchived)
        {
            return true;
        }

        var assignedTourId = (existing.AssignedTourId ?? string.Empty).Trim();
        if (!int.TryParse(assignedTourId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var assignedTourIdNumber) ||
            assignedTourIdNumber <= 0)
        {
            return true;
        }

        var tours = await _tourRepository.LoadAsync();
        var assignedTour = tours.FirstOrDefault(t => t.Id == assignedTourIdNumber);
        if (assignedTour is null || assignedTour.IsArchived)
        {
            return true;
        }

        var tourLabel = string.IsNullOrWhiteSpace(assignedTour.Name)
            ? $"Tour {assignedTour.Id}"
            : $"{assignedTour.Name.Trim()} (Tour {assignedTour.Id})";
        var confirmation = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            $"Der Auftrag ist in {tourLabel} eingeplant, und diese Tour ist nicht archiviert.{Environment.NewLine}{Environment.NewLine}" +
            "Auftrag trotzdem archivieren?",
            "Auftrag in aktiver Tour",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return confirmation == MessageBoxResult.Yes;
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
        RefreshRouteVisualsAfterOrderMutation();
        PublishOrderChange(order.Id, order.Id);
        StatusText = $"Produkt in Auftrag {order.Id} wurde aktualisiert.";
    }

    public void ToggleDetailProductSelection(DetailProductItem? detailItem)
    {
        var productIndex = detailItem?.ProductIndex ?? -1;
        if (productIndex < 0)
        {
            return;
        }

        if (_selectedDetailProductIndices.Contains(productIndex))
        {
            _selectedDetailProductIndices.Remove(productIndex);
        }
        else
        {
            _selectedDetailProductIndices.Add(productIndex);
        }

        SyncDetailSelectedProductStatusFromSelection();
        OnPropertyChanged(nameof(DetailProductItems));
        OnPropertyChanged(nameof(HasSelectedDetailProducts));
        OnPropertyChanged(nameof(DetailSelectedProductsSummary));
        RaiseCommandStates();
    }

    public void SelectSingleDetailProduct(DetailProductItem? detailItem)
    {
        var productIndex = detailItem?.ProductIndex ?? -1;
        if (productIndex < 0)
        {
            return;
        }

        if (_selectedDetailProductIndices.Count == 1 && _selectedDetailProductIndices.Contains(productIndex))
        {
            return;
        }

        _selectedDetailProductIndices.Clear();
        _selectedDetailProductIndices.Add(productIndex);
        SyncDetailSelectedProductStatusFromSelection();
        OnPropertyChanged(nameof(DetailProductItems));
        OnPropertyChanged(nameof(HasSelectedDetailProducts));
        OnPropertyChanged(nameof(DetailSelectedProductsSummary));
        RaiseCommandStates();
    }

    private void ClearDetailProductSelection(bool raiseDetailItemsChanged = true)
    {
        if (_selectedDetailProductIndices.Count == 0)
        {
            SyncDetailSelectedProductStatusFromSelection();
            return;
        }

        _selectedDetailProductIndices.Clear();
        if (raiseDetailItemsChanged)
        {
            OnPropertyChanged(nameof(DetailProductItems));
        }

        SyncDetailSelectedProductStatusFromSelection();
        OnPropertyChanged(nameof(HasSelectedDetailProducts));
        OnPropertyChanged(nameof(DetailSelectedProductsSummary));
        RaiseCommandStates();
    }

    private void SyncDetailSelectedProductStatusFromSelection()
    {
        string? nextStatus = null;
        var order = FindSelectedOrderModel();
        if (order is not null && _selectedDetailProductIndices.Count > 0)
        {
            var products = order.Products ?? [];
            var distinctStatuses = _selectedDetailProductIndices
                .Where(index => index >= 0 && index < products.Count && products[index] is not null)
                .Select(index => OrderProductInfo.NormalizeDeliveryStatus(products[index]!.DeliveryStatus))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctStatuses.Count == 1)
            {
                nextStatus = distinctStatuses[0];
            }
        }

        _suppressDetailSelectedProductStatusApply = true;
        try
        {
            DetailSelectedProductStatus = nextStatus;
        }
        finally
        {
            _suppressDetailSelectedProductStatusApply = false;
        }
    }

    private async Task ApplySelectedDetailProductStatusAsync(string statusToApply)
    {
        var order = FindSelectedOrderModel();
        if (order is null)
        {
            return;
        }

        var products = order.Products ?? [];
        if (products.Count == 0 || _selectedDetailProductIndices.Count == 0)
        {
            return;
        }

        var normalizedStatus = OrderProductInfo.NormalizeDeliveryStatus(statusToApply);
        var changedCount = 0;
        foreach (var index in _selectedDetailProductIndices.OrderBy(x => x))
        {
            if (index < 0 || index >= products.Count)
            {
                continue;
            }

            var product = products[index];
            if (product is null)
            {
                continue;
            }

            product.DeliveryStatus = normalizedStatus;
            changedCount++;
        }

        if (changedCount == 0)
        {
            return;
        }

        order.OrderStatus = Order.ResolveOrderStatusFromProducts(products);
        await _orderRepository.SaveAllAsync(_allOrders);
        RebuildOrderGrid(order.Id);
        ClearDetailProductSelection(raiseDetailItemsChanged: false);
        OnPropertyChanged(nameof(DetailProducts));
        OnPropertyChanged(nameof(DetailProductItems));
        OnPropertyChanged(nameof(DetailOrderStatus));
        OnPropertyChanged(nameof(DetailOrderStatusColor));
        RefreshRouteVisualsAfterOrderMutation();
        PublishOrderChange(order.Id, order.Id);
        StatusText = $"{changedCount} Produkt(e) in Auftrag {order.Id} auf \"{normalizedStatus}\" gesetzt.";
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

        var resolvedOrderId = ResolveCanonicalOrderId(orderId);
        var match = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, resolvedOrderId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedOrder = match;
            return;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, resolvedOrderId, StringComparison.OrdinalIgnoreCase));
        SelectedOrder = order is null ? null : BuildMapOrderItem(order);
    }

    private string ResolveCanonicalOrderId(string? rawOrderId)
    {
        var normalized = (rawOrderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var direct = _allOrders.FirstOrDefault(x => string.Equals(x.Id, normalized, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return direct.Id;
        }

        if (normalized.StartsWith("auftrag:", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = normalized["auftrag:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                var parsedMatch = _allOrders.FirstOrDefault(x => string.Equals(x.Id, parsed, StringComparison.OrdinalIgnoreCase));
                if (parsedMatch is not null)
                {
                    return parsedMatch.Id;
                }
            }
        }

        foreach (var stop in _savedTours.SelectMany(x => x.Stops ?? []))
        {
            var stopId = (stop.Id ?? string.Empty).Trim();
            var stopNumber = (stop.Auftragsnummer ?? string.Empty).Trim();
            if (!string.Equals(stopId, normalized, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(stopNumber, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = ExtractTourStopOrderId(stop);
            var candidateMatch = _allOrders.FirstOrDefault(x => string.Equals(x.Id, candidate, StringComparison.OrdinalIgnoreCase));
            if (candidateMatch is not null)
            {
                return candidateMatch.Id;
            }
        }

        return normalized;
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
        UpdateRouteSummary();
        RebuildPlannedTourRouteOverlays();

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

        RequestRouteGeometryRebuild();
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

    private void RefreshRouteVisualsAfterOrderMutation()
    {
        UpdateRouteSummary();
        RebuildPlannedTourRouteOverlays();
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

        if ((args.Kinds & AppDataKind.Settings) != 0 &&
            (args.Kinds & (AppDataKind.Tours | AppDataKind.Vehicles | AppDataKind.Employees)) == AppDataKind.None)
        {
            _ = ReloadPinInfoCardDisplaySettingsAsync();
            return;
        }

        _ = RefreshAsync();
    }

    private async Task ReloadPinInfoCardDisplaySettingsAsync()
    {
        try
        {
            var settings = await _settingsRepository.LoadAsync();
            var nextScale = settings.PinInfoCardScale is >= 0.7d and <= 1.8d
                ? settings.PinInfoCardScale
                : AppSettings.DefaultPinInfoCardScale;
            var nextZoomBehaviorStrength = settings.PinInfoCardZoomBehaviorStrength is >= 0.2d and <= 4.0d
                ? settings.PinInfoCardZoomBehaviorStrength
                : AppSettings.DefaultPinInfoCardZoomBehaviorStrength;
            var changed = false;

            if (Math.Abs(_pinInfoCardScale - nextScale) > 0.0001d)
            {
                _pinInfoCardScale = nextScale;
                OnPropertyChanged(nameof(PinInfoCardScale));
                OnPropertyChanged(nameof(PinInfoCardScalePercentText));
                changed = true;
            }

            if (Math.Abs(_pinInfoCardZoomBehaviorStrength - nextZoomBehaviorStrength) > 0.0001d)
            {
                _pinInfoCardZoomBehaviorStrength = nextZoomBehaviorStrength;
                OnPropertyChanged(nameof(PinInfoCardZoomBehaviorStrength));
                OnPropertyChanged(nameof(PinInfoCardZoomBehaviorStrengthText));
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(RouteVisualRevision));
            }
        }
        catch
        {
            // Ignore transient settings reload errors; regular refresh paths still recover state.
        }
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

    private void UpdateRouteSelectionVisuals(RouteStopItem? selectedStop, bool selectLeg)
    {
        foreach (var stop in RouteStops)
        {
            var isSelected = selectedStop is not null && ReferenceEquals(stop, selectedStop);
            stop.IsStopSelected = isSelected && !selectLeg;
            stop.IsLegSelected = isSelected && selectLeg;
        }
    }

    private static bool IsPauseStop(RouteStopItem stop)
    {
        var orderId = (stop.OrderId ?? string.Empty).Trim();
        return stop.IsPauseStop || orderId.StartsWith(PauseStopIdPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOrderStop(RouteStopItem stop)
    {
        return !IsCompanyStop(stop) && !IsPauseStop(stop);
    }

    private static bool IsCompanyOrderId(string? orderId)
    {
        var id = (orderId ?? string.Empty).Trim();
        return string.Equals(id, CompanyStartStopId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, CompanyEndStopId, StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("__company_", StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildTourOverviewItems()
    {
        TourOverviewItems.Clear();
        foreach (var tour in _savedTours
                     .OrderBy(t => ParseDateForSort(t.Date))
                     .ThenBy(t => BuildTourLookupLabel(t), StringComparer.OrdinalIgnoreCase))
        {
            var parsedDate = ParseDateForSort(tour.Date);
            var dateText = parsedDate == DateTime.MinValue
                ? (tour.Date ?? string.Empty).Trim()
                : parsedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            var startTimeText = string.IsNullOrWhiteSpace(tour.StartTime)
                ? "--:--"
                : (tour.StartTime ?? string.Empty).Trim();
            var stopCount = (tour.Stops ?? []).Count(IsCustomerTourStop);
            TourOverviewItems.Add(new SavedTourOverviewItem(
                tour.Id,
                BuildTourLookupLabel(tour),
                dateText,
                startTimeText,
                stopCount));
        }
    }

    private void ApplyTourOverviewSelection(SavedTourOverviewItem? selection)
    {
        _selectedTourOverviewId = selection?.TourId ?? 0;
        OnPropertyChanged(nameof(PlannedTourOverlayHighlightTourId));
        if (_selectedTourOverviewId <= 0)
        {
            UpdateRouteSummary();
            SetRouteChanged(false);
            RaiseCommandStates();
            return;
        }

        var selectedTour = _savedTours.FirstOrDefault(x => x.Id == _selectedTourOverviewId);
        if (selectedTour is null)
        {
            UpdateRouteSummary();
            SetRouteChanged(false);
            RaiseCommandStates();
            return;
        }

        var (hour, minute) = ParseStartTimePartsOrDefault(selectedTour.StartTime);
        _suppressRouteChangeTracking = true;
        try
        {
            RouteStartHour = hour;
            RouteStartMinute = minute;
        }
        finally
        {
            _suppressRouteChangeTracking = false;
        }

        UpdateRouteSummary();
        SetRouteChanged(false);
        RaiseCommandStates();
    }

    private void NotifyRoutePanelVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowRouteStopsPanel));
        OnPropertyChanged(nameof(ShowTourOverviewPanel));
        OnPropertyChanged(nameof(PlannedTourOverlayHighlightTourId));
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

    private static string NormalizeTomTomMapStyle(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "main" or "night"
            ? normalized
            : AppSettings.DefaultTomTomMapStyle;
    }

    private static string NormalizeTomTomRoutingMode(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "car" or "heightaware"
            ? normalized
            : AppSettings.DefaultTomTomRoutingMode;
    }

    private static TomTomRoutingService CreateTomTomRoutingService(string? apiKey, string? mode, double vehicleHeightMeters)
    {
        var normalizedMode = NormalizeTomTomRoutingMode(mode);
        var profile = normalizedMode == "heightaware"
            ? new TomTomRoutingProfile(global::Tourenplaner.CSharp.App.Services.TomTomRoutingMode.HeightAware, Math.Clamp(vehicleHeightMeters, 0d, 20d))
            : TomTomRoutingProfile.Default;
        return new TomTomRoutingService(apiKey, profile);
    }

    private bool HasAssignedPrimaryVehicle()
    {
        return !string.IsNullOrWhiteSpace(_currentRouteVehicleId) &&
               _vehicleData.Vehicles.Any(x => string.Equals(x.Id, _currentRouteVehicleId, StringComparison.OrdinalIgnoreCase));
    }

    private Vehicle? ResolvePrimaryAssignedVehicle()
    {
        if (string.IsNullOrWhiteSpace(_currentRouteVehicleId))
        {
            return null;
        }

        return _vehicleData.Vehicles.FirstOrDefault(x => string.Equals(x.Id, _currentRouteVehicleId, StringComparison.OrdinalIgnoreCase));
    }

    private TomTomRoutingService GetTomTomRoutingServiceForCurrentRoute()
    {
        if (_tomTomUseVehicleDimensions || _tomTomUseVehicleWeightRestrictions)
        {
            var vehicle = ResolvePrimaryAssignedVehicle();
            var dimensions = vehicle?.ExternalDimensions;
            if (dimensions is not null || (_tomTomUseVehicleWeightRestrictions && (vehicle?.GrossWeightKg ?? 0) > 0))
            {
                var lengthMeters = _tomTomUseVehicleDimensions ? Math.Clamp((dimensions?.LengthCm ?? 0) / 100d, 0d, 50d) : 0d;
                var widthMeters = _tomTomUseVehicleDimensions ? Math.Clamp((dimensions?.WidthCm ?? 0) / 100d, 0d, 10d) : 0d;
                var heightMeters = _tomTomUseVehicleDimensions ? Math.Clamp((dimensions?.HeightCm ?? 0) / 100d, 0d, 10d) : 0d;
                var weightKg = _tomTomUseVehicleWeightRestrictions ? Math.Clamp(vehicle?.GrossWeightKg ?? 0, 0, 100000) : 0;
                var hasDimension = lengthMeters > 0d || widthMeters > 0d || heightMeters > 0d || weightKg > 0;
                if (hasDimension)
                {
                    var profile = new TomTomRoutingProfile(
                        global::Tourenplaner.CSharp.App.Services.TomTomRoutingMode.HeightAware,
                        heightMeters,
                        lengthMeters,
                        widthMeters,
                        weightKg);
                    return new TomTomRoutingService(_tomTomApiKey, profile);
                }
            }
        }

        return CreateTomTomRoutingService(_tomTomApiKey, _tomTomRoutingMode, _tomTomVehicleHeightMeters);
    }

    public async Task UpdateMapOverlayOptionsAsync(string style, bool showTrafficFlow, bool showTrafficIncidents, bool showRoadLabels, bool showPoi, bool useVehicleDimensions, bool useVehicleWeightRestrictions, bool useDepartAtTraffic)
    {
        if ((useVehicleDimensions || useVehicleWeightRestrictions) && !HasAssignedPrimaryVehicle())
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Bitte ordnen Sie der Tour zuerst ein Fahrzeug zu, damit Fahrzeugmasse berücksichtigt werden können.",
                "Fahrzeug zuordnen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            useVehicleDimensions = false;
            useVehicleWeightRestrictions = false;
        }

        var normalizedStyle = NormalizeMapOverlayStyle(style);
        var changed = false;
        var routingOptionsChanged = false;

        if (!string.Equals(_tomTomMapOverlayStyle, normalizedStyle, StringComparison.OrdinalIgnoreCase))
        {
            _tomTomMapOverlayStyle = normalizedStyle;
            OnPropertyChanged(nameof(TomTomMapOverlayStyle));
            changed = true;
        }

        if (_tomTomShowTrafficFlow != showTrafficFlow)
        {
            _tomTomShowTrafficFlow = showTrafficFlow;
            OnPropertyChanged(nameof(TomTomShowTrafficFlow));
            changed = true;
        }

        if (_tomTomShowTrafficIncidents != showTrafficIncidents)
        {
            _tomTomShowTrafficIncidents = showTrafficIncidents;
            OnPropertyChanged(nameof(TomTomShowTrafficIncidents));
            changed = true;
        }

        if (_tomTomShowRoadLabels != showRoadLabels)
        {
            _tomTomShowRoadLabels = showRoadLabels;
            OnPropertyChanged(nameof(TomTomShowRoadLabels));
            changed = true;
        }

        if (_tomTomShowPoi != showPoi)
        {
            _tomTomShowPoi = showPoi;
            OnPropertyChanged(nameof(TomTomShowPoi));
            changed = true;
        }

        if (_tomTomUseVehicleDimensions != useVehicleDimensions)
        {
            _tomTomUseVehicleDimensions = useVehicleDimensions;
            OnPropertyChanged(nameof(TomTomUseVehicleDimensions));
            changed = true;
            routingOptionsChanged = true;
        }

        if (_tomTomUseVehicleWeightRestrictions != useVehicleWeightRestrictions)
        {
            _tomTomUseVehicleWeightRestrictions = useVehicleWeightRestrictions;
            OnPropertyChanged(nameof(TomTomUseVehicleWeightRestrictions));
            changed = true;
            routingOptionsChanged = true;
        }

        if (_tomTomUseDepartAtTraffic != useDepartAtTraffic)
        {
            _tomTomUseDepartAtTraffic = useDepartAtTraffic;
            OnPropertyChanged(nameof(TomTomUseDepartAtTraffic));
            changed = true;
            routingOptionsChanged = true;
        }

        if (!changed)
        {
            return;
        }

        var settings = await _settingsRepository.LoadAsync();
        settings.CurrentUserName = ResolveCurrentSettingsUserName(settings);
        settings.MapOverlayPreferencesByUser ??= new Dictionary<string, MapOverlayUserPreference>(StringComparer.OrdinalIgnoreCase);
        settings.MapOverlayPreferencesByUser[settings.CurrentUserName] = new MapOverlayUserPreference
        {
            Style = normalizedStyle,
            ShowTrafficFlow = showTrafficFlow,
            ShowTrafficIncidents = showTrafficIncidents,
            ShowRoadLabels = showRoadLabels,
            ShowPoi = showPoi,
            UseVehicleDimensions = useVehicleDimensions,
            UseVehicleWeightRestrictions = useVehicleWeightRestrictions,
            UseDepartAtTraffic = useDepartAtTraffic
        };
        await _settingsRepository.SaveAsync(settings);
        if (routingOptionsChanged)
        {
            RequestRouteGeometryRebuild();
        }
    }

    private void RequestPinInfoCardScaleSave()
    {
        _pinInfoCardScaleAutoSaveCts?.Cancel();
        _pinInfoCardScaleAutoSaveCts?.Dispose();
        _pinInfoCardScaleAutoSaveCts = new CancellationTokenSource();
        var token = _pinInfoCardScaleAutoSaveCts.Token;
        _ = SavePinInfoCardScaleAsync(token);
    }

    private async Task SavePinInfoCardScaleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            var settings = await _settingsRepository.LoadAsync(cancellationToken);
            var clampedScale = Math.Clamp(_pinInfoCardScale, 0.7d, 1.8d);
            var clampedZoomBehaviorStrength = Math.Clamp(_pinInfoCardZoomBehaviorStrength, 0.2d, 4.0d);
            if (Math.Abs(settings.PinInfoCardScale - clampedScale) < 0.0001d &&
                Math.Abs(settings.PinInfoCardZoomBehaviorStrength - clampedZoomBehaviorStrength) < 0.0001d)
            {
                return;
            }

            settings.PinInfoCardScale = clampedScale;
            settings.PinInfoCardZoomBehaviorStrength = clampedZoomBehaviorStrength;
            await _settingsRepository.SaveAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Slider moved again; persist only the latest value.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save pin info card scale: {ex.Message}");
        }
    }

    private static string ResolveCurrentSettingsUserName(AppSettings settings)
    {
        var configured = (settings.CurrentUserName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var environmentUser = (Environment.UserName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(environmentUser) ? "default" : environmentUser;
    }

    private static string NormalizeMapOverlayStyle(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "standard" => "standard",
            "light" => "light",
            "dark" => "dark",
            "satellite" => "satellite",
            _ => AppSettings.DefaultMapOverlayStyle
        };
    }

    private async void RequestRouteGeometryRebuild()
    {
        _routeRebuildDebounceCts?.Cancel();
        _routeRebuildDebounceCts?.Dispose();
        _routeRebuildDebounceCts = new CancellationTokenSource();
        var token = _routeRebuildDebounceCts.Token;
        var debounceMs = Math.Clamp(_tomTomRouteRecalcDebounceMs, 100, 10000);
        try
        {
            await Task.Delay(debounceMs, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            // Intentionally stay on UI context because we touch bindable state.
            await RebuildRouteGeometryAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RebuildRouteGeometryAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _routeGeometryInFlightCount);
        IsRouteCalculating = true;
        var revision = Interlocked.Increment(ref _routeGeometryRevision);
        try
        {
            var validStops = RouteStops
                .Where(x => !IsPauseStop(x))
                .Where(x => !double.IsNaN(x.Latitude) && !double.IsNaN(x.Longitude))
                .Where(x => x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180)
                .ToList();
            var waypoints = validStops.Select(x => new GeoPoint(x.Latitude, x.Longitude)).ToList();
            var plannedDeparture = BuildPlannedDepartureDateTime();
            var trafficDeparture = _tomTomUseDepartAtTraffic ? plannedDeparture : null;
            var routeSignature = BuildRouteSignature(waypoints, trafficDeparture);

            IReadOnlyList<GeoPoint> geometry = Array.Empty<GeoPoint>();
            IReadOnlyList<OsrmRouteLeg> legs = Array.Empty<OsrmRouteLeg>();
            IReadOnlyList<OsrmRouteTrafficSegment> trafficSegments = Array.Empty<OsrmRouteTrafficSegment>();
            var routingSource = "Fallback (Schaetzung)";
            if (waypoints.Count >= 2)
            {
                var activeRoutingService = GetTomTomRoutingServiceForCurrentRoute();
                if (!activeRoutingService.IsConfigured)
                {
                    geometry = waypoints;
                    legs = BuildEstimatedLegs(waypoints);
                    trafficSegments = Array.Empty<OsrmRouteTrafficSegment>();
                    routingSource = "TomTom-Key fehlt (Schaetzung)";
                }
                else
                {
                var loadedFromPersistentCache = TryLoadRouteComputationCache(routeSignature, trafficDeparture, out var cachedGeometry, out var cachedLegs, out var cachedTrafficSegments, out var cachedSource);
                if (loadedFromPersistentCache)
                {
                    geometry = cachedGeometry;
                    legs = cachedLegs;
                    trafficSegments = cachedTrafficSegments;
                    routingSource = $"Cache ({cachedSource})";
                    _lastRouteSignature = routeSignature;
                    _lastRouteProviderCallUtc = DateTime.UtcNow;
                }
                else
                {
                    var needsRemoteRefresh = !string.Equals(routeSignature, _lastRouteSignature, StringComparison.Ordinal) ||
                                             DateTime.UtcNow - _lastRouteProviderCallUtc >= TimeSpan.FromSeconds(Math.Max(15, _tomTomTrafficRefreshSeconds)) ||
                                             _routeTrafficSegments.Count == 0;

                    if (needsRemoteRefresh)
                    {
                        var tomTomResult = await activeRoutingService.TryBuildRouteWithLegsAsync(waypoints, trafficDeparture, cancellationToken);
                        if (tomTomResult.GeometryPoints.Count >= 2 && tomTomResult.Legs.Count == waypoints.Count - 1)
                        {
                            geometry = tomTomResult.GeometryPoints;
                            legs = tomTomResult.Legs;
                            trafficSegments = tomTomResult.TrafficSegments ?? Array.Empty<OsrmRouteTrafficSegment>();
                            routingSource = "TomTom";
                        }
                        else
                        {
                            geometry = waypoints;
                            legs = BuildEstimatedLegs(waypoints);
                            trafficSegments = Array.Empty<OsrmRouteTrafficSegment>();
                            routingSource = "TomTom keine Route (Schaetzung)";
                        }

                        _lastRouteProviderCallUtc = DateTime.UtcNow;
                        _lastRouteSignature = routeSignature;
                    }
                    else
                    {
                        geometry = _routeGeometryPoints.Count >= 2 ? _routeGeometryPoints.ToList() : waypoints;
                        legs = _routeLegs.Count == waypoints.Count - 1 ? _routeLegs.ToList() : BuildEstimatedLegs(waypoints);
                        trafficSegments = _routeTrafficSegments.Count > 0 ? _routeTrafficSegments.ToList() : Array.Empty<OsrmRouteTrafficSegment>();
                        routingSource = "Cache";
                    }
                }
                }

                if (geometry.Count < 2 || legs.Count != waypoints.Count - 1)
                {
                    geometry = waypoints;
                    legs = BuildEstimatedLegs(waypoints);
                    trafficSegments = Array.Empty<OsrmRouteTrafficSegment>();
                    routingSource = "Fallback (Schaetzung)";
                }

                if (geometry.Count >= 2 && legs.Count == waypoints.Count - 1 && !routingSource.StartsWith("Cache", StringComparison.OrdinalIgnoreCase))
                {
                    SaveRouteComputationCache(routeSignature, geometry, legs, trafficSegments, routingSource);
                }
            }
            else
            {
                routingSource = "Noch nicht berechnet";
            }

            if (revision != _routeGeometryRevision)
            {
                return;
            }

            _routeGeometryPoints.Clear();
            _routeGeometryPoints.AddRange(geometry);
            _routeLegs.Clear();
            _routeLegs.AddRange(legs);
            _routeTrafficSegments.Clear();
            _routeTrafficSegments.AddRange(trafficSegments);
            _timedStops.Clear();
            _timedStops.AddRange(validStops);
            var routeVehicle = ResolvePrimaryAssignedVehicle();
            RoutingProviderStatusText = $"Routing: {routingSource} | Masse: {(_tomTomUseVehicleDimensions ? "an" : "aus")} | Gewicht: {(_tomTomUseVehicleWeightRestrictions ? "an" : "aus")} | Traffic-Segmente: {_routeTrafficSegments.Count}";
            OnPropertyChanged(nameof(RouteGeometryPoints));
            RefreshDriveTimesFromCurrentRoute();
        }
        finally
        {
            var remaining = Interlocked.Decrement(ref _routeGeometryInFlightCount);
            if (remaining < 0)
            {
                Interlocked.Exchange(ref _routeGeometryInFlightCount, 0);
                remaining = 0;
            }

            IsRouteCalculating = remaining > 0;
        }
    }

    private string BuildRouteSignature(IReadOnlyList<GeoPoint> waypoints, DateTimeOffset? plannedDeparture)
    {
        var path = string.Join("|", waypoints.Select(x => $"{x.Latitude:F6},{x.Longitude:F6}"));
        var departAt = plannedDeparture?.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture) ?? "none";
        var vehicle = ResolvePrimaryAssignedVehicle();
        var dimensions = vehicle?.ExternalDimensions;

        var vehicleLengthMeters = _tomTomUseVehicleDimensions
            ? Math.Clamp((dimensions?.LengthCm ?? 0) / 100d, 0d, 50d)
            : 0d;
        var vehicleWidthMeters = _tomTomUseVehicleDimensions
            ? Math.Clamp((dimensions?.WidthCm ?? 0) / 100d, 0d, 10d)
            : 0d;
        var vehicleHeightMeters = _tomTomUseVehicleDimensions
            ? Math.Clamp((dimensions?.HeightCm ?? 0) / 100d, 0d, 10d)
            : 0d;
        var vehicleWeightKg = _tomTomUseVehicleWeightRestrictions
            ? Math.Clamp(vehicle?.GrossWeightKg ?? 0, 0, 100000)
            : 0;

        var restrictionsSignature = FormattableString.Invariant(
            $"dims={(_tomTomUseVehicleDimensions ? 1 : 0)}:{vehicleLengthMeters:0.###},{vehicleWidthMeters:0.###},{vehicleHeightMeters:0.###};weight={(_tomTomUseVehicleWeightRestrictions ? 1 : 0)}:{vehicleWeightKg};mode={_tomTomRoutingMode};heightAware={_tomTomVehicleHeightMeters:0.###}");

        return $"{path}#departAt={departAt}#trafficV2#{restrictionsSignature}";
    }

    private bool TryLoadRouteComputationCache(
        string routeSignature,
        DateTimeOffset? plannedDeparture,
        out IReadOnlyList<GeoPoint> geometry,
        out IReadOnlyList<OsrmRouteLeg> legs,
        out IReadOnlyList<OsrmRouteTrafficSegment> trafficSegments,
        out string source)
    {
        geometry = Array.Empty<GeoPoint>();
        legs = Array.Empty<OsrmRouteLeg>();
        trafficSegments = Array.Empty<OsrmRouteTrafficSegment>();
        source = "n/a";

        try
        {
            if (string.IsNullOrWhiteSpace(_routeComputationCachePath) || !File.Exists(_routeComputationCachePath))
            {
                return false;
            }

            var json = File.ReadAllText(_routeComputationCachePath);
            var cache = JsonSerializer.Deserialize<RouteComputationCacheEntry>(json);
            if (cache is null || !string.Equals(cache.Signature, routeSignature, StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsRouteCacheEntryFresh(cache, plannedDeparture))
            {
                return false;
            }

            if (cache.GeometryPoints is null || cache.Legs is null || cache.GeometryPoints.Count < 2 || cache.Legs.Count < 1)
            {
                return false;
            }

            geometry = cache.GeometryPoints
                .Select(x => new GeoPoint(x.Latitude, x.Longitude))
                .ToList();
            legs = cache.Legs
                .Select(x => new OsrmRouteLeg(Math.Max(0, x.DurationMinutes), Math.Max(0d, x.DistanceKm)))
                .ToList();
            trafficSegments = (cache.TrafficSegments ?? new List<RouteTrafficSegmentCacheItem>())
                .Select(x => new OsrmRouteTrafficSegment(
                    Math.Max(0, x.StartIndex),
                    Math.Max(0, x.EndIndex),
                    string.IsNullOrWhiteSpace(x.TrafficLevel) ? "unknown" : x.TrafficLevel.Trim()))
                .Where(x => x.EndIndex > x.StartIndex)
                .ToList();
            source = string.IsNullOrWhiteSpace(cache.Source) ? "unbekannt" : cache.Source.Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsRouteCacheEntryFresh(RouteComputationCacheEntry cache, DateTimeOffset? plannedDeparture)
    {
        var source = (cache.Source ?? string.Empty).Trim();
        if (!source.Contains("TomTom", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!DateTime.TryParse(cache.SavedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var savedUtcDateTime))
        {
            return false;
        }

        var savedUtc = new DateTimeOffset(savedUtcDateTime.ToUniversalTime(), TimeSpan.Zero);
        var nowUtc = DateTimeOffset.UtcNow;
        var age = nowUtc - savedUtc;

        // Keep long cache for historical/far-future planning, but force fresh traffic for near-now routes.
        if (!plannedDeparture.HasValue)
        {
            return age <= TimeSpan.FromMinutes(10);
        }

        var deltaToDeparture = (plannedDeparture.Value.ToUniversalTime() - nowUtc).Duration();
        if (deltaToDeparture <= TimeSpan.FromHours(24))
        {
            return age <= TimeSpan.FromMinutes(10);
        }

        return age <= TimeSpan.FromDays(7);
    }

    private void SaveRouteComputationCache(
        string routeSignature,
        IReadOnlyList<GeoPoint> geometry,
        IReadOnlyList<OsrmRouteLeg> legs,
        IReadOnlyList<OsrmRouteTrafficSegment> trafficSegments,
        string source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_routeComputationCachePath))
            {
                return;
            }

            var dir = Path.GetDirectoryName(_routeComputationCachePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var payload = new RouteComputationCacheEntry
            {
                Signature = routeSignature,
                Source = source,
                SavedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                GeometryPoints = geometry.Select(x => new RouteGeometryPointCacheItem
                {
                    Latitude = x.Latitude,
                    Longitude = x.Longitude
                }).ToList(),
                Legs = legs.Select(x => new RouteLegCacheItem
                {
                    DurationMinutes = x.DurationMinutes,
                    DistanceKm = x.DistanceKm
                }).ToList(),
                TrafficSegments = trafficSegments.Select(x => new RouteTrafficSegmentCacheItem
                {
                    StartIndex = x.StartIndex,
                    EndIndex = x.EndIndex,
                    TrafficLevel = x.TrafficLevel
                }).ToList()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(_routeComputationCachePath, json);
        }
        catch
        {
        }
    }

    private void RefreshDriveTimesFromCurrentRoute()
    {
        ClearRouteStopEtaValues();

        var routedStops = RouteStops
            .Where(x => !IsPauseStop(x))
            .Where(HasValidCoordinate)
            .ToList();

        if (routedStops.Count < 2 || _routeLegs.Count == 0)
        {
            if (!RouteStops.Any(IsOrderStop))
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

        var routeLegIndex = 0;
        RouteStopItem? previousRoutedStop = null;

        foreach (var stop in RouteStops)
        {
            if (IsPauseStop(stop))
            {
                stop.EtaText = current.ToString("HH:mm");
                previousRoutedStop?.AddPauseAfter(Math.Max(0, stop.PlannedStayMinutes));
                totalStayMinutes += Math.Max(0, stop.PlannedStayMinutes);
                current = current.AddMinutes(Math.Max(0, stop.PlannedStayMinutes));
                continue;
            }

            if (!HasValidCoordinate(stop))
            {
                continue;
            }

            if (previousRoutedStop is null)
            {
                stop.EtaText = start.ToString("HH:mm");
                previousRoutedStop = stop;
                continue;
            }

            if (routeLegIndex >= _routeLegs.Count)
            {
                break;
            }

            var leg = _routeLegs[routeLegIndex];
            var depart = current;
            current = current.AddMinutes(leg.DurationMinutes);
            var arrive = current;
            totalDriveMinutes += leg.DurationMinutes;
            totalDistanceKm += leg.DistanceKm;

            previousRoutedStop.SetNextLeg(
                durationText: FormatDuration(leg.DurationMinutes),
                distanceText: $"{leg.DistanceKm:0.0} km",
                departureText: depart.ToString("HH:mm"),
                arrivalText: arrive.ToString("HH:mm"),
                accentColorHex: routeLegIndex % 2 == 0 ? "#7BC6A4" : "#8EC6E8");

            sb.AppendLine($"{BuildStopLabel(previousRoutedStop, isFrom: true)} -> {BuildStopLabel(stop, isFrom: false)}");
            sb.AppendLine($"{leg.DurationMinutes} min | {leg.DistanceKm:0.0} km");
            sb.AppendLine($"{depart:HH:mm} -> {arrive:HH:mm}");
            if (routeLegIndex < _routeLegs.Count - 1)
            {
                sb.AppendLine();
            }

            stop.EtaText = arrive.ToString("HH:mm");
            if (!IsCompanyStop(stop) && !IsPauseStop(stop))
            {
                totalStayMinutes += Math.Max(0, stop.PlannedStayMinutes);
                current = current.AddMinutes(Math.Max(0, stop.PlannedStayMinutes));
            }

            previousRoutedStop = stop;
            routeLegIndex++;
        }

        var end = current;
        RouteTimingSummary = $"Start: {start:HH:mm} | Fahrt: {totalDriveMinutes} min | Aufenthalt: {totalStayMinutes} min | Ende: {end:HH:mm}";
        RouteOperationalSummaryText = $"Gesamt Fahrzeit: {FormatDuration(totalDriveMinutes)} | Gesamt Distanz: {totalDistanceKm:0.0} km | Start: {start:HH:mm} | Ende: {end:HH:mm}";
        DriveTimesText = sb.Length == 0 ? "Noch keine Stopps geplant." : sb.ToString().TrimEnd();
    }

    private void ClearDriveTimes()
    {
        RouteTimingSummary = "Noch keine Stopps geplant.";
        DriveTimesText = "Noch keine Stopps geplant.";
        RouteOperationalSummaryText = "Noch keine Stopps geplant.";
        RoutingProviderStatusText = "Routing: Noch nicht berechnet";
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
        var routeDate = ParseRouteDateOrToday();
        var hour = 8;
        var minute = 0;
        _ = int.TryParse(RouteStartHour, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour);
        _ = int.TryParse(RouteStartMinute, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute);
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        return new DateTime(routeDate.Year, routeDate.Month, routeDate.Day, hour, minute, 0, DateTimeKind.Local);
    }

    private DateTimeOffset? BuildPlannedDepartureDateTime()
    {
        var start = BuildStartDateTime();
        return new DateTimeOffset(start);
    }

    private DateTime ParseRouteDateOrToday()
    {
        var parsed = ParseDateForSort(RouteDate);
        return parsed == DateTime.MinValue ? DateTime.Today : parsed.Date;
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
            var plannedDeparture = BuildPlannedDepartureDateTime();
            var activeRoutingService = GetTomTomRoutingServiceForCurrentRoute();
            matrix = await activeRoutingService.TryBuildDurationMatrixMinutesAsync(points, plannedDeparture);
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
                        !x.IsArchived &&
                        (x.Location is null || AddressGeocodingService.IsLikelyCountryCentroid(x.Location)))
            .ToList();

        foreach (var order in candidates)
        {
            var location = await AddressGeocodingService.TryGeocodeOrderAsync(order, _tomTomApiKey, _geocodeCachePath);
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
            IsDimmed = isDimmed,
            IsBatchSelected = _selectedBatchOrderIds.Contains(order.Id)
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

        if (string.Equals(normalized, Order.PartiallyInTransitStatus, StringComparison.OrdinalIgnoreCase))
        {
            return _statusColorOrdered;
        }

        if (string.Equals(normalized, Order.InTransitStatus, StringComparison.OrdinalIgnoreCase) ||
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
            var weightLine = $"{unitWeightKg.ToString("0.##", culture)} kg/Stk";
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
                borderColor,
                _selectedDetailProductIndices.Contains(i)));
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
        string borderColor,
        bool isSelected)
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
        IsSelected = isSelected;
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
    public bool IsSelected { get; }
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
    public bool IsBatchSelected { get; set; }
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

public sealed record RouteStopRemovalUndoSnapshot(
    IReadOnlyList<MapRouteStop> RouteStops,
    string SelectedOrderId,
    bool HadUnsavedRouteChanges);

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
    private bool _isPauseStop;
    private bool _isStopSelected;
    private bool _isLegSelected;
    private int _plannedStayMinutes = 10;
    private string _etaText = string.Empty;
    private string _nextLegDurationText = string.Empty;
    private string _nextLegDistanceText = string.Empty;
    private string _nextLegDepartureText = string.Empty;
    private string _nextLegArrivalText = string.Empty;
    private string _nextLegAccentColor = "#8EC6E8";
    private string _employeeInfoText = string.Empty;
    private int _pauseAfterMinutes;

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

    public bool IsPauseStop
    {
        get => _isPauseStop;
        set
        {
            if (SetProperty(ref _isPauseStop, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(DisplayOrder));
                OnPropertyChanged(nameof(DisplayStay));
                OnPropertyChanged(nameof(DisplayPosition));
                OnPropertyChanged(nameof(DisplayEta));
                OnPropertyChanged(nameof(RouteBadgeText));
                OnPropertyChanged(nameof(DisplayNameWithOrder));
                OnPropertyChanged(nameof(DisplayAddress));
                OnPropertyChanged(nameof(DisplayEmployeeInfo));
            }
        }
    }

    public bool IsStopSelected
    {
        get => _isStopSelected;
        set => SetProperty(ref _isStopSelected, value);
    }

    public bool IsLegSelected
    {
        get => _isLegSelected;
        set => SetProperty(ref _isLegSelected, value);
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

    public string EmployeeInfoText
    {
        get => _employeeInfoText;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (SetProperty(ref _employeeInfoText, normalized))
            {
                OnPropertyChanged(nameof(DisplayEmployeeInfo));
            }
        }
    }

    public int PauseAfterMinutes
    {
        get => _pauseAfterMinutes;
        private set
        {
            var clamped = Math.Max(0, value);
            if (SetProperty(ref _pauseAfterMinutes, clamped))
            {
                OnPropertyChanged(nameof(HasPauseAfter));
                OnPropertyChanged(nameof(PauseAfterText));
            }
        }
    }

    private bool IsCompanyDisplay => IsCompanyAnchor ||
                                     string.Equals(OrderId, "__company_start__", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(OrderId, "__company_end__", StringComparison.OrdinalIgnoreCase) ||
                                     (OrderId ?? string.Empty).StartsWith("__company_", StringComparison.OrdinalIgnoreCase);

    public bool IsRouteStart => string.Equals(OrderId, "__company_start__", StringComparison.OrdinalIgnoreCase);
    public bool IsRouteEnd => string.Equals(OrderId, "__company_end__", StringComparison.OrdinalIgnoreCase);
    public string RouteBadgeText => IsRouteStart ? "Start" : IsRouteEnd ? "Ende" : string.Empty;
    public bool HasNextLeg => !string.IsNullOrWhiteSpace(NextLegDurationText) && !string.IsNullOrWhiteSpace(NextLegDistanceText);
    public bool HasPauseAfter => PauseAfterMinutes > 0;
    public string DisplayPosition => ToAlphaLabel(DisplayIndex > 0 ? DisplayIndex : Position);
    public string DisplayName => IsCompanyDisplay ? Address : (IsPauseStop ? "Pause" : (!string.IsNullOrWhiteSpace(Customer) ? Customer : Address));
    public string DisplayNameWithOrder =>
        IsCompanyDisplay || string.IsNullOrWhiteSpace(DisplayOrder) || string.Equals(DisplayOrder, "-", StringComparison.Ordinal)
            ? DisplayName
            : $"{DisplayName} ({DisplayOrder})";
    public string DisplayAddress => IsPauseStop || string.Equals(DisplayName, Address, StringComparison.OrdinalIgnoreCase) ? string.Empty : Address;
    public string DisplayWindow => "--";
    public string DisplayOrder => IsPauseStop ? string.Empty : (string.IsNullOrWhiteSpace(OrderId) ? "-" : OrderId);
    public string DisplayStay => IsCompanyDisplay ? string.Empty : $"{PlannedStayMinutes} min";
    public string DisplayEta => string.IsNullOrWhiteSpace(EtaText) ? "--:--" : EtaText;
    public string DisplayEmployeeInfo => IsCompanyDisplay || IsPauseStop ? string.Empty : EmployeeInfoText;
    public string PauseAfterText => $"{PauseAfterMinutes} min";

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

    public void AddPauseAfter(int minutes)
    {
        PauseAfterMinutes += Math.Max(0, minutes);
    }

    public void ClearPauseAfter()
    {
        PauseAfterMinutes = 0;
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

public sealed class PlannedTourRouteOverlay
{
    public PlannedTourRouteOverlay(int tourId, string label, string colorHex, IReadOnlyList<GeoPoint> points)
    {
        TourId = tourId;
        Label = string.IsNullOrWhiteSpace(label) ? $"Tour {tourId}" : label.Trim();
        ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#2563EB" : colorHex.Trim();
        Points = (points ?? []).Select(x => new GeoPoint(x.Latitude, x.Longitude)).ToList();
    }

    public int TourId { get; }
    public string Label { get; }
    public string ColorHex { get; }
    public IReadOnlyList<GeoPoint> Points { get; }

    public PlannedTourRouteOverlay Clone()
    {
        return new PlannedTourRouteOverlay(TourId, Label, ColorHex, Points);
    }
}

public sealed class SavedTourLookupItem
{
    public int TourId { get; set; }
    public string Label { get; set; } = string.Empty;

    public override string ToString()
    {
        return Label;
    }
}

public sealed class SavedTourOverviewItem
{
    public SavedTourOverviewItem(int tourId, string tourName, string dateText, string startTimeText, int stopCount)
    {
        TourId = tourId;
        TourName = string.IsNullOrWhiteSpace(tourName) ? $"Tour {tourId}" : tourName.Trim();
        DateText = string.IsNullOrWhiteSpace(dateText) ? "-" : dateText.Trim();
        StartTimeText = string.IsNullOrWhiteSpace(startTimeText) ? "--:--" : startTimeText.Trim();
        StopCount = Math.Max(0, stopCount);
    }

    public int TourId { get; }
    public string TourName { get; }
    public string DateText { get; }
    public string StartTimeText { get; }
    public int StopCount { get; }
    public string StopCountText => $"{StopCount} Stopps";
}

internal sealed class RouteComputationCacheEntry
{
    public string Signature { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SavedUtc { get; set; } = string.Empty;
    public List<RouteGeometryPointCacheItem> GeometryPoints { get; set; } = new();
    public List<RouteLegCacheItem> Legs { get; set; } = new();
    public List<RouteTrafficSegmentCacheItem> TrafficSegments { get; set; } = new();
}

internal sealed class RouteGeometryPointCacheItem
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

internal sealed class RouteLegCacheItem
{
    public int DurationMinutes { get; set; }
    public double DistanceKm { get; set; }
}

internal sealed class RouteTrafficSegmentCacheItem
{
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public string TrafficLevel { get; set; } = string.Empty;
}







