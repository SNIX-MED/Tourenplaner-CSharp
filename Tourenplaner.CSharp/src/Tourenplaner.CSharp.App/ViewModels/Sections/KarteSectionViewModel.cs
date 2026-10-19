using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class KarteSectionViewModel : SectionViewModelBase
{
    private const string CompanyStartStopId = "__company_start__";
    private const string CompanyEndStopId = "__company_end__";
    private const string PauseStopKind = "pause";
    private const string PauseStopIdPrefix = "pause:";
    private const string ManualStopKind = "manual";
    private const string ManualStopIdPrefix = "manual:";
    private const int DefaultPauseMinutes = 15;
    private const int MaxDraftRouteUndoEntries = 30;
    private const string PlannedTourStatus = "Eingeplant";
    private const string UnspecifiedSupplierFilterOption = "nicht Festgelegt";
    private const string AvisoBadgeColorNotAvisiert = "#64748B";
    private const string AvisoBadgeColorInformiert = "#F59E0B";
    private const string AvisoBadgeColorBestaetigt = "#16A34A";
    private const string ProductStatusColorPendingPreparation = "#16A34A";
    private static readonly string[] PlannedTourOverlayPalette =
    [
        "#2563EB",
        "#DC2626",
        "#16A34A",
        "#9333EA",
        "#EA580C",
        "#0891B2",
        "#DB2777",
        "#65A30D",
        "#7C2D12",
        "#0F172A"
    ];
    private static readonly IReadOnlyList<string> _orderStatusOptions =
    [
        Order.DefaultOrderStatus,
        Order.OrderedStatus,
        Order.InTransitStatus,
        Order.PartiallyInTransitStatus,
        Order.PendingPreparationStatus,
        Order.PartiallyPendingPreparationStatus,
        Order.PartiallyReadyStatus,
        Order.ReadyToDeliverStatus
    ];
    private static readonly IReadOnlyList<string> _avisoStatusOptions =
    [
        "nicht avisiert",
        "informiert",
        "Best\u00E4tigt"
    ];
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderMutationRepository? _orderMutationRepository;
    private readonly ITourRecordStore _tourRepository;
    private readonly ITourRecordMutationStore? _tourMutationStore;
    private readonly ITourOrderTransactionStore? _tourOrderTransactionStore;
    private readonly IEmployeeDataStore _employeeRepository;
    private readonly IVehicleDataStore _vehicleRepository;
    private readonly IAppSettingsStore _settingsRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly RouteOptimizationService _optimizationService;
    private readonly MapRouteService _mapRouteService;
    private readonly TourScheduleService _scheduleService;
    private readonly TourConflictService _conflictService;
    private readonly Dictionary<string, string> _employeeLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _employeeLabelsByWebfleetObjectUid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _employeeIdsByWebfleetObjectUid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WebfleetTrackDriverAssignment> _webfleetTrackDriversByObjectUid = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Order> _allOrders = new();
    private readonly List<TourRecord> _savedTours = new();
    private readonly List<GeoPoint> _routeGeometryPoints = new();
    private readonly List<OsrmRouteLeg> _routeLegs = new();
    private readonly List<OsrmRouteTrafficSegment> _routeTrafficSegments = new();
    private readonly List<RouteStopItem> _timedStops = new();
    private readonly List<PlannedTourRouteOverlay> _plannedTourRouteOverlays = new();
    private readonly List<WebfleetVehicleSnapshot> _webfleetVehicles = new();
    private readonly WebfleetLiveTrackSession _webfleetLiveTrack = new();
    private List<WebfleetTrackPoint> _webfleetTrackPoints => _webfleetLiveTrack.Points;
    private bool _webfleetTrackHistoryRefreshRunning;
    private int _webfleetTrackLoadRevision;
    private string? _webfleetTrackWarning;
    private readonly List<WebfleetPauseMarker> _webfleetPauseMarkers = new();
    private CollaborationResourceLease? _loadedTourLease;
    private readonly Dictionary<string, CollaborationResourceLease> _loadedTourOrderLeases = new(StringComparer.OrdinalIgnoreCase);
    private int _loadedTourLeaseId;
    private bool _isLoadedTourEditing;
    private bool _hasDeferredRemoteRefresh;
    private readonly SemaphoreSlim _tourAccessSyncGate = new(1, 1);
    private string? _loadedTourEditConcurrencyToken;
    private int _webfleetVehicleRevision;
    private int _webfleetTrackRevision;
    private bool _areWebfleetVehiclesVisible;
    private string _webfleetTrackVehicleName = string.Empty;
    private string _webfleetTrackStatusText = string.Empty;
    private VehicleDataRecord _vehicleData = new();

    private string _searchText = string.Empty;
    private readonly Dictionary<string, IReadOnlyList<MapSearchSuggestionItem>> _searchSuggestionCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _searchSuggestionCts;
    private MapSearchSuggestionItem? _selectedSearchSuggestion;
    private bool _isSearchSuggestionsOpen;
    private bool _suppressSearchSuggestionRefresh;
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
    private string _statusColorPartiallyReady = AppSettings.DefaultStatusColorPartiallyReady;
    private string _statusColorPendingPreparation = AppSettings.DefaultStatusColorPendingPreparation;
    private string _statusColorInStock = AppSettings.DefaultStatusColorInStock;
    private string _statusColorPlanned = AppSettings.DefaultStatusColorPlanned;
    private bool _mapUseDistinctPlannedTourColors = true;
    private bool _mapSearchDimNonMatchingPins;
    private string _tomTomApiKey = string.Empty;
    private bool _tomTomShowTrafficFlow;
    private bool _tomTomShowTrafficIncidents;
    private bool _tomTomShowRoadLabels = true;
    private bool _tomTomShowPoi = true;
    private bool _tomTomUseVehicleDimensions;
    private bool _tomTomUseVehicleWeightRestrictions;
    private bool _tomTomUseDepartAtTraffic = true;
    private bool _tomTomAvoidFerries = true;
    private string _tomTomMapOverlayStyle = AppSettings.DefaultMapOverlayStyle;
    private int _tomTomTrafficRefreshSeconds = AppSettings.DefaultTomTomTrafficRefreshSeconds;
    private int _tomTomRouteRecalcDebounceMs = AppSettings.DefaultTomTomRouteRecalcDebounceMs;
    private int _tomTomVehicleOnlyMaxSpeedKmh = AppSettings.DefaultTomTomVehicleOnlyMaxSpeedKmh;
    private int _tomTomVehicleWithTrailerMaxSpeedKmh = AppSettings.DefaultTomTomVehicleWithTrailerMaxSpeedKmh;
    private int _trafficBufferPercentFrom0500To0730 = AppSettings.DefaultTrafficBufferPercentFrom0500To0730;
    private int _trafficBufferPercentFrom0730To0900 = AppSettings.DefaultTrafficBufferPercentFrom0730To0900;
    private int _trafficBufferPercentFrom0900To1530 = AppSettings.DefaultTrafficBufferPercentFrom0900To1530;
    private int _trafficBufferPercentFrom1530To1830 = AppSettings.DefaultTrafficBufferPercentFrom1530To1830;
    private int _stayMinutesFreiBordsteinkante = AppSettings.DefaultStayMinutesFreiBordsteinkante;
    private int _stayMinutesMitVerteilung = AppSettings.DefaultStayMinutesMitVerteilung;
    private int _stayMinutesMitVerteilungMontage = AppSettings.DefaultStayMinutesMitVerteilungMontage;
    private string _tomTomTrafficSeverityMode = AppSettings.DefaultTomTomTrafficSeverityMode;
    private bool _tomTomEnableTileCache = true;
    private string _geocodeCachePath = string.Empty;
    private GeoPoint? _companyLocation;
    private bool _isDetailsOpen;
    private bool _isDetailsPanelExpanded = true;
    private bool _mapAutoOpenDetailsOnPinSelection = true;
    private bool _isRouteNameAutoManaged = true;
    private bool _suppressRouteNameAutoDetection;
    private bool _savedTourSelectionSync;
    private bool _suppressDetailStatusSave;
    private bool _suppressDetailAvisoStatusSave;
    private bool _suppressAutoDetailsOpenForNextSelection;
    private bool _suppressRouteChangeTracking;
    private bool _suppressFilterRefresh;
    private bool _isUpdatingFilterOptions;
    private bool _hasUnsavedRouteChanges;
    public override bool HasUnsavedChanges => _hasUnsavedRouteChanges;
    private bool _arePinInfoCardsVisible;
    private bool _isMapFilterPanelVisible;
    private bool _isMapLegendPanelVisible;
    private double _pinInfoCardScale = 1.0;
    private bool _mapPinInfoCardShowName = true;
    private bool _mapOpenPinInfoCardOnHover = true;
    private bool _mapPinInfoCardShowOrderNumber = true;
    private bool _mapPinInfoCardShowStreet = true;
    private bool _mapPinInfoCardShowPostalCodeCity = true;
    private bool _mapPinInfoCardShowNotes = true;
    private bool _mapPinInfoCardShowProducts = true;
    private bool _mapPinInfoCardShowTotalWeight = true;
    private double _pinInfoCardZoomBehaviorStrength = AppSettings.DefaultPinInfoCardZoomBehaviorStrength;
    private bool _isRouteCalculating;
    private bool _isReturnToCompanyLegVisible;
    private int _routeGeometryInFlightCount;
    private int _routeGeometryRevision;
    private int _activeTourId;
    private string _currentRouteVehicleId = string.Empty;
    private string _currentRouteTrailerId = string.Empty;
    private string _currentRouteSecondaryVehicleId = string.Empty;
    private string _currentRouteSecondaryTrailerId = string.Empty;
    private List<AdditionalMaterialGroup> _additionalMaterialGroups = new();
    private List<TourAdditionalMaterial> _currentAdditionalMaterials = new();
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
    private CancellationTokenSource? _webfleetVehicleRefreshCts;
    private string? _detailSelectedProductStatus;
    private bool _suppressDetailSelectedProductStatusApply;
    private bool _hasTemporarySearchPin;
    private double _temporarySearchPinLatitude;
    private double _temporarySearchPinLongitude;
    private string _temporarySearchPinLabel = string.Empty;
    private int _temporarySearchPinRevision;
    private int _searchFocusRevision;
    private string? _orderOpenedFromSearchId;
    private readonly SemaphoreSlim _orderRefreshGate = new(1, 1);
    private readonly Guid _instanceId = Guid.NewGuid();

    public KarteSectionViewModel(
        IOrderRepository orderRepository,
        ITourRecordStore tourRepository,
        IEmployeeDataStore employeeRepository,
        IVehicleDataStore vehicleRepository,
        IAppSettingsStore settingsRepository,
        string dataRoot,
        AppDataSyncService dataSyncService)
        : base("Karte", "Map order review, marker filters, route panel and save-to-tour workflow.")
    {
        _orderRepository = orderRepository;
        _orderMutationRepository = orderRepository as IOrderMutationRepository;
        _tourRepository = tourRepository;
        _tourMutationStore = tourRepository as ITourRecordMutationStore;
        _tourOrderTransactionStore = tourRepository as ITourOrderTransactionStore;
        _employeeRepository = employeeRepository;
        _vehicleRepository = vehicleRepository;
        _settingsRepository = settingsRepository;
        _dataSyncService = dataSyncService;
        _optimizationService = new RouteOptimizationService();
        _mapRouteService = new MapRouteService();
        _scheduleService = new TourScheduleService(
            AppSettings.DefaultTrafficBufferPercentFrom0500To0730,
            AppSettings.DefaultTrafficBufferPercentFrom0730To0900,
            AppSettings.DefaultTrafficBufferPercentFrom0900To1530,
            AppSettings.DefaultTrafficBufferPercentFrom1530To1830);
        _conflictService = new TourConflictService(_scheduleService);
        _geocodeCachePath = Path.Combine(dataRoot, "geocode-cache.json");
        _routeComputationCachePath = Path.Combine(dataRoot, "route-computation-cache.json");

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SearchCommand = new AsyncCommand(SearchAsync);
        CenterSelectedOrderCommand = new DelegateCommand(CenterSelectedOrder, () => SelectedOrder is not null);
        AddToRouteCommand = new DelegateCommand(AddSelectedOrderToRoute, CanAddSelectedOrderToCurrentRoute);
        AddToNewTourCommand = new DelegateCommand(AddSelectedOrderToNewTour, CanAddSelectedOrderToNewTour);
        AddSelectedOrdersToRouteCommand = new DelegateCommand(AddSelectedOrdersToRoute, CanAddSelectedOrdersToRoute);
        RemoveOrderFromTourCommand = new AsyncCommand(RemoveSelectedOrderFromTourAsync, CanRemoveSelectedOrderFromTour);
        RemoveSelectedOrdersFromTourCommand = new AsyncCommand(RemoveSelectedOrdersFromTourAsync, CanRemoveSelectedOrdersFromTour);
        ClearSelectedOrdersCommand = new DelegateCommand(ClearSelectedBatchOrders, () => SelectedBatchOrderCount > 0);
        RemoveFromRouteCommand = new DelegateCommand(RemoveSelectedRouteStop, () => CanModifyCurrentRoute() && SelectedRouteStop is not null && !IsCompanyStop(SelectedRouteStop));
        MoveStopUpCommand = new DelegateCommand(MoveSelectedStopUp, () => CanMoveSelectedStop(-1));
        MoveStopDownCommand = new DelegateCommand(MoveSelectedStopDown, () => CanMoveSelectedStop(1));
        OptimizeRouteCommand = new AsyncCommand(OptimizeRouteAsync, CanOptimizeRoute);
        OpenCreateTourDialogCommand = new AsyncCommand(OpenCreateTourDialogAsync, CanModifyCurrentRoute);
        EditSelectedTourCommand = new AsyncCommand(OpenEditSelectedTourDialogAsync, CanEditOrLeaveSelectedTour);
        StartSelectedTourEditingCommand = new AsyncCommand(StartSelectedTourEditingAsync, CanStartSelectedTourEditing);
        ArchiveSelectedTourCommand = new AsyncCommand(ArchiveSelectedTourAsync, CanEditOrLeaveSelectedTour);
        ExportRouteCommand = new AsyncCommand(ExportRouteAsync, CanExportRoute);
        SaveRouteAsTourCommand = new AsyncCommand(SaveRouteAsTourAsync, () => CanModifyCurrentRoute() && RouteStops.Any(x => !IsCompanyStop(x)));
        SaveCurrentTourCommand = new AsyncCommand(SaveCurrentTourAsync, CanSaveCurrentTour);
        SendCurrentTourToWebfleetCommand = new AsyncCommand(SendCurrentTourToWebfleetAsync, CanSendCurrentTourToWebfleet);
        RefreshWebfleetVehiclesCommand = new AsyncCommand(ToggleWebfleetVehiclesAsync);
        DeleteSelectedTourCommand = new AsyncCommand(DeleteSelectedTourAsync, CanEditOrLeaveSelectedTour);
        OpenSelectedTourOverviewCommand = new AsyncCommand(OpenSelectedTourOverviewAsync, CanOpenSelectedTourOverview);
        ClearRouteCommand = new DelegateCommand(ClearRoute, () => RouteStops.Any(x => !IsCompanyStop(x)));
        LeaveSelectedTourCommand = new DelegateCommand(LeaveSelectedTour, CanLeaveSelectedTour);
        PreviousTourCommand = new DelegateCommand(SwitchToPreviousTour, CanSwitchToPreviousTour);
        NextTourCommand = new DelegateCommand(SwitchToNextTour, CanSwitchToNextTour);
        ApplyStartTimeCommand = new DelegateCommand(ApplyRouteStartTime, CanModifyCurrentRoute);
        ToggleDetailsPanelCommand = new AsyncCommand(ToggleDetailsPanelAsync);
        CloseDetailsCommand = new DelegateCommand(CloseDetails, () => SelectedOrder is not null);
        ArchiveSelectedDeliveryCommand = new AsyncCommand(ArchiveSelectedDeliveryAsync, CanArchiveSelectedDelivery);
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

    public ObservableRangeCollection<MapOrderItem> MapOrders { get; } = new();

    public ObservableCollection<MapSearchSuggestionItem> SearchSuggestions { get; } = new();

    public ObservableRangeCollection<RouteStopItem> RouteStops { get; } = new();

    public ObservableCollection<SavedTourLookupItem> SavedTours { get; } = new();
    public ObservableCollection<SavedTourOverviewItem> TourOverviewItems { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }

    public ICommand CenterSelectedOrderCommand { get; }

    public ICommand AddToRouteCommand { get; }

    public ICommand AddToNewTourCommand { get; }

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

    public ICommand StartSelectedTourEditingCommand { get; }

    public ICommand ArchiveSelectedTourCommand { get; }

    public ICommand ExportRouteCommand { get; }

    public Func<RouteExportSnapshot, Task<RoutePdfExportResult>>? PdfExportHandler { get; set; }

    public ICommand SaveRouteAsTourCommand { get; }

    public ICommand SaveCurrentTourCommand { get; }
    public ICommand SendCurrentTourToWebfleetCommand { get; }
    public ICommand RefreshWebfleetVehiclesCommand { get; }
    public int WebfleetVehicleRevision => _webfleetVehicleRevision;
    public int WebfleetTrackRevision => _webfleetTrackRevision;
    public bool AreWebfleetVehiclesVisible => _areWebfleetVehiclesVisible;
    public string WebfleetVehiclesButtonToolTip => _areWebfleetVehiclesVisible
        ? "Live-Fahrzeugstandorte ausblenden"
        : "Live-Fahrzeugstandorte anzeigen und aktualisieren";
    public string WebfleetVehiclesButtonImagePath => _areWebfleetVehiclesVisible
        ? "/Assets/Webfleet-Fahrzeuge-ausblenden.png"
        : "/Assets/Webfleet-Fahrzeuge-einblenden.jpg";
    public string WebfleetTrackVehicleName => _webfleetTrackVehicleName;
    public string WebfleetTrackStatusText => _webfleetTrackStatusText;

    public ICommand DeleteSelectedTourCommand { get; }

    public ICommand OpenSelectedTourOverviewCommand { get; }

    public ICommand ClearRouteCommand { get; }

    public ICommand LeaveSelectedTourCommand { get; }

    public ICommand PreviousTourCommand { get; }

    public ICommand NextTourCommand { get; }

    public ICommand ApplyStartTimeCommand { get; }

    public ICommand ToggleDetailsPanelCommand { get; }

    public ICommand CloseDetailsCommand { get; }

    public ICommand ArchiveSelectedDeliveryCommand { get; }

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

    public ObservableCollection<MapOrderFilterOption> SupplierFilters { get; } = new();

    public string FilterSummaryText => BuildFilterSummaryText();
    public int ActiveFilterGroupCount => CountActiveFilterGroups();
    public bool HasActiveFilterGroups => ActiveFilterGroupCount > 0;
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
    public string CurrentTourPrimaryActionText => ResolveCurrentTourPrimaryActionText(ResolveCurrentTourId() > 0);
    public string CurrentTourPrimaryActionToolTip => ResolveCurrentTourId() > 0
        ? "Änderungen an der bestehenden Tour speichern"
        : "Datum, Abfahrtszeit, Name, Fahrzeug, Anhänger und Mitarbeiter festlegen";
    public string CurrentTourPrimaryActionAccessibleName => ResolveCurrentTourId() > 0
        ? "Änderungen an der bestehenden Tour speichern"
        : "Tourdetails festlegen";
    internal static string ResolveCurrentTourPrimaryActionText(bool isExistingTour) =>
        isExistingTour ? "Speichern" : "Tourdetails festlegen";
    public string ToggleAllPlannedToursButtonText => IsAllPlannedToursVisible ? "Touren ausblenden" : "Alle Touren anzeigen";
    public string ToggleAllPlannedToursImagePath => IsAllPlannedToursVisible
        ? "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/Touren-ausblenden.png"
        : "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/Touren-einblenden.png";

    public string ToggleAllFiltersButtonText => AreAllFiltersSelected() ? "Alle abwählen" : "Alle auswählen";

    public string LegendStatusColorNotSpecified => _statusColorNotSpecified;
    public string LegendStatusColorOrdered => _statusColorOrdered;
    public string LegendStatusColorOnTheWay => _statusColorOnTheWay;
    public string LegendStatusColorPartiallyReady => _statusColorPartiallyReady;
    public string LegendStatusColorPendingPreparation => _statusColorPendingPreparation;
    public string LegendStatusColorInStock => _statusColorInStock;
    public string LegendStatusColorPlanned => _statusColorPlanned;
    public string LegendAvisoBadgeColorNotAvisiert => AvisoBadgeColorNotAvisiert;
    public string LegendAvisoBadgeColorInformiert => AvisoBadgeColorInformiert;
    public string LegendAvisoBadgeColorBestaetigt => AvisoBadgeColorBestaetigt;
    public bool ShowRouteStopsPanel => _activeTourId > 0 || RouteStops.Any(x => !IsCompanyStop(x));
    public bool ShowTourOverviewPanel => !ShowRouteStopsPanel;
    public int LoadedTourId => _activeTourId > 0 ? _activeTourId : 0;
    public bool IsLoadedTourEditing => _isLoadedTourEditing && _loadedTourLeaseId == LoadedTourId && LoadedTourId > 0;
    internal bool IsEditingTour(int tourId) => tourId > 0 && IsLoadedTourEditing && LoadedTourId == tourId;
    internal int ResolveTourIdForDetailsEditing() => LoadedTourId > 0 ? LoadedTourId : ResolveCurrentTourId();
    public bool IsLoadedTourReadOnly => LoadedTourId > 0 && !IsLoadedTourEditing;
    public bool CanReorderRouteStops => LoadedTourId <= 0 || IsLoadedTourEditing;
    public string LoadedTourAccessText => IsLoadedTourEditing
        ? "Bearbeitungsmodus – diese Tour ist für andere Benutzer gesperrt."
        : IsLoadedTourReadOnly
            ? "Nur Ansicht – zum Ändern bitte 'Tour bearbeiten' wählen."
            : string.Empty;
    public string StartTourEditingToolTip => IsLoadedTourEditing
        ? "Bearbeitungsmodus für Tourstopps ist aktiv"
        : "Bearbeitungsmodus für Tourstopps starten";
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

    // Kept as a stable binding/test surface. Tour selections no longer request viewport focus.
    public int TourOverviewFocusRevision => 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _orderOpenedFromSearchId = null;
                if (string.IsNullOrWhiteSpace(value))
                {
                    ClearTemporarySearchPin();
                }

                RebuildOrderGrid(preserveSelectedOrderInstance: true);
                if (!_suppressSearchSuggestionRefresh)
                {
                    ScheduleSearchSuggestions(value);
                }
            }
        }
    }

    public MapSearchSuggestionItem? SelectedSearchSuggestion
    {
        get => _selectedSearchSuggestion;
        set => SetProperty(ref _selectedSearchSuggestion, value);
    }

    public bool IsSearchSuggestionsOpen
    {
        get => _isSearchSuggestionsOpen;
        set => SetProperty(ref _isSearchSuggestionsOpen, value);
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
        ? _selectedTourOverviewId
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

    public Func<Order, Task>? ShowOrderInListAsync { get; set; }

    public bool HasTemporarySearchPin => _hasTemporarySearchPin;
    public double TemporarySearchPinLatitude => _temporarySearchPinLatitude;
    public double TemporarySearchPinLongitude => _temporarySearchPinLongitude;
    public string TemporarySearchPinLabel => _temporarySearchPinLabel;
    public int TemporarySearchPinRevision => _temporarySearchPinRevision;
    public int SearchFocusRevision => _searchFocusRevision;

    public bool MapPinInfoCardShowName => _mapPinInfoCardShowName;
    public bool MapOpenPinInfoCardOnHover => _mapOpenPinInfoCardOnHover;
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
            if (!_suppressRouteChangeTracking && !CanModifyCurrentRoute()) return;
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
            if (!_suppressRouteChangeTracking && !CanModifyCurrentRoute()) return;
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
            if (!_suppressRouteChangeTracking && !CanModifyCurrentRoute()) return;
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
            if (!_suppressRouteChangeTracking && !CanModifyCurrentRoute()) return;
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

            _ = RunDetailSaveAsync(() => UpdateSelectedOrderStatusAsync(value));
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

            _ = RunDetailSaveAsync(() => UpdateSelectedAvisoStatusAsync(value));
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
            var productSelectionToRestore = string.Equals(_selectedOrder?.OrderId, value?.OrderId, StringComparison.OrdinalIgnoreCase)
                ? _selectedDetailProductIndices.OrderBy(x => x).ToList()
                : null;
            if (SetProperty(ref _selectedOrder, value))
            {
                if (value is null ||
                    !string.Equals(value.OrderId, _orderOpenedFromSearchId, StringComparison.OrdinalIgnoreCase))
                {
                    _orderOpenedFromSearchId = null;
                }
                var wasDetailsOpen = IsDetailsOpen;
                var shouldOpenDetails = value is not null &&
                                        (!_suppressAutoDetailsOpenForNextSelection || wasDetailsOpen);
                IsDetailsOpen = shouldOpenDetails;
                if (shouldOpenDetails)
                {
                    IsDetailsPanelExpanded = true;
                }
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
                if (productSelectionToRestore is not null)
                {
                    RestoreDetailProductSelection(
                        productSelectionToRestore,
                        FindSelectedOrderModel()?.Products ?? [],
                        raiseDetailItemsChanged: false);
                }
                else
                {
                    ResetDetailProductSelectionForCurrentOrder(raiseDetailItemsChanged: false);
                }
                OnPropertyChanged(nameof(DetailAddress));
                OnPropertyChanged(nameof(DetailCustomer));
                OnPropertyChanged(nameof(IsDetailLocationManuallySet));
                OnPropertyChanged(nameof(DetailLocationStatusText));
                OnPropertyChanged(nameof(DetailOrderNumber));
                OnPropertyChanged(nameof(DetailOrderStatus));
                OnPropertyChanged(nameof(DetailOrderStatusColor));
                OnPropertyChanged(nameof(DetailOrderStatusBadgeBackgroundColor));
                OnPropertyChanged(nameof(DetailAvisoStatus));
                OnPropertyChanged(nameof(CanEditDetailAvisoStatus));
                OnPropertyChanged(nameof(DetailTourStatus));
                OnPropertyChanged(nameof(DetailProducts));
                OnPropertyChanged(nameof(DetailProductItems));
                OnPropertyChanged(nameof(DetailManualTotalWeightKgText));
                OnPropertyChanged(nameof(DetailManualTotalWeightInfoText));
                OnPropertyChanged(nameof(DetailEffectiveTotalWeightText));
                OnPropertyChanged(nameof(DetailProductsTotalWeightText));
                OnPropertyChanged(nameof(DetailEmail));
                OnPropertyChanged(nameof(DetailPhone));
                OnPropertyChanged(nameof(DetailDeliveryType));
                OnPropertyChanged(nameof(DetailDeliveryDate));
                OnPropertyChanged(nameof(DetailDeliveryPeriod));
                OnPropertyChanged(nameof(DetailDeliveryCanOccurEarlier));
                OnPropertyChanged(nameof(DetailNotes));
                OnPropertyChanged(nameof(HasDetailNotes));
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
                    if (IsManualStop(value))
                    {
                        SelectedOrder = null;
                        IsDetailsOpen = true;
                        IsDetailsPanelExpanded = true;
                    }
                    else
                    {
                        SelectOrderDetailsById(value.MarkerId);
                    }
                }
                NotifyManualStopDetailsChanged();
                RaiseCommandStates();
            }
        }
    }

    public string DetailAddress => FormatOrderAddress(FindSelectedOrderModel());
    public string DetailCustomer => FormatDeliveryAddress(FindSelectedOrderModel());
    public bool IsDetailLocationManuallySet => FindSelectedOrderModel()?.IsLocationManuallySet == true;
    public string DetailLocationStatusText => FindSelectedOrderModel()?.ManualLocationRequiresReview == true
        ? "⚠ Manueller Pin – Adresse geändert"
        : "📍 Pin manuell gesetzt";
    public string DetailOrderNumber => SelectedOrder?.OrderId ?? "n/a";
    public string DetailOrderStatus
    {
        get
        {
            var order = FindSelectedOrderModel();
            return ResolveEffectiveOrderStatus(order, order is null ? null : FindSelectedDelivery(order));
        }
    }
    public string DetailOrderStatusColor
    {
        get
        {
            var order = FindSelectedOrderModel();
            return ResolveOrderStatusColor(order, order is null ? null : FindSelectedDelivery(order), isAssigned: false);
        }
    }
    public string DetailOrderStatusBadgeBackgroundColor => CreateOrderStatusBackground(FindSelectedOrderModel(), DetailOrderStatusColor);
    public string DetailAvisoStatus => NormalizeAvisoStatus(FindSelectedOrderModel()?.AvisoStatus);
    public bool CanEditDetailAvisoStatus
    {
        get => IsSelectedDeliveryAssignedOrInDraftRoute();
    }
    public string DetailTourStatus => SelectedOrder?.TourStatusLabel ?? "Offen";
    public string DetailProducts => OrderProductFormatter.BuildDetails(GetSelectedDeliveryProducts());
    public IReadOnlyList<DetailProductItem> DetailProductItems => BuildDetailProductItems(GetSelectedDeliveryProducts());
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

            _ = RunDetailSaveAsync(() => ApplySelectedDetailProductStatusAsync(normalized));
        }
    }
    public bool HasSelectedDetailProducts => _selectedDetailProductIndices.Count > 0;
    public string DetailSelectedProductsSummary => HasSelectedDetailProducts
        ? $"{_selectedDetailProductIndices.Count} Produkt(e) ausgewählt"
        : "Ctrl + Linksklick für Mehrfachauswahl";
    public string DetailEmail => FindSelectedOrderModel()?.Email ?? "n/a";
    public string DetailPhone => FindSelectedOrderModel()?.Phone ?? "n/a";
    public string DetailDeliveryType
    {
        get
        {
            var order = FindSelectedOrderModel();
            return order is null
                ? SelectedOrder?.DeliveryLabel ?? DeliveryMethodExtensions.FreiBordsteinkante
                : DeliveryMethodExtensions.GetPlanningDeliveryDisplayLabel(order);
        }
    }
    public string DetailDeliveryDate
    {
        get
        {
            var order = FindSelectedOrderModel();
            var delivery = order is null ? null : FindSelectedDelivery(order);
            return OrderDeliveryDateDisplayService.BuildDisplayText(order, _savedTours, delivery);
        }
    }
    public bool DetailDeliveryCanOccurEarlier => FindSelectedOrderModel()?.DeliveryCanOccurEarlier == true;
    public string DetailDeliveryPeriod => FindSelectedOrderModel()?.DeliveryPeriod ?? string.Empty;
    public string DetailNotes
    {
        get
        {
            var order = FindSelectedOrderModel();
            var delivery = order is null ? null : FindSelectedDelivery(order);
            return NormalizeUiText(!string.IsNullOrWhiteSpace(delivery?.Notes) ? delivery.Notes : order?.Notes);
        }
    }
    public bool HasDetailNotes => !string.IsNullOrWhiteSpace(DetailNotes);
    public string DetailManualTotalWeightKgText => FindSelectedOrderModel()?.ManualTotalWeightKg?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
    public string DetailManualTotalWeightInfoText => FindSelectedOrderModel()?.ManualTotalWeightInfo ?? string.Empty;
    public string DetailProductsTotalWeightText => FindSelectedOrderModel() is { } order
        ? $"Gesamtgewicht aller Produkte: {order.Products.Sum(OrderProductFormatter.ResolveTotalWeightKg).ToString("0.##", CultureInfo.CurrentCulture)} kg"
        : "Gesamtgewicht aller Produkte: 0 kg";
    public string DetailEffectiveTotalWeightText => FindSelectedOrderModel() is { } order
        ? $"Verwendetes Gesamtgewicht: {ResolveSelectedDeliveryWeight(order).ToString("0.##", CultureInfo.CurrentCulture)} kg"
        : string.Empty;
    public bool IsSelectedRouteStopManual => SelectedRouteStop is not null && IsManualStop(SelectedRouteStop);
    public string ManualStopDetailName => IsSelectedRouteStopManual ? SelectedRouteStop!.Customer : string.Empty;
    public string ManualStopDetailAddress => IsSelectedRouteStopManual ? SelectedRouteStop!.Address : string.Empty;
    public string ManualStopDetailStay => IsSelectedRouteStopManual ? $"{SelectedRouteStop!.PlannedStayMinutes} min" : string.Empty;
    public string ManualStopDetailNotes => IsSelectedRouteStopManual ? SelectedRouteStop!.EmployeeInfoText : string.Empty;

    public CompanyMarkerInfo? CompanyMarker =>
        _companyLocation is null
            ? null
            : new CompanyMarkerInfo(_companyName, _companyAddress, _companyLocation.Latitude, _companyLocation.Longitude);

    public string TomTomApiKey => _tomTomApiKey;
    public bool TomTomShowTrafficFlow => _tomTomShowTrafficFlow;
    public bool TomTomShowTrafficIncidents => _tomTomShowTrafficIncidents;
    public bool TomTomShowRoadLabels => _tomTomShowRoadLabels;
    public bool TomTomShowPoi => _tomTomShowPoi;
    public bool TomTomUseVehicleDimensions => _tomTomUseVehicleDimensions;
    public bool TomTomUseVehicleWeightRestrictions => _tomTomUseVehicleWeightRestrictions;
    public bool TomTomUseDepartAtTraffic => _tomTomUseDepartAtTraffic;
    public bool TomTomAvoidFerries => _tomTomAvoidFerries;
    public string TomTomMapOverlayStyle => _tomTomMapOverlayStyle;
    public int TomTomTrafficRefreshSeconds => _tomTomTrafficRefreshSeconds;
    public int TomTomRouteRecalcDebounceMs => _tomTomRouteRecalcDebounceMs;
    public int TomTomVehicleOnlyMaxSpeedKmh => _tomTomVehicleOnlyMaxSpeedKmh;
    public int TomTomVehicleWithTrailerMaxSpeedKmh => _tomTomVehicleWithTrailerMaxSpeedKmh;
    public int CurrentRouteAppliedMaxSpeedKmh => ResolveCurrentRouteMaxSpeedKmh();
    public bool HasCurrentRouteAppliedMaxSpeed => CurrentRouteAppliedMaxSpeedKmh > 0;
    public bool TomTomEnableTileCache => _tomTomEnableTileCache;

    public bool IsReturnToCompanyLegVisible => _isReturnToCompanyLegVisible;
}
