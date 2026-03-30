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
    private string _selectedTourVehicleText = "Fahrzeug: -";
    private LookupItem? _selectedVehicle;
    private LookupItem? _selectedTrailer;
    private TourOverviewItem? _selectedTour;

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
            ApplyDateFilter();
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
            ApplyDateFilter();
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
                LoadSelectedTourStops();
                SyncEditorFromSelection();
                UpdateSelectedTourSummary();
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

        var originalDate = target.Date;
        var originalStartTime = target.StartTime;
        var originalVehicleId = target.VehicleId;
        var originalTrailerId = target.TrailerId;
        var originalEmployeeIds = target.EmployeeIds.ToList();

        target.Date = (EditorDate ?? string.Empty).Trim();
        target.StartTime = (EditorStartTime ?? string.Empty).Trim();
        target.VehicleId = SelectedVehicle?.Id;
        target.TrailerId = SelectedTrailer?.Id;
        target.EmployeeIds = AvailableEmployees
            .Where(e => e.IsSelected)
            .Select(e => e.Id)
            .Take(2)
            .ToList();

        if (!ConfirmAssignmentConflictWarning(_loadedTours, target.Id))
        {
            target.Date = originalDate;
            target.StartTime = originalStartTime;
            target.VehicleId = originalVehicleId;
            target.TrailerId = originalTrailerId;
            target.EmployeeIds = originalEmployeeIds;
            return;
        }

        if (!ConfirmCapacityWarning(target.VehicleId, target.TrailerId, CalculateTourWeightKg(target)))
        {
            target.Date = originalDate;
            target.StartTime = originalStartTime;
            target.VehicleId = originalVehicleId;
            target.TrailerId = originalTrailerId;
            target.EmployeeIds = originalEmployeeIds;
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
        IsToDatePopupOpen = false;
        IsFromDatePopupOpen = !IsFromDatePopupOpen;
        if (IsFromDatePopupOpen)
        {
            if (FromDate.HasValue)
            {
                _fromCalendarMonth = new DateTime(FromDate.Value.Year, FromDate.Value.Month, 1);
            }

            RebuildFromCalendarDays();
        }
    }

    private void ToggleToDatePopup()
    {
        IsFromDatePopupOpen = false;
        IsToDatePopupOpen = !IsToDatePopupOpen;
        if (IsToDatePopupOpen)
        {
            if (ToDate.HasValue)
            {
                _toCalendarMonth = new DateTime(ToDate.Value.Year, ToDate.Value.Month, 1);
            }

            RebuildToCalendarDays();
        }
    }

    private void ShowPreviousFromMonth()
    {
        _fromCalendarMonth = _fromCalendarMonth.AddMonths(-1);
        RebuildFromCalendarDays();
    }

    private void ShowNextFromMonth()
    {
        _fromCalendarMonth = _fromCalendarMonth.AddMonths(1);
        RebuildFromCalendarDays();
    }

    private void ShowPreviousToMonth()
    {
        _toCalendarMonth = _toCalendarMonth.AddMonths(-1);
        RebuildToCalendarDays();
    }

    private void ShowNextToMonth()
    {
        _toCalendarMonth = _toCalendarMonth.AddMonths(1);
        RebuildToCalendarDays();
    }

    private void SetTodayFilter()
    {
        var today = DateTime.Today;
        FromDate = today;
        ToDate = today;
        ApplyDateFilter();
    }

    private void SetTomorrowFilter()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        FromDate = tomorrow;
        ToDate = tomorrow;
        ApplyDateFilter();
    }

    private void SetWeekFilter()
    {
        var today = DateTime.Today;
        var offset = (7 + ((int)today.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        var start = today.AddDays(-offset);
        var end = start.AddDays(6);
        FromDate = start;
        ToDate = end;
        ApplyDateFilter();
    }

    private void ResetDateFilter()
    {
        FromDate = null;
        ToDate = null;
        ApplyDateFilter();
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

    public async Task<bool> MoveStopWithinSelectedTourAsync(TourStopOverviewItem sourceItem, TourStopOverviewItem? targetItem)
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

        var movableStops = tour.Stops.Where(s => !IsCompanyStop(s)).ToList();
        if (movableStops.Count < 2)
        {
            return false;
        }

        var sourceIndex = movableStops.IndexOf(sourceItem.Source);
        if (sourceIndex < 0)
        {
            return false;
        }

        var targetStop = targetItem?.Source;
        var targetIndex = targetStop is null || (targetItem?.IsCompanyStop ?? false)
            ? movableStops.Count - 1
            : movableStops.IndexOf(targetStop);

        if (targetIndex < 0 || sourceIndex == targetIndex)
        {
            return false;
        }

        var moved = movableStops[sourceIndex];
        movableStops.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        movableStops.Insert(targetIndex, moved);
        RebuildTourStopsWithAnchors(tour, movableStops);
        _scheduleService.ApplySchedule(tour);

        await _tourRepository.SaveAsync(_loadedTours);
        _dataSyncService.PublishTours(_instanceId, tour.Id.ToString(CultureInfo.InvariantCulture), tour.Id.ToString(CultureInfo.InvariantCulture));

        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: tour.Id);
        StatusText = $"Stopps in Tour {tour.Name} wurden neu angeordnet.";
        return true;
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
        var (employees, vehicles, trailers) = await LoadTourDialogOptionsAsync();

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
        var originalEmployeeIds = tour.EmployeeIds.ToList();

        tour.Name = result.RouteName;
        tour.Date = result.RouteDate;
        tour.StartTime = result.StartTime;
        tour.VehicleId = string.IsNullOrWhiteSpace(result.VehicleId) ? null : result.VehicleId.Trim();
        tour.TrailerId = string.IsNullOrWhiteSpace(result.TrailerId) ? null : result.TrailerId.Trim();
        tour.EmployeeIds = (result.EmployeeIds ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        if (!ConfirmAssignmentConflictWarning(_loadedTours, tour.Id))
        {
            tour.Name = originalName;
            tour.Date = originalDate;
            tour.StartTime = originalStartTime;
            tour.VehicleId = originalVehicleId;
            tour.TrailerId = originalTrailerId;
            tour.EmployeeIds = originalEmployeeIds;
            return;
        }

        if (!ConfirmCapacityWarning(tour.VehicleId, tour.TrailerId, CalculateTourWeightKg(tour)))
        {
            tour.Name = originalName;
            tour.Date = originalDate;
            tour.StartTime = originalStartTime;
            tour.VehicleId = originalVehicleId;
            tour.TrailerId = originalTrailerId;
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

    private async Task<(List<TourEmployeeOption> Employees, List<TourLookupOption> Vehicles, List<TourLookupOption> Trailers)> LoadTourDialogOptionsAsync()
    {
        var employeesTask = _employeeRepository.LoadAsync();
        var vehiclesTask = _vehicleRepository.LoadAsync();
        await Task.WhenAll(employeesTask, vehiclesTask);

        var employees = (await employeesTask)
            .Where(x => x.Active)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TourEmployeeOption(x.Id, x.DisplayName))
            .ToList();

        var vehicleData = await vehiclesTask;
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
        FilterInfoText = BuildDateFilterInfoText(from, to, Tours.Count);
    }

    private IEnumerable<TourRecord> ApplyDateFilterToTours(IEnumerable<TourRecord> tours, DateTime? from, DateTime? to)
    {
        if (from is null && to is null)
        {
            return tours;
        }

        var fromValue = from ?? DateTime.MinValue.Date;
        var toValue = to ?? DateTime.MaxValue.Date;
        if (fromValue > toValue)
        {
            (fromValue, toValue) = (toValue, fromValue);
        }

        return tours.Where(t =>
        {
            var tourDate = ParseDate(t.Date);
            if (tourDate is null)
            {
                return false;
            }

            return tourDate.Value.Date >= fromValue && tourDate.Value.Date <= toValue;
        });
    }

    private static string BuildDateFilterInfoText(DateTime? from, DateTime? to, int matches)
    {
        if (from is null && to is null)
        {
            return $"Alle Touren | Treffer: {matches}";
        }

        var fromText = from?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "offen";
        var toText = to?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "offen";
        return $"{fromText} - {toText} | Treffer: {matches}";
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
                VehicleId = ResolveVehicleShortLabel(tour.VehicleId),
                TrailerId = ResolveTrailerLabel(tour.TrailerId),
                Employees = employeeText,
                StopCount = tour.Stops.Count(s => !IsCompanyStop(s)),
                TotalWeightKg = totalWeight,
                StopConflicts = schedule.Stops.Count(s => s.HasConflict),
                AssignmentConflicts = conflicts.TryGetValue(tour.Id, out var count) ? count : 0,
                Source = tour
            });
        }

        SelectedTour = Tours.FirstOrDefault(t => keepSelectionTourId.HasValue && t.TourId == keepSelectionTourId.Value) ?? Tours.FirstOrDefault();
        StatusText = $"Tours: {Tours.Count} | Stop conflicts: {Tours.Sum(t => t.StopConflicts)} | Assignment conflicts: {Tours.Sum(t => t.AssignmentConflicts)}";
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
        if (SelectedTour?.Source is null)
        {
            return;
        }

        foreach (var stop in SelectedTour.Source.Stops
                     .OrderBy(GetStopDisplayOrderGroup)
                     .ThenBy(s => s.Order))
        {
            var isCompanyStop = IsCompanyStop(stop);
            SelectedTourStops.Add(new TourStopOverviewItem
            {
                SourceTourId = SelectedTour.TourId,
                Source = stop,
                IsCompanyStop = isCompanyStop,
                Order = isCompanyStop ? string.Empty : stop.Order.ToString(CultureInfo.InvariantCulture),
                OrderNumber = isCompanyStop ? string.Empty : stop.Auftragsnummer,
                Name = isCompanyStop ? NormalizeCompanyStopName(stop.Name) : stop.Name,
                Address = isCompanyStop ? string.Empty : stop.Address,
                Window = isCompanyStop ? string.Empty : $"{stop.TimeWindowStart} - {stop.TimeWindowEnd}".Trim(' ', '-'),
                Arrival = isCompanyStop ? string.Empty : stop.PlannedArrival,
                Departure = isCompanyStop ? string.Empty : stop.PlannedDeparture,
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
            SelectedTourVehicleText = "Fahrzeug: -";
            return;
        }

        var totalWeight = SelectedTour.Source.Stops
            .Where(s => !IsCompanyStop(s))
            .Sum(s => ParseWeightKg(s.Gewicht));
        SelectedTourWeightText = $"Totalgewicht: {totalWeight} kg";

        var display = VehicleCombinationDisplayResolver.Resolve(
            _vehicleData,
            SelectedVehicle?.Id,
            SelectedTrailer?.Id);
        var vehicleLabel = string.IsNullOrWhiteSpace(display.VehicleLabel)
            ? ResolveVehicleLabel(SelectedTour.Source.VehicleId)
            : display.VehicleLabel;
        var trailerLabel = string.IsNullOrWhiteSpace(display.TrailerLabel)
            ? ResolveTrailerLabel(SelectedTour.Source.TrailerId)
            : display.TrailerLabel;

        var summaryLines = new List<string>
        {
            $"Fahrzeug: {(!string.IsNullOrWhiteSpace(vehicleLabel) ? vehicleLabel : "-")}"
        };

        if (!string.IsNullOrWhiteSpace(trailerLabel))
        {
            summaryLines.Add($"Anhänger: {trailerLabel}");
        }

        var loadSummaryLines = new List<string>();
        if (display.HasVehiclePayload)
        {
            loadSummaryLines.Add($"Ladegewicht: {display.VehiclePayloadKg} kg");
        }

        if (display.TrailerLoadKg.HasValue)
        {
            loadSummaryLines.Add($"Anhängelast: {display.TrailerLoadKg} kg");
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

        var conflicts = _conflictService.FindAssignmentConflicts(previewTours)
            .Where(c => c.TourIdA == selectedTourId || c.TourIdB == selectedTourId)
            .Select(c => c.Message)
            .Distinct()
            .ToList();

        EditorConflictText = conflicts.Count == 0
            ? "No assignment conflicts for current selection."
            : string.Join(" | ", conflicts);
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
                Gewicht = stop.Gewicht
            }).ToList()
        };
    }

    private void RaiseCommandStates()
    {
        if (RefreshCommand is AsyncCommand refresh)
        {
            refresh.RaiseCanExecuteChanged();
        }

        if (RecalculateCommand is AsyncCommand recalculate)
        {
            recalculate.RaiseCanExecuteChanged();
        }

        if (SaveAssignmentCommand is AsyncCommand save)
        {
            save.RaiseCanExecuteChanged();
        }

        if (DeleteTourCommand is AsyncCommand delete)
        {
            delete.RaiseCanExecuteChanged();
        }

        if (OpenTourOnMapCommand is AsyncCommand openOnMap)
        {
            openOnMap.RaiseCanExecuteChanged();
        }

        if (EditTourOnMapCommand is AsyncCommand editTour)
        {
            editTour.RaiseCanExecuteChanged();
        }
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

    private bool ConfirmCapacityWarning(string? vehicleId, string? trailerId, int totalWeightKg)
    {
        var warning = TourCapacityWarningService.Evaluate(_vehicleData, vehicleId, trailerId, totalWeightKg);
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
    public TourRecord Source { get; set; } = new();
}

public sealed class TourStopOverviewItem
{
    public int SourceTourId { get; set; }
    public TourStopRecord Source { get; set; } = new();
    public bool IsCompanyStop { get; set; }
    public string Order { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public string Arrival { get; set; } = string.Empty;
    public string Departure { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string Conflict { get; set; } = string.Empty;
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

