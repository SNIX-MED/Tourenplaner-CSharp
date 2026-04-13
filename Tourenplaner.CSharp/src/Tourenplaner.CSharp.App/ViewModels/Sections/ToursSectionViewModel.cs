using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class ToursSectionViewModel : SectionViewModelBase
{
    private static readonly CultureInfo UiCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly JsonToursRepository _tourRepository;
    private readonly JsonOrderRepository _orderRepository;
    private readonly JsonEmployeesRepository _employeeRepository;
    private readonly JsonVehicleDataRepository _vehicleRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly TourScheduleService _scheduleService;
    private readonly TourConflictService _conflictService;
    private readonly RouteOptimizationService _routeOptimizationService;
    private VehicleDataRecord _vehicleData = new();

    private readonly List<TourRecord> _loadedTours = new();
    private readonly Dictionary<string, string> _vehicleLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _trailerLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _employeeLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<int, Task>? _openTourOnMapAsync;
    private readonly Guid _instanceId = Guid.NewGuid();
    private bool _editorSyncInProgress;
    private bool _dateFilterSyncInProgress;
    private bool _isFromDatePopupOpen;
    private bool _isToDatePopupOpen;
    private DateTime _fromCalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _toCalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string _fromMonthDisplayText = string.Empty;
    private string _toMonthDisplayText = string.Empty;
    private DateFilterCalendarDayItem? _selectedFromCalendarDay;
    private DateFilterCalendarDayItem? _selectedToCalendarDay;

    private string _statusText = "Lade Touren...";
    private string _editorDate = string.Empty;
    private string _editorStartTime = "08:00";
    private string _editorConflictText = string.Empty;
    private string _fromDateText = string.Empty;
    private string _toDateText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private string _filterInfoText = "Alle Touren | Treffer: 0";
    private string _selectedTourWeightText = "Totalgewicht: 0 kg";
    private string _selectedTourLoadSummaryText = string.Empty;
    private string _selectedTourVehicleText = "Fahrzeug & Anhänger: -";
    private string _dragPreviewEtaText = string.Empty;
    private string _dragPreviewDurationText = string.Empty;
    private string _dragPreviewDistanceText = string.Empty;
    private string _lastDragPreviewSignature = string.Empty;
    private LookupItem? _selectedVehicle;
    private LookupItem? _selectedTrailer;
    private TourOverviewItem? _selectedTour;
    private TourStopOverviewItem? _selectedTourStop;
    private bool _showArchivedTours;

    public ToursSectionViewModel(
        string toursJsonPath,
        string ordersJsonPath,
        string employeesJsonPath,
        string vehiclesJsonPath,
        string settingsJsonPath,
        Func<int, Task>? openTourOnMapAsync = null,
        AppDataSyncService? dataSyncService = null)
        : base("Tours", "Tour creation, stop sequencing, ETA/ETD and assignment conflict checks.")
    {
        _tourRepository = new JsonToursRepository(toursJsonPath);
        _orderRepository = new JsonOrderRepository(ordersJsonPath);
        _employeeRepository = new JsonEmployeesRepository(employeesJsonPath);
        _vehicleRepository = new JsonVehicleDataRepository(vehiclesJsonPath);
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _dataSyncService = dataSyncService ?? new AppDataSyncService();
        _scheduleService = new TourScheduleService();
        _conflictService = new TourConflictService(_scheduleService);
        _routeOptimizationService = new RouteOptimizationService();
        _openTourOnMapAsync = openTourOnMapAsync;

        RefreshCommand = new AsyncCommand(RefreshAsync);
        RecalculateCommand = new AsyncCommand(RecalculateAndSaveAsync, () => Tours.Count > 0);
        SaveAssignmentCommand = new AsyncCommand(SaveSelectedAssignmentAsync, () => SelectedTour is not null);
        OpenTourOnMapCommand = new AsyncCommand(OpenSelectedTourOnMapAsync, () => SelectedTour is not null);
        EditTourOnMapCommand = new AsyncCommand(OpenSelectedTourOnMapForEditAsync, () => SelectedTour is not null);
        ApplyDateFilterCommand = new DelegateCommand(ApplyDateFilter);
        FilterTodayCommand = new DelegateCommand(SetTodayFilter);
        FilterTomorrowCommand = new DelegateCommand(SetTomorrowFilter);
        FilterThisWeekCommand = new DelegateCommand(SetWeekFilter);
        ResetDateFilterCommand = new DelegateCommand(ResetDateFilter);
        ToggleFromDatePopupCommand = new DelegateCommand(ToggleFromDatePopup);
        ToggleToDatePopupCommand = new DelegateCommand(ToggleToDatePopup);
        PreviousFromMonthCommand = new DelegateCommand(ShowPreviousFromMonth);
        NextFromMonthCommand = new DelegateCommand(ShowNextFromMonth);
        PreviousToMonthCommand = new DelegateCommand(ShowPreviousToMonth);
        NextToMonthCommand = new DelegateCommand(ShowNextToMonth);
        DeleteTourCommand = new AsyncCommand(DeleteSelectedTourAsync, () => SelectedTour is not null);
        ToggleArchiveTourCommand = new AsyncCommand(ToggleArchiveSelectedTourAsync, () => SelectedTour is not null);
        ShowActiveToursCommand = new DelegateCommand(() => ShowArchivedTours = false);
        ShowArchivedToursCommand = new DelegateCommand(() => ShowArchivedTours = true);
        EditSelectedTourStopStayMinutesCommand = new AsyncCommand(EditSelectedTourStopStayMinutesAsync, () => SelectedTourStop is not null && !SelectedTourStop.IsCompanyStop);
        RemoveSelectedTourStopCommand = new AsyncCommand(RemoveSelectedTourStopAsync, () => SelectedTourStop is not null && !SelectedTourStop.IsCompanyStop);
        EditSelectedTourStopOrderCommand = new AsyncCommand(EditSelectedTourStopOrderAsync, () => SelectedTourStop is not null && !SelectedTourStop.IsCompanyStop);
        RebuildFromCalendarDays();
        RebuildToCalendarDays();
        _dataSyncService.DataChanged += OnDataChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<TourOverviewItem> Tours { get; } = new();

    public ObservableCollection<TourStopOverviewItem> SelectedTourStops { get; } = new();

    public ObservableCollection<LookupItem> AvailableVehicles { get; } = new();

    public ObservableCollection<LookupItem> AvailableTrailers { get; } = new();

    public ObservableCollection<SelectableEmployeeItem> AvailableEmployees { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand RecalculateCommand { get; }

    public ICommand SaveAssignmentCommand { get; }

    public ICommand ApplyDateFilterCommand { get; }

    public ICommand FilterTodayCommand { get; }

    public ICommand FilterTomorrowCommand { get; }

    public ICommand FilterThisWeekCommand { get; }

    public ICommand ResetDateFilterCommand { get; }

    public ICommand DeleteTourCommand { get; }

    public ICommand ToggleArchiveTourCommand { get; }

    public ICommand ShowActiveToursCommand { get; }

    public ICommand ShowArchivedToursCommand { get; }

    public ICommand EditSelectedTourStopStayMinutesCommand { get; }

    public ICommand RemoveSelectedTourStopCommand { get; }

    public ICommand EditSelectedTourStopOrderCommand { get; }

    public ICommand OpenTourOnMapCommand { get; }

    public ICommand EditTourOnMapCommand { get; }

    public ICommand ToggleFromDatePopupCommand { get; }

    public ICommand ToggleToDatePopupCommand { get; }

    public ICommand PreviousFromMonthCommand { get; }

    public ICommand NextFromMonthCommand { get; }

    public ICommand PreviousToMonthCommand { get; }

    public ICommand NextToMonthCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool ShowArchivedTours
    {
        get => _showArchivedTours;
        set
        {
            if (!SetProperty(ref _showArchivedTours, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowActiveTours));
            RebuildTourRowsWithCurrentFilter(keepSelectionTourId: SelectedTour?.TourId);
        }
    }

    public bool ShowActiveTours => !ShowArchivedTours;

    public string ToggleArchiveTourButtonText => SelectedTour?.Source.IsArchived == true ? "Tour reaktivieren" : "Tour archivieren";

    public string EditorDate
    {
        get => _editorDate;
        set
        {
            if (SetProperty(ref _editorDate, value))
            {
                UpdateEditorConflictPreview();
            }
        }
    }

    public string EditorStartTime
    {
        get => _editorStartTime;
        set
        {
            if (SetProperty(ref _editorStartTime, value))
            {
                UpdateEditorConflictPreview();
            }
        }
    }

    public LookupItem? SelectedVehicle
    {
        get => _selectedVehicle;
        set
        {
            if (SetProperty(ref _selectedVehicle, value))
            {
                UpdateEditorConflictPreview();
                UpdateSelectedTourSummary();
            }
        }
    }

    public LookupItem? SelectedTrailer
    {
        get => _selectedTrailer;
        set
        {
            if (SetProperty(ref _selectedTrailer, value))
            {
                UpdateEditorConflictPreview();
                UpdateSelectedTourSummary();
            }
        }
    }

    public string EditorConflictText
    {
        get => _editorConflictText;
        private set => SetProperty(ref _editorConflictText, value);
    }

    public string DragPreviewEtaText
    {
        get => _dragPreviewEtaText;
        private set => SetProperty(ref _dragPreviewEtaText, value);
    }

    public string DragPreviewDurationText
    {
        get => _dragPreviewDurationText;
        private set => SetProperty(ref _dragPreviewDurationText, value);
    }

    public string DragPreviewDistanceText
    {
        get => _dragPreviewDistanceText;
        private set => SetProperty(ref _dragPreviewDistanceText, value);
    }

    public bool HasDragPreview => !string.IsNullOrWhiteSpace(DragPreviewEtaText);

    public string FromDateText
    {
        get => _fromDateText;
        set
        {
            if (!SetProperty(ref _fromDateText, value))
            {
                return;
            }

            if (_dateFilterSyncInProgress)
            {
                return;
            }

            _dateFilterSyncInProgress = true;
            try
            {
                var parsed = ParseDate(value);
                if (_fromDate != parsed)
                {
                    _fromDate = parsed;
                    OnPropertyChanged(nameof(FromDate));
                    OnPropertyChanged(nameof(FromDateDisplayText));
                    if (parsed.HasValue)
                    {
                        _fromCalendarMonth = new DateTime(parsed.Value.Year, parsed.Value.Month, 1);
                    }

                    RebuildFromCalendarDays();
                    ApplyDateFilter();
                }
            }
            finally
            {
                _dateFilterSyncInProgress = false;
            }
        }
    }

    public string ToDateText
    {
        get => _toDateText;
        set
        {
            if (!SetProperty(ref _toDateText, value))
            {
                return;
            }

            if (_dateFilterSyncInProgress)
            {
                return;
            }

            _dateFilterSyncInProgress = true;
            try
            {
                var parsed = ParseDate(value);
                if (_toDate != parsed)
                {
                    _toDate = parsed;
                    OnPropertyChanged(nameof(ToDate));
                    OnPropertyChanged(nameof(ToDateDisplayText));
                    if (parsed.HasValue)
                    {
                        _toCalendarMonth = new DateTime(parsed.Value.Year, parsed.Value.Month, 1);
                    }

                    RebuildToCalendarDays();
                    ApplyDateFilter();
                }
            }
            finally
            {
                _dateFilterSyncInProgress = false;
            }
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            var normalized = value?.Date;
            if (_fromDate == normalized)
            {
                return;
            }

            _fromDate = normalized;
            OnPropertyChanged();

            if (_dateFilterSyncInProgress)
            {
                return;
            }

            _dateFilterSyncInProgress = true;
            try
            {
                var text = normalized.HasValue
                    ? normalized.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                    : string.Empty;
                if (_fromDateText != text)
                {
                    _fromDateText = text;
                    OnPropertyChanged(nameof(FromDateText));
                }
            }
            finally
            {
                _dateFilterSyncInProgress = false;
            }

            OnPropertyChanged(nameof(FromDateDisplayText));
            if (normalized.HasValue)
            {
                _fromCalendarMonth = new DateTime(normalized.Value.Year, normalized.Value.Month, 1);
            }

            RebuildFromCalendarDays();
            ApplyDateFilter();
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            var normalized = value?.Date;
            if (_toDate == normalized)
            {
                return;
            }

            _toDate = normalized;
            OnPropertyChanged();

            if (_dateFilterSyncInProgress)
            {
                return;
            }

            _dateFilterSyncInProgress = true;
            try
            {
                var text = normalized.HasValue
                    ? normalized.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                    : string.Empty;
                if (_toDateText != text)
                {
                    _toDateText = text;
                    OnPropertyChanged(nameof(ToDateText));
                }
            }
            finally
            {
                _dateFilterSyncInProgress = false;
            }

            OnPropertyChanged(nameof(ToDateDisplayText));
            if (normalized.HasValue)
            {
                _toCalendarMonth = new DateTime(normalized.Value.Year, normalized.Value.Month, 1);
            }

            RebuildToCalendarDays();
            ApplyDateFilter();
        }
    }

    public string FromDateDisplayText => FromDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "Datum auswählen";

    public string ToDateDisplayText => ToDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "Datum auswählen";

    public bool IsFromDatePopupOpen
    {
        get => _isFromDatePopupOpen;
        set => SetProperty(ref _isFromDatePopupOpen, value);
    }

    public bool IsToDatePopupOpen
    {
        get => _isToDatePopupOpen;
        set => SetProperty(ref _isToDatePopupOpen, value);
    }

    public string FromMonthDisplayText
    {
        get => _fromMonthDisplayText;
        private set => SetProperty(ref _fromMonthDisplayText, value);
    }

    public string ToMonthDisplayText
    {
        get => _toMonthDisplayText;
        private set => SetProperty(ref _toMonthDisplayText, value);
    }

    public ObservableCollection<DateFilterCalendarDayItem> FromCalendarDays { get; } = new();

    public ObservableCollection<DateFilterCalendarDayItem> ToCalendarDays { get; } = new();

    public DateFilterCalendarDayItem? SelectedFromCalendarDay
    {
        get => _selectedFromCalendarDay;
        set
        {
            if (!SetProperty(ref _selectedFromCalendarDay, value) || value is null)
            {
                return;
            }

            FromDate = value.Date;
            IsFromDatePopupOpen = false;
            _selectedFromCalendarDay = null;
            OnPropertyChanged();
        }
    }

    public DateFilterCalendarDayItem? SelectedToCalendarDay
    {
        get => _selectedToCalendarDay;
        set
        {
            if (!SetProperty(ref _selectedToCalendarDay, value) || value is null)
            {
                return;
            }

            ToDate = value.Date;
            IsToDatePopupOpen = false;
            _selectedToCalendarDay = null;
            OnPropertyChanged();
        }
    }

    public string FilterInfoText
    {
        get => _filterInfoText;
        private set => SetProperty(ref _filterInfoText, value);
    }

    public string SelectedTourWeightText
    {
        get => _selectedTourWeightText;
        private set => SetProperty(ref _selectedTourWeightText, value);
    }

    public string SelectedTourLoadSummaryText
    {
        get => _selectedTourLoadSummaryText;
        private set => SetProperty(ref _selectedTourLoadSummaryText, value);
    }

    public string SelectedTourVehicleText
    {
        get => _selectedTourVehicleText;
        private set => SetProperty(ref _selectedTourVehicleText, value);
    }

    public TourOverviewItem? SelectedTour
    {
        get => _selectedTour;
        set
        {
            if (SetProperty(ref _selectedTour, value))
            {
                ClearDragReorderPreview();
                LoadSelectedTourStops();
                SyncEditorFromSelection();
                UpdateSelectedTourSummary();
                OnPropertyChanged(nameof(ToggleArchiveTourButtonText));
                RaiseCommandStates();
            }
        }
    }

    public TourStopOverviewItem? SelectedTourStop
    {
        get => _selectedTourStop;
        set
        {
            if (SetProperty(ref _selectedTourStop, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        await LoadReferenceDataAsync();
        var settings = await _settingsRepository.LoadAsync();
        _loadedTours.Clear();
        _loadedTours.AddRange(await _tourRepository.LoadAsync());
        if (NormalizeCompanyStops(_loadedTours, settings))
        {
            await _tourRepository.SaveAsync(_loadedTours);
            _dataSyncService.PublishTours(_instanceId);
        }

        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: SelectedTour?.TourId);
        RaiseCommandStates();
    }

    public async Task RecalculateAndSaveAsync()
    {
        var tours = (await _tourRepository.LoadAsync()).ToList();
        foreach (var tour in tours)
        {
            _scheduleService.ApplySchedule(tour);
        }

        await _tourRepository.SaveAsync(tours);
        _dataSyncService.PublishTours(_instanceId);
        await RefreshAsync();
    }

    public async Task SaveSelectedAssignmentAsync()
    {
        if (SelectedTour is null)
        {
            return;
        }

        var target = _loadedTours.FirstOrDefault(t => t.Id == SelectedTour.TourId);
        if (target is null)
        {
            return;
        }

        var originalAssignment = CaptureAssignment(target);

        target.Date = (EditorDate ?? string.Empty).Trim();
        target.StartTime = (EditorStartTime ?? string.Empty).Trim();
        target.VehicleId = SelectedVehicle?.Id;
        target.TrailerId = SelectedTrailer?.Id;
        target.EmployeeIds = AvailableEmployees
            .Where(e => e.IsSelected)
            .Select(e => e.Id)
            .Take(2)
            .ToList();

        var availabilityError = await BuildAvailabilityErrorAsync(
            target.Date,
            target.VehicleId,
            target.TrailerId,
            target.SecondaryVehicleId,
            target.SecondaryTrailerId,
            target.EmployeeIds);
        if (!string.IsNullOrWhiteSpace(availabilityError))
        {
            MessageBox.Show(availabilityError, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            RestoreAssignment(target, originalAssignment);
            return;
        }

        if (!ConfirmPlanningWarnings(_loadedTours, target.Id))
        {
            RestoreAssignment(target, originalAssignment);
            return;
        }

        _scheduleService.ApplySchedule(target);
        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(_instanceId, target.Id.ToString(CultureInfo.InvariantCulture), target.Id.ToString(CultureInfo.InvariantCulture));
        await RefreshAsync();
    }

    private void ApplyDateFilter()
    {
        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: SelectedTour?.TourId);
    }

    private void ToggleFromDatePopup()
    {
        ToggleDatePopup(isFrom: true);
    }

    private void ToggleToDatePopup()
    {
        ToggleDatePopup(isFrom: false);
    }

    private void ShowPreviousFromMonth()
    {
        ShiftCalendarMonth(isFrom: true, monthDelta: -1);
    }

    private void ShowNextFromMonth()
    {
        ShiftCalendarMonth(isFrom: true, monthDelta: 1);
    }

    private void ShowPreviousToMonth()
    {
        ShiftCalendarMonth(isFrom: false, monthDelta: -1);
    }

    private void ShowNextToMonth()
    {
        ShiftCalendarMonth(isFrom: false, monthDelta: 1);
    }

    private void SetTodayFilter()
    {
        var today = DateTime.Today;
        SetDateFilter(today, today);
    }

    private void SetTomorrowFilter()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        SetDateFilter(tomorrow, tomorrow);
    }

    private void SetWeekFilter()
    {
        var today = DateTime.Today;
        var offset = (7 + ((int)today.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        var start = today.AddDays(-offset);
        var end = start.AddDays(6);
        SetDateFilter(start, end);
    }

    private void ResetDateFilter()
    {
        SetDateFilter(null, null);
    }

    public async Task FocusTourAsync(int tourId)
    {
        var match = Tours.FirstOrDefault(t => t.TourId == tourId);
        if (match is null)
        {
            await RefreshAsync();
            match = Tours.FirstOrDefault(t => t.TourId == tourId);
        }

        if (match is not null)
        {
            SelectedTour = match;
        }
    }

    public async Task FocusDateAsync(DateTime date, int? preferredTourId = null)
    {
        if (_loadedTours.Count == 0)
        {
            await RefreshAsync();
        }

        var dayText = date.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        FromDateText = dayText;
        ToDateText = dayText;
        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: preferredTourId);

        if (preferredTourId.HasValue)
        {
            var preferred = Tours.FirstOrDefault(t => t.TourId == preferredTourId.Value);
            if (preferred is not null)
            {
                SelectedTour = preferred;
                return;
            }
        }

        SelectedTour = Tours.FirstOrDefault();
    }

    public async Task<bool> MoveStopWithinSelectedTourAsync(
        TourStopOverviewItem sourceItem,
        TourStopOverviewItem? targetItem,
        bool insertAfterTarget)
    {
        if (sourceItem.Source is null ||
            sourceItem.IsCompanyStop ||
            SelectedTour?.Source is null)
        {
            return false;
        }

        var tour = _loadedTours.FirstOrDefault(x => x.Id == SelectedTour.TourId);
        if (tour is null)
        {
            return false;
        }

        var beforeMetrics = BuildTourOrderMetrics(tour);
        if (!TryBuildReorderedStops(tour, sourceItem.Source, targetItem?.Source, insertAfterTarget, out var reorderedStops))
        {
            return false;
        }

        tour.Stops = reorderedStops;
        _scheduleService.ApplySchedule(tour);

        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(_instanceId, tour.Id.ToString(CultureInfo.InvariantCulture), tour.Id.ToString(CultureInfo.InvariantCulture));

        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: tour.Id);
        ClearDragReorderPreview();

        var afterMetrics = BuildTourOrderMetrics(tour);
        var etaDelta = FormatSignedMinutes(afterMetrics.EndMinutes - beforeMetrics.EndMinutes);
        var durationDelta = FormatSignedMinutes(afterMetrics.DurationMinutes - beforeMetrics.DurationMinutes);
        var distanceDelta = FormatSignedDistance(afterMetrics.DistanceKm - beforeMetrics.DistanceKm);
        StatusText = $"Stopps in Tour {tour.Name} neu angeordnet | ETA: {afterMetrics.EndTime} ({etaDelta}) | Dauer: {FormatDuration(afterMetrics.DurationMinutes)} ({durationDelta}) | Distanz: {afterMetrics.DistanceKm:0.0} km ({distanceDelta})";
        return true;
    }

    public void UpdateDragReorderPreview(TourStopOverviewItem sourceItem, TourStopOverviewItem? targetItem, bool insertAfterTarget)
    {
        if (sourceItem.Source is null ||
            sourceItem.IsCompanyStop ||
            SelectedTour?.Source is null)
        {
            ClearDragReorderPreview();
            return;
        }

        var tour = _loadedTours.FirstOrDefault(x => x.Id == SelectedTour.TourId);
        if (tour is null)
        {
            ClearDragReorderPreview();
            return;
        }

        var signature = $"{tour.Id}|{sourceItem.Source.Id}|{targetItem?.Source?.Id ?? "_tail"}|{insertAfterTarget}";
        if (string.Equals(signature, _lastDragPreviewSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastDragPreviewSignature = signature;
        if (!TryBuildReorderedStops(tour, sourceItem.Source, targetItem?.Source, insertAfterTarget, out var reorderedStops))
        {
            ClearDragReorderPreview();
            return;
        }

        var currentMetrics = BuildTourOrderMetrics(tour);
        var previewMetrics = BuildTourOrderMetrics(tour, reorderedStops);

        DragPreviewEtaText = $"ETA Ende: {previewMetrics.EndTime} ({FormatSignedMinutes(previewMetrics.EndMinutes - currentMetrics.EndMinutes)})";
        DragPreviewDurationText = $"Dauer: {FormatDuration(previewMetrics.DurationMinutes)} ({FormatSignedMinutes(previewMetrics.DurationMinutes - currentMetrics.DurationMinutes)})";
        DragPreviewDistanceText = $"Distanz: {previewMetrics.DistanceKm:0.0} km ({FormatSignedDistance(previewMetrics.DistanceKm - currentMetrics.DistanceKm)})";
        OnPropertyChanged(nameof(HasDragPreview));
    }

    public void ClearDragReorderPreview()
    {
        _lastDragPreviewSignature = string.Empty;
        DragPreviewEtaText = string.Empty;
        DragPreviewDurationText = string.Empty;
        DragPreviewDistanceText = string.Empty;
        OnPropertyChanged(nameof(HasDragPreview));
    }

    public async Task<bool> MoveStopToTourAsync(TourStopOverviewItem sourceItem, TourOverviewItem targetTourItem)
    {
        if (sourceItem.Source is null ||
            sourceItem.IsCompanyStop)
        {
            return false;
        }

        var sourceTour = _loadedTours.FirstOrDefault(x => x.Id == sourceItem.SourceTourId);
        var targetTour = _loadedTours.FirstOrDefault(x => x.Id == targetTourItem.TourId);
        if (sourceTour is null || targetTour is null || sourceTour.Id == targetTour.Id)
        {
            return false;
        }

        if (!ConfirmStopReassignment(sourceItem, sourceTour, targetTour))
        {
            return false;
        }

        var sourceMovable = sourceTour.Stops.Where(s => !IsCompanyStop(s)).ToList();
        var targetMovable = targetTour.Stops.Where(s => !IsCompanyStop(s)).ToList();

        var sourceIndex = sourceMovable.IndexOf(sourceItem.Source);
        if (sourceIndex < 0)
        {
            return false;
        }

        var movedStop = sourceMovable[sourceIndex];
        sourceMovable.RemoveAt(sourceIndex);
        targetMovable.Add(movedStop);

        RebuildTourStopsWithAnchors(sourceTour, sourceMovable);
        RebuildTourStopsWithAnchors(targetTour, targetMovable);
        _scheduleService.ApplySchedule(sourceTour);
        _scheduleService.ApplySchedule(targetTour);

        var movedOrderId = ExtractOrderIdFromStop(movedStop);
        var updatedOrder = false;
        if (!string.IsNullOrWhiteSpace(movedOrderId))
        {
            var orders = (await _orderRepository.GetAllAsync()).ToList();
            var order = orders.FirstOrDefault(o => string.Equals(o.Id, movedOrderId, StringComparison.OrdinalIgnoreCase));
            if (order is not null)
            {
                order.AssignedTourId = targetTour.Id.ToString(CultureInfo.InvariantCulture);
                await _orderRepository.SaveAllAsync(orders);
                updatedOrder = true;
            }
        }

        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(
            _instanceId,
            sourceTour.Id.ToString(CultureInfo.InvariantCulture),
            targetTour.Id.ToString(CultureInfo.InvariantCulture));
        if (updatedOrder)
        {
            _dataSyncService.PublishOrders(_instanceId);
        }

        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: targetTour.Id);
        StatusText = $"Stopp wurde von Tour {sourceTour.Name} nach {targetTour.Name} verschoben.";
        return true;
    }

    public async Task EditSelectedTourStopStayMinutesAsync()
    {
        if (SelectedTourStop?.Source is null ||
            SelectedTourStop.IsCompanyStop)
        {
            return;
        }

        var sourceTour = _loadedTours.FirstOrDefault(x => x.Id == SelectedTourStop.SourceTourId);
        if (sourceTour is null)
        {
            return;
        }

        var stop = sourceTour.Stops.FirstOrDefault(x => ReferenceEquals(x, SelectedTourStop.Source)) ??
                   sourceTour.Stops.FirstOrDefault(x =>
                       x.Order == SelectedTourStop.Source.Order &&
                       string.Equals(x.Auftragsnummer, SelectedTourStop.Source.Auftragsnummer, StringComparison.OrdinalIgnoreCase));
        if (stop is null)
        {
            return;
        }

        var dialog = new RouteStopStayMinutesDialogWindow(stop.ServiceMinutes)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || !dialog.StayMinutes.HasValue)
        {
            return;
        }

        stop.ServiceMinutes = Math.Max(0, dialog.StayMinutes.Value);
        _scheduleService.ApplySchedule(sourceTour);
        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(_instanceId, sourceTour.Id.ToString(CultureInfo.InvariantCulture), sourceTour.Id.ToString(CultureInfo.InvariantCulture));
        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: sourceTour.Id);
        StatusText = $"Aufenthaltszeit für Auftrag {ExtractOrderIdFromStop(stop)} gesetzt: {stop.ServiceMinutes} min.";
    }

    public async Task RemoveSelectedTourStopAsync()
    {
        if (SelectedTourStop?.Source is null ||
            SelectedTourStop.IsCompanyStop)
        {
            return;
        }

        var sourceTour = _loadedTours.FirstOrDefault(x => x.Id == SelectedTourStop.SourceTourId);
        if (sourceTour is null)
        {
            return;
        }

        var movableStops = sourceTour.Stops.Where(s => !IsCompanyStop(s)).ToList();
        var removeIndex = movableStops.IndexOf(SelectedTourStop.Source);
        if (removeIndex < 0)
        {
            return;
        }

        var removedStop = movableStops[removeIndex];
        var removedOrderId = ExtractOrderIdFromStop(removedStop);
        movableStops.RemoveAt(removeIndex);
        RebuildTourStopsWithAnchors(sourceTour, movableStops);
        _scheduleService.ApplySchedule(sourceTour);

        var updatedOrderAssignment = false;
        if (!string.IsNullOrWhiteSpace(removedOrderId))
        {
            var orders = (await _orderRepository.GetAllAsync()).ToList();
            var order = orders.FirstOrDefault(o => string.Equals(o.Id, removedOrderId, StringComparison.OrdinalIgnoreCase));
            if (order is not null)
            {
                order.AssignedTourId = string.Empty;
                await _orderRepository.SaveAllAsync(orders);
                updatedOrderAssignment = true;
            }
        }

        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(_instanceId, sourceTour.Id.ToString(CultureInfo.InvariantCulture), sourceTour.Id.ToString(CultureInfo.InvariantCulture));
        if (updatedOrderAssignment)
        {
            _dataSyncService.PublishOrders(_instanceId);
        }

        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: sourceTour.Id);
        StatusText = $"Stopp {removedOrderId} wurde aus der Tour entfernt.";
    }

    public async Task EditSelectedTourStopOrderAsync()
    {
        if (SelectedTourStop?.Source is null ||
            SelectedTourStop.IsCompanyStop)
        {
            return;
        }

        var orderId = ExtractOrderIdFromStop(SelectedTourStop.Source);
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        var orders = (await _orderRepository.GetAllAsync()).ToList();
        var existing = orders.FirstOrDefault(o => string.Equals(o.Id, orderId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        var dialog = new ManualOrderDialogWindow(
            existing,
            deliveryTypes: DeliveryMethodExtensions.MapDeliveryTypeOptions,
            defaultOrderType: existing.Type)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var updated = dialog.CreatedOrder;
        updated.Type = existing.Type;
        updated.AssignedTourId = existing.AssignedTourId;
        updated.Location = await AddressGeocodingService.TryGeocodeOrderAsync(updated) ?? existing.Location;

        orders.RemoveAll(x => string.Equals(x.Id, existing.Id, StringComparison.OrdinalIgnoreCase));
        orders.RemoveAll(x => !string.Equals(x.Id, existing.Id, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        orders.Add(updated);

        await _orderRepository.SaveAllAsync(orders);
        _dataSyncService.PublishOrders(_instanceId);
        StatusText = $"Auftrag {updated.Id} wurde aktualisiert.";
    }

    private static bool ConfirmStopReassignment(TourStopOverviewItem sourceItem, TourRecord sourceTour, TourRecord targetTour)
    {
        var orderNumber = string.IsNullOrWhiteSpace(sourceItem.OrderNumber)
            ? (string.IsNullOrWhiteSpace(sourceItem.Name) ? "unbekannt" : sourceItem.Name)
            : sourceItem.OrderNumber;
        var sourceName = string.IsNullOrWhiteSpace(sourceTour.Name) ? $"Tour {sourceTour.Id}" : sourceTour.Name.Trim();
        var targetName = string.IsNullOrWhiteSpace(targetTour.Name) ? $"Tour {targetTour.Id}" : targetTour.Name.Trim();

        var message =
            $"Der Auftrag/Stopp \"{orderNumber}\" ist bereits in \"{sourceName}\" eingeplant.{Environment.NewLine}{Environment.NewLine}" +
            $"Soll er wirklich nach \"{targetName}\" umgeplant werden?";

        var result = MessageBox.Show(
            message,
            "Umplanung bestätigen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private async Task LoadReferenceDataAsync()
    {
        var employees = await _employeeRepository.LoadAsync();
        var vehicles = await _vehicleRepository.LoadAsync();
        _vehicleData = vehicles;

        _vehicleLabelsById.Clear();
        _trailerLabelsById.Clear();
        _employeeLabelsById.Clear();

        AvailableVehicles.Clear();
        AvailableVehicles.Add(new LookupItem { Id = string.Empty, Label = "(none)" });
        foreach (var vehicle in vehicles.Vehicles.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = $"{vehicle.Name} [{vehicle.LicensePlate}]";
            AvailableVehicles.Add(new LookupItem { Id = vehicle.Id, Label = label });
            _vehicleLabelsById[vehicle.Id] = label;
        }

        AvailableTrailers.Clear();
        AvailableTrailers.Add(new LookupItem { Id = string.Empty, Label = "(none)" });
        foreach (var trailer in vehicles.Trailers.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = $"{trailer.Name} [{trailer.LicensePlate}]";
            AvailableTrailers.Add(new LookupItem { Id = trailer.Id, Label = label });
            _trailerLabelsById[trailer.Id] = label;
        }

        AvailableEmployees.Clear();
        foreach (var employee in employees.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _employeeLabelsById[employee.Id] = employee.DisplayName;
            var item = new SelectableEmployeeItem
            {
                Id = employee.Id,
                Label = employee.DisplayName,
                IsSelected = false
            };

            item.PropertyChanged += (_, args) =>
            {
                if (_editorSyncInProgress || args.PropertyName != nameof(SelectableEmployeeItem.IsSelected))
                {
                    return;
                }

                EnforceEmployeeSelectionLimit(item);
                UpdateEditorConflictPreview();
            };
            AvailableEmployees.Add(item);
        }
    }

    private void EnforceEmployeeSelectionLimit(SelectableEmployeeItem changedItem)
    {
        if (!changedItem.IsSelected)
        {
            return;
        }

        var selected = AvailableEmployees.Where(x => x.IsSelected).ToList();
        if (selected.Count <= 2)
        {
            return;
        }

        var toDisable = selected.FirstOrDefault(x => x != changedItem);
        if (toDisable is not null)
        {
            toDisable.IsSelected = false;
        }
    }

    private async Task ToggleArchiveSelectedTourAsync()
    {
        if (SelectedTour is null)
        {
            return;
        }

        var target = _loadedTours.FirstOrDefault(x => x.Id == SelectedTour.TourId);
        if (target is null)
        {
            return;
        }

        var nextTourArchivedState = !target.IsArchived;
        var changedOrderCount = 0;

        var tourKey = target.Id.ToString(CultureInfo.InvariantCulture);
        var orders = (await _orderRepository.GetAllAsync()).ToList();
        var relatedOrders = orders
            .Where(x => string.Equals((x.AssignedTourId ?? string.Empty).Trim(), tourKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var affectedOrders = relatedOrders
            .Where(x => x.IsArchived != nextTourArchivedState)
            .ToList();

        if (affectedOrders.Count > 0)
        {
            var orderActionQuestion = nextTourArchivedState ? "archiviert" : "reaktiviert";
            var confirmation = MessageBox.Show(
                $"Sollen die {affectedOrders.Count} zugehörigen Aufträge ebenfalls {orderActionQuestion} werden?",
                "Aufträge mitführen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation == MessageBoxResult.Yes)
            {
                foreach (var order in affectedOrders)
                {
                    order.IsArchived = nextTourArchivedState;
                }

                await _orderRepository.SaveAllAsync(orders);
                _dataSyncService.PublishOrders(_instanceId);
                changedOrderCount = affectedOrders.Count;
            }
        }

        target.IsArchived = nextTourArchivedState;
        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(_instanceId, target.Id.ToString(CultureInfo.InvariantCulture), target.Id.ToString(CultureInfo.InvariantCulture));
        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: target.Id);
        OnPropertyChanged(nameof(ToggleArchiveTourButtonText));

        var label = string.IsNullOrWhiteSpace(target.Name)
            ? target.Id.ToString(CultureInfo.InvariantCulture)
            : target.Name.Trim();
        var action = target.IsArchived ? "archiviert" : "reaktiviert";
        var orderInfo = changedOrderCount > 0
            ? $" {changedOrderCount} Auftrag/Aufträge wurden ebenfalls {action}."
            : string.Empty;
        ToastNotificationService.ShowInfo($"Tour {label} wurde {action}.{orderInfo}");
    }

    private async Task DeleteSelectedTourAsync()
    {
        if (SelectedTour is null)
        {
            return;
        }

        var target = _loadedTours.FirstOrDefault(x => x.Id == SelectedTour.TourId);
        if (target is null)
        {
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

        _loadedTours.Remove(target);
        await _tourRepository.SaveAsync(_loadedTours);
        await ClearAssignedTourReferencesAsync(target.Id);
        _dataSyncService.PublishTours(_instanceId, target.Id.ToString(CultureInfo.InvariantCulture), null);
        _dataSyncService.PublishOrders(_instanceId);
        RebuildTourRowsWithCurrentFilter();
        var deletedTourLabel = string.IsNullOrWhiteSpace(target.Name) ? target.Id.ToString(CultureInfo.InvariantCulture) : target.Name.Trim();
        ToastNotificationService.ShowInfo($"Tour {deletedTourLabel} wurde gelöscht.");
    }

    private async Task OpenSelectedTourOnMapAsync()
    {
        if (SelectedTour is null || _openTourOnMapAsync is null)
        {
            return;
        }

        await _openTourOnMapAsync(SelectedTour.TourId);
    }

    private async Task OpenSelectedTourOnMapForEditAsync()
    {
        if (SelectedTour?.Source is null)
        {
            return;
        }

        var tour = SelectedTour.Source;
        var (editHour, editMinute) = ParseStartTimeParts(tour.StartTime);
        var (employees, vehicles, trailers) = await LoadTourDialogOptionsAsync(tour.Date);

        var dialog = new CreateTourDialogWindow(
            routeDate: string.IsNullOrWhiteSpace(tour.Date) ? DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : tour.Date.Trim(),
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
            selectedEmployeeIds: tour.EmployeeIds,
            showOpenOnMapButton: true)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            Title = "Tour bearbeiten"
        };

        if (dialog.ShowDialog() != true)
        {
            if (dialog.OpenOnMapRequested && _openTourOnMapAsync is not null)
            {
                await _openTourOnMapAsync(tour.Id);
            }
            return;
        }

        if (dialog.Result is null)
        {
            return;
        }

        var result = dialog.Result;
        var originalName = tour.Name;
        var originalDate = tour.Date;
        var originalStartTime = tour.StartTime;
        var originalVehicleId = tour.VehicleId;
        var originalTrailerId = tour.TrailerId;
        var originalSecondaryVehicleId = tour.SecondaryVehicleId;
        var originalSecondaryTrailerId = tour.SecondaryTrailerId;
        var originalEmployeeIds = tour.EmployeeIds.ToList();

        tour.Name = result.RouteName;
        tour.Date = result.RouteDate;
        tour.StartTime = result.StartTime;
        tour.VehicleId = string.IsNullOrWhiteSpace(result.VehicleId) ? null : result.VehicleId.Trim();
        tour.TrailerId = string.IsNullOrWhiteSpace(result.TrailerId) ? null : result.TrailerId.Trim();
        tour.SecondaryVehicleId = string.IsNullOrWhiteSpace(result.SecondaryVehicleId) ? null : result.SecondaryVehicleId.Trim();
        tour.SecondaryTrailerId = string.IsNullOrWhiteSpace(result.SecondaryTrailerId) ? null : result.SecondaryTrailerId.Trim();
        tour.EmployeeIds = (result.EmployeeIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var availabilityError = await BuildAvailabilityErrorAsync(
            tour.Date,
            tour.VehicleId,
            tour.TrailerId,
            tour.SecondaryVehicleId,
            tour.SecondaryTrailerId,
            tour.EmployeeIds);
        if (!string.IsNullOrWhiteSpace(availabilityError))
        {
            MessageBox.Show(availabilityError, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            tour.Name = originalName;
            tour.Date = originalDate;
            tour.StartTime = originalStartTime;
            tour.VehicleId = originalVehicleId;
            tour.TrailerId = originalTrailerId;
            tour.SecondaryVehicleId = originalSecondaryVehicleId;
            tour.SecondaryTrailerId = originalSecondaryTrailerId;
            tour.EmployeeIds = originalEmployeeIds;
            return;
        }

        if (!ConfirmPlanningWarnings(_loadedTours, tour.Id))
        {
            tour.Name = originalName;
            tour.Date = originalDate;
            tour.StartTime = originalStartTime;
            tour.VehicleId = originalVehicleId;
            tour.TrailerId = originalTrailerId;
            tour.SecondaryVehicleId = originalSecondaryVehicleId;
            tour.SecondaryTrailerId = originalSecondaryTrailerId;
            tour.EmployeeIds = originalEmployeeIds;
            return;
        }

        _scheduleService.ApplySchedule(tour);
        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(_instanceId, tour.Id.ToString(CultureInfo.InvariantCulture), tour.Id.ToString(CultureInfo.InvariantCulture));
        await RefreshAsync();
        await FocusTourAsync(tour.Id);
        StatusText = $"Tour {tour.Id} wurde aktualisiert.";
    }

    private async Task<(List<TourEmployeeOption> Employees, List<TourLookupOption> Vehicles, List<TourLookupOption> Trailers)> LoadTourDialogOptionsAsync(string? routeDate)
    {
        var employeesTask = _employeeRepository.LoadAsync();
        var vehiclesTask = _vehicleRepository.LoadAsync();
        await Task.WhenAll(employeesTask, vehiclesTask);
        var selectedDate = ResourceAvailabilityService.ParseDate(routeDate);

        var employees = (await employeesTask)
            .Where(x => x.Active &&
                        (!selectedDate.HasValue || !ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, selectedDate.Value)))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourEmployeeOption(x.Id, x.DisplayName))
            .ToList();

        var vehicleData = await vehiclesTask;
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

    private static (string Hour, string Minute) ParseStartTimeParts(string? startTime)
    {
        var value = (startTime ?? string.Empty).Trim();
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return (parsed.Hour.ToString("00", CultureInfo.InvariantCulture), parsed.Minute.ToString("00", CultureInfo.InvariantCulture));
        }

        return ("08", "00");
    }

    private async Task ClearAssignedTourReferencesAsync(int tourId)
    {
        var tourKey = tourId.ToString(CultureInfo.InvariantCulture);
        var orders = (await _orderRepository.GetAllAsync()).ToList();
        var changed = false;

        foreach (var order in orders.Where(x => string.Equals(x.AssignedTourId, tourKey, StringComparison.OrdinalIgnoreCase)))
        {
            order.AssignedTourId = string.Empty;
            changed = true;
        }

        if (changed)
        {
            await _orderRepository.SaveAllAsync(orders);
        }
    }

    private void RebuildTourRowsWithCurrentFilter(int? keepSelectionTourId = null)
    {
        var from = FromDate ?? ParseDate(FromDateText);
        var to = ToDate ?? ParseDate(ToDateText);
        var filtered = ApplyDateFilterToTours(_loadedTours, from, to);
        RebuildTourRows(filtered, keepSelectionTourId);
        FilterInfoText = BuildDateFilterInfoText(from, to, Tours.Count, ShowArchivedTours);
    }

    private IEnumerable<TourRecord> ApplyDateFilterToTours(IEnumerable<TourRecord> tours, DateTime? from, DateTime? to)
    {
        var modeFiltered = tours.Where(t => t.IsArchived == ShowArchivedTours);
        if (from is null && to is null)
        {
            return modeFiltered;
        }

        var fromValue = from ?? DateTime.MinValue.Date;
        var toValue = to ?? DateTime.MaxValue.Date;
        if (fromValue > toValue)
        {
            (fromValue, toValue) = (toValue, fromValue);
        }

        return modeFiltered.Where(t =>
        {
            var tourDate = ParseDate(t.Date);
            if (tourDate is null)
            {
                return false;
            }

            return tourDate.Value.Date >= fromValue && tourDate.Value.Date <= toValue;
        });
    }

    private static string BuildDateFilterInfoText(DateTime? from, DateTime? to, int matches, bool showArchived)
    {
        var modeLabel = showArchived ? "Archiviert" : "Aktiv";
        if (from is null && to is null)
        {
            return $"{modeLabel} | Alle Touren | Treffer: {matches}";
        }

        var fromText = from?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "offen";
        var toText = to?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "offen";
        return $"{modeLabel} | {fromText} - {toText} | Treffer: {matches}";
    }

    private static DateTime? ParseDate(string? value)
    {
        if (DateTime.TryParseExact((value ?? string.Empty).Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    private void RebuildFromCalendarDays()
    {
        FromMonthDisplayText = _fromCalendarMonth.ToString("MMMM yyyy", UiCulture);
        RebuildCalendarDays(FromCalendarDays, _fromCalendarMonth, FromDate);
    }

    private void RebuildToCalendarDays()
    {
        ToMonthDisplayText = _toCalendarMonth.ToString("MMMM yyyy", UiCulture);
        RebuildCalendarDays(ToCalendarDays, _toCalendarMonth, ToDate);
    }

    private static void RebuildCalendarDays(
        ObservableCollection<DateFilterCalendarDayItem> target,
        DateTime monthStart,
        DateTime? selectedDate)
    {
        target.Clear();
        var firstOfMonth = new DateTime(monthStart.Year, monthStart.Month, 1);
        var offset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var start = firstOfMonth.AddDays(-offset);

        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i).Date;
            target.Add(new DateFilterCalendarDayItem
            {
                Date = date,
                DayText = date.Day.ToString(CultureInfo.InvariantCulture),
                IsCurrentMonth = date.Month == monthStart.Month && date.Year == monthStart.Year,
                IsToday = date == DateTime.Today,
                IsSelected = selectedDate.HasValue && date == selectedDate.Value.Date
            });
        }
    }

    private void RebuildTourRows(IEnumerable<TourRecord> tours, int? keepSelectionTourId)
    {
        var tourList = tours.ToList();
        var conflicts = _conflictService.FindAssignmentConflicts(tourList)
            .GroupBy(c => c.TourIdA)
            .ToDictionary(g => g.Key, g => g.Count());

        Tours.Clear();
        foreach (var tour in tourList.OrderBy(t => t.Date).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var schedule = _scheduleService.BuildSchedule(tour);
            var employeeText = BuildEmployeeFirstNameText(tour.EmployeeIds ?? []);
            var totalWeight = tour.Stops
                .Where(s => !IsCompanyStop(s))
                .Sum(s => ParseWeightKg(s.Gewicht));
            Tours.Add(new TourOverviewItem
            {
                TourId = tour.Id,
                Name = tour.Name,
                Date = tour.Date,
                Start = schedule.Start.ToString("HH:mm"),
                End = schedule.End.ToString("HH:mm"),
                VehicleId = BuildVehicleOverviewText(tour),
                TrailerId = BuildTrailerOverviewText(tour),
                Employees = employeeText,
                StopCount = tour.Stops.Count(s => !IsCompanyStop(s)),
                TotalWeightKg = totalWeight,
                StopConflicts = schedule.Stops.Count(s => s.HasConflict),
                AssignmentConflicts = conflicts.TryGetValue(tour.Id, out var count) ? count : 0,
                IsArchived = tour.IsArchived,
                Source = tour
            });
        }

        SelectedTour = Tours.FirstOrDefault(t => keepSelectionTourId.HasValue && t.TourId == keepSelectionTourId.Value) ?? Tours.FirstOrDefault();
        var modeLabel = ShowArchivedTours ? "Archiviert" : "Aktiv";
        StatusText = $"Tours ({modeLabel}): {Tours.Count} | Stop conflicts: {Tours.Sum(t => t.StopConflicts)} | Assignment conflicts: {Tours.Sum(t => t.AssignmentConflicts)}";
    }

    private string ResolveVehicleLabel(string? vehicleId)
    {
        var id = (vehicleId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        return _vehicleLabelsById.TryGetValue(id, out var label) ? label : id;
    }

    private bool TryBuildReorderedStops(
        TourRecord tour,
        TourStopRecord sourceStop,
        TourStopRecord? targetStop,
        bool insertAfterTarget,
        out List<TourStopRecord> reorderedStops)
    {
        reorderedStops = new List<TourStopRecord>();
        var movableStops = tour.Stops.Where(s => !IsCompanyStop(s)).ToList();
        if (movableStops.Count < 2)
        {
            return false;
        }

        var sourceIndex = movableStops.IndexOf(sourceStop);
        if (sourceIndex < 0)
        {
            return false;
        }

        var targetIndex = targetStop is null || IsCompanyStop(targetStop)
            ? movableStops.Count
            : movableStops.IndexOf(targetStop);
        if (targetIndex < 0)
        {
            return false;
        }

        var moved = movableStops[sourceIndex];
        movableStops.RemoveAt(sourceIndex);

        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        if (insertAfterTarget && targetStop is not null && !IsCompanyStop(targetStop))
        {
            targetIndex++;
        }

        targetIndex = Math.Clamp(targetIndex, 0, movableStops.Count);
        if (targetIndex == sourceIndex)
        {
            return false;
        }

        movableStops.Insert(targetIndex, moved);

        var previewTour = CloneTour(tour);
        RebuildTourStopsWithAnchors(previewTour, movableStops);
        reorderedStops = previewTour.Stops;
        return true;
    }

    private TourOrderMetrics BuildTourOrderMetrics(TourRecord tour, List<TourStopRecord>? orderedStopsOverride = null)
    {
        var scheduleSource = orderedStopsOverride is null
            ? tour
            : new TourRecord
            {
                Date = tour.Date,
                StartTime = tour.StartTime,
                TravelTimeCache = new Dictionary<string, int>(tour.TravelTimeCache),
                Stops = orderedStopsOverride
            };

        var schedule = _scheduleService.BuildSchedule(scheduleSource);
        var totalDurationMinutes = Math.Max(0, (int)Math.Round((schedule.End - schedule.Start).TotalMinutes));
        var routeDistanceKm = CalculateRouteDistanceKm((orderedStopsOverride ?? tour.Stops)
            .OrderBy(s => s.Order)
            .ToList());

        return new TourOrderMetrics(
            EndTime: schedule.End.ToString("HH:mm"),
            EndMinutes: (int)Math.Round(schedule.End.TimeOfDay.TotalMinutes),
            DurationMinutes: totalDurationMinutes,
            DistanceKm: routeDistanceKm);
    }

    private double CalculateRouteDistanceKm(IReadOnlyList<TourStopRecord> orderedStops)
    {
        var points = orderedStops
            .Select(stop => new
            {
                Lat = stop.Lat,
                Lon = stop.Lon ?? stop.Lng
            })
            .Where(p => p.Lat.HasValue && p.Lon.HasValue)
            .Select(p => new GeoPoint(p.Lat!.Value, p.Lon!.Value))
            .ToList();

        return _routeOptimizationService.ComputeTotalDistanceKm(points, p => p.Latitude, p => p.Longitude);
    }

    private static string FormatSignedMinutes(int deltaMinutes)
    {
        if (deltaMinutes == 0)
        {
            return "±0 min";
        }

        var sign = deltaMinutes > 0 ? "+" : "-";
        return $"{sign}{Math.Abs(deltaMinutes)} min";
    }

    private static string FormatSignedDistance(double deltaKm)
    {
        if (Math.Abs(deltaKm) < 0.05d)
        {
            return "±0.0 km";
        }

        var sign = deltaKm > 0 ? "+" : "-";
        return $"{sign}{Math.Abs(deltaKm):0.0} km";
    }

    private static string FormatDuration(int totalMinutes)
    {
        var safeMinutes = Math.Max(0, totalMinutes);
        var hours = safeMinutes / 60;
        var minutes = safeMinutes % 60;
        return $"{hours}h {minutes:00}m";
    }

    private static void RebuildTourStopsWithAnchors(TourRecord tour, List<TourStopRecord> movableStops)
    {
        var start = tour.Stops.FirstOrDefault(s => string.Equals(s.Id, TourStopIdentity.CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
        var end = tour.Stops.FirstOrDefault(s => string.Equals(s.Id, TourStopIdentity.CompanyEndStopId, StringComparison.OrdinalIgnoreCase));
        if (start is null || end is null)
        {
            // Fallback for unexpected legacy data without explicit company anchors.
            tour.Stops = movableStops;
            for (var i = 0; i < tour.Stops.Count; i++)
            {
                tour.Stops[i].Order = i + 1;
            }

            return;
        }

        tour.Stops = [start, .. movableStops, end];
        for (var i = 0; i < tour.Stops.Count; i++)
        {
            tour.Stops[i].Order = i + 1;
        }
    }

    private static string ExtractOrderIdFromStop(TourStopRecord stop)
    {
        var orderNumber = (stop.Auftragsnummer ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            return orderNumber;
        }

        var id = (stop.Id ?? string.Empty).Trim();
        if (id.StartsWith("auftrag:", StringComparison.OrdinalIgnoreCase))
        {
            return id["auftrag:".Length..];
        }

        return id;
    }

    private string ResolveVehicleShortLabel(string? vehicleId)
    {
        var label = ResolveVehicleLabel(vehicleId);
        if (string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        // Keep the table compact: hide license plate suffix like " [TG 123456]".
        var bracketIndex = label.LastIndexOf(" [", StringComparison.Ordinal);
        if (bracketIndex > 0 && label.EndsWith(']'))
        {
            return label[..bracketIndex].Trim();
        }

        return label;
    }

    private string BuildVehicleOverviewText(TourRecord tour)
    {
        var lines = BuildVehicleTrailerOverviewLines(
            tour.VehicleId,
            tour.TrailerId,
            tour.SecondaryVehicleId,
            tour.SecondaryTrailerId,
            useShortVehicleLabel: true);

        return lines.Count == 0 ? "-" : string.Join(Environment.NewLine, lines);
    }

    private string BuildTrailerOverviewText(TourRecord tour)
    {
        var lines = BuildVehicleAssignments(
            tour.VehicleId,
            tour.TrailerId,
            tour.SecondaryVehicleId,
            tour.SecondaryTrailerId)
            .Select(x => ResolveTrailerLabel(x.TrailerId))
            .Select(x => string.IsNullOrWhiteSpace(x) ? "-" : x)
            .ToList();

        return lines.Count == 0 ? "-" : string.Join(Environment.NewLine, lines);
    }

    private List<string> BuildVehicleTrailerOverviewLines(
        string? vehicleId,
        string? trailerId,
        string? secondaryVehicleId,
        string? secondaryTrailerId,
        bool useShortVehicleLabel)
    {
        return BuildVehicleAssignments(vehicleId, trailerId, secondaryVehicleId, secondaryTrailerId)
            .Select(x =>
            {
                var vehicleLabel = useShortVehicleLabel
                    ? ResolveVehicleShortLabel(x.VehicleId)
                    : ResolveVehicleLabel(x.VehicleId);
                var trailerLabel = ResolveTrailerLabel(x.TrailerId);
                return $"{(string.IsNullOrWhiteSpace(vehicleLabel) ? "-" : vehicleLabel)} & {(string.IsNullOrWhiteSpace(trailerLabel) ? "-" : trailerLabel)}";
            })
            .ToList();
    }

    private string ResolveTrailerLabel(string? trailerId)
    {
        var id = (trailerId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        return _trailerLabelsById.TryGetValue(id, out var label) ? label : id;
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

    private string BuildEmployeeFirstNameText(IEnumerable<string> employeeIds)
    {
        var firstNames = employeeIds
            .Select(ResolveEmployeeLabel)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(ExtractFirstName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return string.Join(", ", firstNames);
    }

    private static string ExtractFirstName(string label)
    {
        var parts = (label ?? string.Empty)
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : parts[0];
    }

    private void LoadSelectedTourStops()
    {
        SelectedTourStops.Clear();
        SelectedTourStop = null;
        if (SelectedTour?.Source is null)
        {
            return;
        }

        var orderedStops = SelectedTour.Source.Stops
            .OrderBy(GetStopDisplayOrderGroup)
            .ThenBy(s => s.Order)
            .ToList();

        for (var index = 0; index < orderedStops.Count; index++)
        {
            var stop = orderedStops[index];
            var isCompanyStop = IsCompanyStop(stop);
            var isRouteStart = index == 0;
            var isRouteEnd = index == orderedStops.Count - 1;
            var arrival = stop.PlannedArrival ?? string.Empty;
            var departure = stop.PlannedDeparture ?? string.Empty;

            SelectedTourStops.Add(new TourStopOverviewItem
            {
                SourceTourId = SelectedTour.TourId,
                Source = stop,
                IsCompanyStop = isCompanyStop,
                IsRouteStart = isRouteStart,
                IsRouteEnd = isRouteEnd,
                Order = isCompanyStop ? string.Empty : stop.Order.ToString(CultureInfo.InvariantCulture),
                OrderNumber = isCompanyStop ? string.Empty : stop.Auftragsnummer,
                Name = isCompanyStop ? NormalizeCompanyStopName(stop.Name) : stop.Name,
                Address = isCompanyStop ? string.Empty : stop.Address,
                Window = isCompanyStop ? string.Empty : $"{stop.TimeWindowStart} - {stop.TimeWindowEnd}".Trim(' ', '-'),
                Arrival = arrival,
                Departure = departure,
                Weight = isCompanyStop ? string.Empty : $"{ParseWeightKg(stop.Gewicht)} kg",
                Conflict = isCompanyStop ? string.Empty : (stop.ScheduleConflict ? (string.IsNullOrWhiteSpace(stop.ScheduleConflictText) ? "Yes" : stop.ScheduleConflictText) : string.Empty)
            });
        }
    }

    private static int GetStopDisplayOrderGroup(TourStopRecord stop)
    {
        if (string.Equals(stop.Id, TourStopIdentity.CompanyStartStopId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stop.Auftragsnummer, TourStopIdentity.CompanyStartOrderNumber, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(stop.Id, TourStopIdentity.CompanyEndStopId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stop.Auftragsnummer, TourStopIdentity.CompanyEndOrderNumber, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private void UpdateSelectedTourSummary()
    {
        if (SelectedTour?.Source is null)
        {
            SelectedTourWeightText = "Totalgewicht: 0 kg";
            SelectedTourLoadSummaryText = string.Empty;
            SelectedTourVehicleText = "Fahrzeug & Anhänger: -";
            return;
        }

        var totalWeight = SelectedTour.Source.Stops
            .Where(s => !IsCompanyStop(s))
            .Sum(s => ParseWeightKg(s.Gewicht));
        SelectedTourWeightText = $"Totalgewicht: {totalWeight} kg";

        var assignments = BuildVehicleAssignments(
            SelectedTour.Source.VehicleId,
            SelectedTour.Source.TrailerId,
            SelectedTour.Source.SecondaryVehicleId,
            SelectedTour.Source.SecondaryTrailerId);

        var vehicleTrailerLines = BuildVehicleTrailerOverviewLines(
            SelectedTour.Source.VehicleId,
            SelectedTour.Source.TrailerId,
            SelectedTour.Source.SecondaryVehicleId,
            SelectedTour.Source.SecondaryTrailerId,
            useShortVehicleLabel: false);

        var summaryLines = new List<string> { "Fahrzeug & Anhänger:" };
        summaryLines.AddRange(vehicleTrailerLines.Count == 0 ? ["-"] : vehicleTrailerLines);

        var loadSummaryLines = new List<string>();
        for (var i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            var display = VehicleCombinationDisplayResolver.Resolve(_vehicleData, assignment.VehicleId, assignment.TrailerId);
            var prefix = assignments.Count > 1 ? $"Fahrzeug {i + 1}: " : string.Empty;
            if (display.HasVehiclePayload)
            {
                loadSummaryLines.Add($"{prefix}Ladegewicht: {display.VehiclePayloadKg} kg");
            }

            if (display.TrailerLoadKg.HasValue)
            {
                loadSummaryLines.Add($"{prefix}Anhängelast: {display.TrailerLoadKg} kg");
            }
        }

        SelectedTourLoadSummaryText = string.Join(Environment.NewLine, loadSummaryLines);
        SelectedTourVehicleText = string.Join(Environment.NewLine, summaryLines);
    }

    private static int ParseWeightKg(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Max(0, value);
        }

        return 0;
    }

    private void SyncEditorFromSelection()
    {
        _editorSyncInProgress = true;
        try
        {
            if (SelectedTour?.Source is null)
            {
                EditorDate = string.Empty;
                EditorStartTime = "08:00";
                SelectedVehicle = AvailableVehicles.FirstOrDefault();
                SelectedTrailer = AvailableTrailers.FirstOrDefault();
                foreach (var employee in AvailableEmployees)
                {
                    employee.IsSelected = false;
                }

                EditorConflictText = string.Empty;
                return;
            }

            var source = SelectedTour.Source;
            EditorDate = source.Date;
            EditorStartTime = source.StartTime;
            SelectedVehicle = AvailableVehicles.FirstOrDefault(v => v.Id == (source.VehicleId ?? string.Empty)) ?? AvailableVehicles.FirstOrDefault();
            SelectedTrailer = AvailableTrailers.FirstOrDefault(v => v.Id == (source.TrailerId ?? string.Empty)) ?? AvailableTrailers.FirstOrDefault();

            var selectedEmployees = new HashSet<string>(source.EmployeeIds ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var employee in AvailableEmployees)
            {
                employee.IsSelected = selectedEmployees.Contains(employee.Id);
            }
        }
        finally
        {
            _editorSyncInProgress = false;
        }

        UpdateEditorConflictPreview();
    }

    private void UpdateEditorConflictPreview()
    {
        if (_editorSyncInProgress || SelectedTour?.Source is null)
        {
            return;
        }

        var selectedTourId = SelectedTour.TourId;
        var previewTours = _loadedTours
            .Select(CloneTour)
            .ToList();

        var preview = previewTours.FirstOrDefault(t => t.Id == selectedTourId);
        if (preview is null)
        {
            EditorConflictText = string.Empty;
            return;
        }

        preview.Date = (EditorDate ?? string.Empty).Trim();
        preview.StartTime = (EditorStartTime ?? string.Empty).Trim();
        preview.VehicleId = SelectedVehicle?.Id;
        preview.TrailerId = SelectedTrailer?.Id;
        preview.EmployeeIds = AvailableEmployees.Where(e => e.IsSelected).Select(e => e.Id).Take(2).ToList();

        var warnings = BuildPlanningWarnings(previewTours, selectedTourId);
        if (warnings.Count == 0)
        {
            EditorConflictText = "Keine Konflikte in aktueller Auswahl.";
            return;
        }

        var previewLines = warnings
            .Take(3)
            .Select(x => $"{x.Title}: {x.Message}")
            .ToList();

        var remaining = warnings.Count - previewLines.Count;
        if (remaining > 0)
        {
            previewLines.Add($"+{remaining} weitere Warnung(en)");
        }

        EditorConflictText = string.Join(" | ", previewLines);
    }

    private static TourRecord CloneTour(TourRecord source)
    {
        return new TourRecord
        {
            Id = source.Id,
            Date = source.Date,
            Name = source.Name,
            StartTime = source.StartTime,
            RouteMode = source.RouteMode,
            VehicleId = source.VehicleId,
            TrailerId = source.TrailerId,
            SecondaryVehicleId = source.SecondaryVehicleId,
            SecondaryTrailerId = source.SecondaryTrailerId,
            IsArchived = source.IsArchived,
            EmployeeIds = source.EmployeeIds.ToList(),
            TravelTimeCache = source.TravelTimeCache.ToDictionary(kv => kv.Key, kv => kv.Value),
            Stops = source.Stops.Select(stop => new TourStopRecord
            {
                Id = stop.Id,
                Name = stop.Name,
                Address = stop.Address,
                Auftragsnummer = stop.Auftragsnummer,
                Lat = stop.Lat,
                Lon = stop.Lon,
                Lng = stop.Lng,
                Order = stop.Order,
                TimeWindowStart = stop.TimeWindowStart,
                TimeWindowEnd = stop.TimeWindowEnd,
                ServiceMinutes = stop.ServiceMinutes,
                PlannedArrival = stop.PlannedArrival,
                PlannedDeparture = stop.PlannedDeparture,
                WaitMinutes = stop.WaitMinutes,
                ScheduleConflict = stop.ScheduleConflict,
                ScheduleConflictText = stop.ScheduleConflictText,
                Gewicht = stop.Gewicht,
                EmployeeInfoText = stop.EmployeeInfoText
            }).ToList()
        };
    }

    private void RaiseCommandStates()
    {
        RaiseCanExecuteChangedIfSupported(RefreshCommand);
        RaiseCanExecuteChangedIfSupported(RecalculateCommand);
        RaiseCanExecuteChangedIfSupported(SaveAssignmentCommand);
        RaiseCanExecuteChangedIfSupported(DeleteTourCommand);
        RaiseCanExecuteChangedIfSupported(ToggleArchiveTourCommand);
        RaiseCanExecuteChangedIfSupported(OpenTourOnMapCommand);
        RaiseCanExecuteChangedIfSupported(EditTourOnMapCommand);
        RaiseCanExecuteChangedIfSupported(EditSelectedTourStopStayMinutesCommand);
        RaiseCanExecuteChangedIfSupported(RemoveSelectedTourStopCommand);
        RaiseCanExecuteChangedIfSupported(EditSelectedTourStopOrderCommand);
    }

    private void ToggleDatePopup(bool isFrom)
    {
        IsFromDatePopupOpen = isFrom ? !IsFromDatePopupOpen : false;
        IsToDatePopupOpen = isFrom ? false : !IsToDatePopupOpen;

        if (isFrom && IsFromDatePopupOpen)
        {
            if (FromDate.HasValue)
            {
                _fromCalendarMonth = new DateTime(FromDate.Value.Year, FromDate.Value.Month, 1);
            }

            RebuildFromCalendarDays();
            return;
        }

        if (!isFrom && IsToDatePopupOpen)
        {
            if (ToDate.HasValue)
            {
                _toCalendarMonth = new DateTime(ToDate.Value.Year, ToDate.Value.Month, 1);
            }

            RebuildToCalendarDays();
        }
    }

    private void ShiftCalendarMonth(bool isFrom, int monthDelta)
    {
        if (isFrom)
        {
            _fromCalendarMonth = _fromCalendarMonth.AddMonths(monthDelta);
            RebuildFromCalendarDays();
            return;
        }

        _toCalendarMonth = _toCalendarMonth.AddMonths(monthDelta);
        RebuildToCalendarDays();
    }

    private void SetDateFilter(DateTime? fromDate, DateTime? toDate)
    {
        FromDate = fromDate;
        ToDate = toDate;
        ApplyDateFilter();
    }

    private static TourAssignmentSnapshot CaptureAssignment(TourRecord target)
    {
        return new TourAssignmentSnapshot(
            target.Date,
            target.StartTime,
            target.VehicleId,
            target.TrailerId,
            target.SecondaryVehicleId,
            target.SecondaryTrailerId,
            target.EmployeeIds.ToList());
    }

    private static void RestoreAssignment(TourRecord target, TourAssignmentSnapshot snapshot)
    {
        target.Date = snapshot.Date;
        target.StartTime = snapshot.StartTime;
        target.VehicleId = snapshot.VehicleId;
        target.TrailerId = snapshot.TrailerId;
        target.SecondaryVehicleId = snapshot.SecondaryVehicleId;
        target.SecondaryTrailerId = snapshot.SecondaryTrailerId;
        target.EmployeeIds = snapshot.EmployeeIds;
    }

    private static void RaiseCanExecuteChangedIfSupported(ICommand command)
    {
        if (command is AsyncCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
    }

    private readonly record struct TourAssignmentSnapshot(
        string Date,
        string StartTime,
        string? VehicleId,
        string? TrailerId,
        string? SecondaryVehicleId,
        string? SecondaryTrailerId,
        List<string> EmployeeIds);

    private bool ConfirmPlanningWarnings(IEnumerable<TourRecord> tours, int targetTourId)
    {
        var warnings = BuildPlanningWarnings(tours, targetTourId);
        if (warnings.Count == 0)
        {
            return true;
        }

        var grouped = warnings
            .GroupBy(x => x.Title)
            .ToList();

        var lines = new List<string>
        {
            "Folgende Planungswarnungen wurden erkannt:",
            string.Empty
        };

        foreach (var group in grouped)
        {
            lines.Add($"{group.Key}:");
            foreach (var item in group.Take(6))
            {
                lines.Add($"- {item.Message}");
                if (!string.IsNullOrWhiteSpace(item.Suggestion))
                {
                    lines.Add($"  Vorschlag: {item.Suggestion}");
                }
            }

            var remaining = group.Count() - 6;
            if (remaining > 0)
            {
                lines.Add($"- ... und {remaining} weitere");
            }

            lines.Add(string.Empty);
        }

        lines.Add("Trotzdem speichern?");
        return MessageBox.Show(
                   string.Join(Environment.NewLine, lines),
                   "Planungswarnung",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private IReadOnlyList<TourPlanningWarningItem> BuildPlanningWarnings(IEnumerable<TourRecord> tours, int targetTourId)
    {
        var allTours = (tours ?? [])
            .Where(x => x is not null)
            .ToList();
        var targetTour = allTours.FirstOrDefault(x => x.Id == targetTourId);
        if (targetTour is null)
        {
            return Array.Empty<TourPlanningWarningItem>();
        }

        var warnings = new List<TourPlanningWarningItem>();

        var assignmentConflicts = _conflictService.FindSameDayAssignmentConflicts(allTours)
            .Where(c => c.TourIdA == targetTourId || c.TourIdB == targetTourId)
            .Select(BuildAssignmentConflictDisplayText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var conflictText in assignmentConflicts)
        {
            warnings.Add(new TourPlanningWarningItem(
                Title: "Doppelbelegung",
                Message: conflictText,
                Suggestion: "Startzeit oder Datum verschieben oder andere Ressourcen zuweisen."));
        }

        var schedulePreview = _scheduleService.BuildSchedule(targetTour);
        var orderedStops = (targetTour.Stops ?? [])
            .OrderBy(s => s.Order > 0 ? s.Order : int.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < schedulePreview.Stops.Count && i < orderedStops.Count; i++)
        {
            var scheduleEntry = schedulePreview.Stops[i];
            if (!scheduleEntry.HasConflict)
            {
                continue;
            }

            var stop = orderedStops[i];
            var stopLabel = string.IsNullOrWhiteSpace(stop.Auftragsnummer)
                ? stop.Name
                : $"{stop.Name} ({stop.Auftragsnummer})";

            warnings.Add(new TourPlanningWarningItem(
                Title: "Zeitfenster",
                Message: $"{stopLabel}: {scheduleEntry.ConflictText}",
                Suggestion: "Zeitfenster anpassen, Startzeit vorziehen oder Aufenthaltszeit reduzieren."));
        }

        var assignments = BuildVehicleAssignments(
            targetTour.VehicleId,
            targetTour.TrailerId,
            targetTour.SecondaryVehicleId,
            targetTour.SecondaryTrailerId);

        var totalWeight = CalculateTourWeightKg(targetTour);
        var capacity = TourCapacityWarningService.EvaluateFleet(_vehicleData, assignments, totalWeight);
        if (capacity.IsOverCapacity)
        {
            var overloadKg = capacity.AllowedWeightKg.HasValue
                ? Math.Max(0, totalWeight - capacity.AllowedWeightKg.Value)
                : 0;
            warnings.Add(new TourPlanningWarningItem(
                Title: "Ueberladung",
                Message: capacity.AllowedWeightKg.HasValue
                    ? $"Totalgewicht {totalWeight} kg, zulaessig {capacity.AllowedWeightKg.Value} kg, Ueberladung {overloadKg} kg."
                    : $"Totalgewicht {totalWeight} kg ist hoeher als die bekannte Kapazitaet.",
                Suggestion: "Tour aufteilen oder Fahrzeug/Anhaenger mit hoeherer Kapazitaet waehlen."));
        }

        foreach (var assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.TrailerId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(assignment.VehicleId))
            {
                warnings.Add(new TourPlanningWarningItem(
                    Title: "Fahrzeugklasse",
                    Message: $"Anhaenger {ResolveTrailerLabel(assignment.TrailerId)} ist ohne Zugfahrzeug zugewiesen.",
                    Suggestion: "Passendes Zugfahrzeug zuweisen oder Anhaenger entfernen."));
                continue;
            }

            var hasActiveCombination = _vehicleData.VehicleCombinations.Any(x =>
                x.Active &&
                string.Equals(x.VehicleId, assignment.VehicleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TrailerId, assignment.TrailerId, StringComparison.OrdinalIgnoreCase));

            if (!hasActiveCombination)
            {
                warnings.Add(new TourPlanningWarningItem(
                    Title: "Fahrzeugklasse",
                    Message: $"Keine aktive Kombination fuer {ResolveVehicleLabel(assignment.VehicleId)} + {ResolveTrailerLabel(assignment.TrailerId)}.",
                    Suggestion: "Kombination in Fahrzeugverwaltung aktivieren oder kompatible Kombination waehlen."));
            }
        }

        return warnings
            .DistinctBy(x => $"{x.Title}|{x.Message}", StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static int CalculateTourWeightKg(TourRecord tour)
    {
        return (tour.Stops ?? [])
            .Where(s => !IsCompanyStop(s))
            .Sum(s => ParseWeightKg(s.Gewicht));
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId)
        {
            return;
        }

        var relevantKinds = AppDataKind.Tours | AppDataKind.Vehicles | AppDataKind.Employees;
        if ((args.Kinds & relevantKinds) == AppDataKind.None)
        {
            return;
        }

        _ = RefreshAsync();
    }

    private static bool NormalizeCompanyStops(IReadOnlyList<TourRecord> tours, AppSettings settings)
    {
        var changed = false;
        var companyName = string.IsNullOrWhiteSpace(settings.CompanyName) ? "Firma" : settings.CompanyName.Trim();
        var companyAddress = BuildCompanyAddress(settings);

        foreach (var tour in tours)
        {
            var originalCount = tour.Stops.Count;
            tour.Stops = (tour.Stops ?? [])
                .Where(s => !IsEmptyPlaceholderStop(s))
                .ToList();
            if (tour.Stops.Count != originalCount)
            {
                changed = true;
            }

            var start = tour.Stops.FirstOrDefault(s => string.Equals(s.Id, TourStopIdentity.CompanyStartStopId, StringComparison.OrdinalIgnoreCase));
            if (start is null)
            {
                start = new TourStopRecord();
                tour.Stops.Insert(0, start);
                changed = true;
            }

            start.Id = TourStopIdentity.CompanyStartStopId;
            start.Auftragsnummer = TourStopIdentity.CompanyStartOrderNumber;
            start.Name = $"{companyName} (Start)";
            start.Address = companyAddress;
            start.ServiceMinutes = 0;

            var end = tour.Stops.LastOrDefault(s => string.Equals(s.Id, TourStopIdentity.CompanyEndStopId, StringComparison.OrdinalIgnoreCase));
            if (end is null)
            {
                end = new TourStopRecord();
                tour.Stops.Add(end);
                changed = true;
            }

            end.Id = TourStopIdentity.CompanyEndStopId;
            end.Auftragsnummer = TourStopIdentity.CompanyEndOrderNumber;
            end.Name = $"{companyName} (Ende)";
            end.Address = companyAddress;
            end.ServiceMinutes = 0;

            // Keep exactly one company start/end at deterministic positions.
            var middle = tour.Stops
                .Where(s => !string.Equals(s.Id, TourStopIdentity.CompanyStartStopId, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(s.Id, TourStopIdentity.CompanyEndStopId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            tour.Stops = [start, .. middle, end];
            for (var i = 0; i < tour.Stops.Count; i++)
            {
                tour.Stops[i].Order = i + 1;
            }
        }

        return changed;
    }

    private static bool IsEmptyPlaceholderStop(TourStopRecord stop)
    {
        return string.IsNullOrWhiteSpace(stop.Id) &&
               string.IsNullOrWhiteSpace(stop.Name) &&
               string.IsNullOrWhiteSpace(stop.Auftragsnummer) &&
               string.IsNullOrWhiteSpace(stop.Address);
    }

    private static bool IsCompanyStop(TourStopRecord stop)
    {
        return TourStopIdentity.IsCompanyStop(stop);
    }

    private static string NormalizeCompanyStopName(string name)
    {
        return TourStopIdentity.NormalizeCompanyStopDisplayName(name);
    }

    private static string BuildCompanyAddress(AppSettings settings)
    {
        var street = (settings.CompanyStreet ?? string.Empty).Trim();
        var postalCode = (settings.CompanyPostalCode ?? string.Empty).Trim();
        var city = (settings.CompanyCity ?? string.Empty).Trim();
        var zipCity = string.Join(' ', new[] { postalCode, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var parts = new[] { street, zipCity }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return parts.Length == 0 ? "Firmenadresse nicht gesetzt" : string.Join(", ", parts);
    }
}

internal readonly record struct TourOrderMetrics(
    string EndTime,
    int EndMinutes,
    int DurationMinutes,
    double DistanceKm);

internal readonly record struct TourPlanningWarningItem(
    string Title,
    string Message,
    string Suggestion);

public sealed class TourOverviewItem
{
    public int TourId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string TrailerId { get; set; } = string.Empty;
    public string Employees { get; set; } = string.Empty;
    public int StopCount { get; set; }
    public int TotalWeightKg { get; set; }
    public int StopConflicts { get; set; }
    public int AssignmentConflicts { get; set; }
    public bool IsArchived { get; set; }
    public TourRecord Source { get; set; } = new();
}

public sealed class TourStopOverviewItem
{
    public int SourceTourId { get; set; }
    public TourStopRecord Source { get; set; } = new();
    public bool IsCompanyStop { get; set; }
    public bool IsRouteStart { get; set; }
    public bool IsRouteEnd { get; set; }
    public string Order { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public string Arrival { get; set; } = string.Empty;
    public string Departure { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string Conflict { get; set; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(OrderNumber) ? Name : $"{Name} ({OrderNumber})";
    public string DisplayTime => !string.IsNullOrWhiteSpace(Arrival) ? Arrival : Departure;
}

public sealed class LookupItem
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class SelectableEmployeeItem : ObservableObject
{
    private bool _isSelected;

    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class DateFilterCalendarDayItem : ObservableObject
{
    private bool _isSelected;

    public DateTime Date { get; set; }

    public string DayText { get; set; } = string.Empty;

    public bool IsCurrentMonth { get; set; }

    public bool IsToday { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

