using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class ToursSectionViewModel : SectionViewModelBase
{
    private readonly JsonToursRepository _tourRepository;
    private readonly JsonEmployeesRepository _employeeRepository;
    private readonly JsonVehicleDataRepository _vehicleRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly TourScheduleService _scheduleService;
    private readonly TourConflictService _conflictService;

    private readonly List<TourRecord> _loadedTours = new();
    private readonly Dictionary<string, string> _vehicleLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _trailerLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _employeeLabelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<int, Task>? _openTourOnMapAsync;
    private bool _editorSyncInProgress;

    private string _statusText = "Lade Touren...";
    private string _editorDate = string.Empty;
    private string _editorStartTime = "08:00";
    private string _editorConflictText = string.Empty;
    private string _fromDateText = string.Empty;
    private string _toDateText = string.Empty;
    private string _filterInfoText = "Alle Touren | Treffer: 0";
    private string _selectedTourWeightText = "Totalgewicht: 0 kg";
    private string _selectedTourVehicleText = "Fahrzeug: -";
    private LookupItem? _selectedVehicle;
    private LookupItem? _selectedTrailer;
    private TourOverviewItem? _selectedTour;

    public ToursSectionViewModel(
        string toursJsonPath,
        string employeesJsonPath,
        string vehiclesJsonPath,
        string settingsJsonPath,
        Func<int, Task>? openTourOnMapAsync = null)
        : base("Tours", "Tour creation, stop sequencing, ETA/ETD and assignment conflict checks.")
    {
        _tourRepository = new JsonToursRepository(toursJsonPath);
        _employeeRepository = new JsonEmployeesRepository(employeesJsonPath);
        _vehicleRepository = new JsonVehicleDataRepository(vehiclesJsonPath);
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
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
        DeleteTourCommand = new AsyncCommand(DeleteSelectedTourAsync, () => SelectedTour is not null);
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
        set => SetProperty(ref _fromDateText, value);
    }

    public string ToDateText
    {
        get => _toDateText;
        set => SetProperty(ref _toDateText, value);
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

        target.Date = (EditorDate ?? string.Empty).Trim();
        target.StartTime = (EditorStartTime ?? string.Empty).Trim();
        target.VehicleId = SelectedVehicle?.Id;
        target.TrailerId = SelectedTrailer?.Id;
        target.EmployeeIds = AvailableEmployees
            .Where(e => e.IsSelected)
            .Select(e => e.Id)
            .Take(2)
            .ToList();

        _scheduleService.ApplySchedule(target);
        await _tourRepository.SaveAsync(_loadedTours);
        await RefreshAsync();
    }

    private void ApplyDateFilter()
    {
        RebuildTourRowsWithCurrentFilter(keepSelectionTourId: SelectedTour?.TourId);
    }

    private void SetTodayFilter()
    {
        var today = DateTime.Today;
        FromDateText = today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        ToDateText = today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        ApplyDateFilter();
    }

    private void SetTomorrowFilter()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        FromDateText = tomorrow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        ToDateText = tomorrow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        ApplyDateFilter();
    }

    private void SetWeekFilter()
    {
        var today = DateTime.Today;
        var offset = (7 + ((int)today.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        var start = today.AddDays(-offset);
        var end = start.AddDays(6);
        FromDateText = start.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        ToDateText = end.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        ApplyDateFilter();
    }

    private void ResetDateFilter()
    {
        FromDateText = string.Empty;
        ToDateText = string.Empty;
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

    private async Task LoadReferenceDataAsync()
    {
        var employees = await _employeeRepository.LoadAsync();
        var vehicles = await _vehicleRepository.LoadAsync();

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
        RebuildTourRowsWithCurrentFilter();
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

        _scheduleService.ApplySchedule(tour);
        await _tourRepository.SaveAsync(_loadedTours);
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

    private void RebuildTourRowsWithCurrentFilter(int? keepSelectionTourId = null)
    {
        var filtered = ApplyDateFilterToTours(_loadedTours);
        RebuildTourRows(filtered, keepSelectionTourId);
        FilterInfoText = $"Alle Touren | Treffer: {Tours.Count}";
    }

    private IEnumerable<TourRecord> ApplyDateFilterToTours(IEnumerable<TourRecord> tours)
    {
        var from = ParseDate(FromDateText);
        var to = ParseDate(ToDateText);
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

    private static DateTime? ParseDate(string? value)
    {
        if (DateTime.TryParseExact((value ?? string.Empty).Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.Date;
        }

        return null;
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
            var employeeText = string.Join(", ", (tour.EmployeeIds ?? [])
                .Select(ResolveEmployeeLabel)
                .Where(x => !string.IsNullOrWhiteSpace(x)));
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
                VehicleId = ResolveVehicleLabel(tour.VehicleId),
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

    private void LoadSelectedTourStops()
    {
        SelectedTourStops.Clear();
        if (SelectedTour?.Source is null)
        {
            return;
        }

        foreach (var stop in SelectedTour.Source.Stops.OrderBy(s => s.Order))
        {
            var isCompanyStop = IsCompanyStop(stop);
            SelectedTourStops.Add(new TourStopOverviewItem
            {
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

    private void UpdateSelectedTourSummary()
    {
        if (SelectedTour?.Source is null)
        {
            SelectedTourWeightText = "Totalgewicht: 0 kg";
            SelectedTourVehicleText = "Fahrzeug: -";
            return;
        }

        var totalWeight = SelectedTour.Source.Stops
            .Where(s => !IsCompanyStop(s))
            .Sum(s => ParseWeightKg(s.Gewicht));
        SelectedTourWeightText = $"Totalgewicht: {totalWeight} kg";
        SelectedTourVehicleText = $"Fahrzeug: {ResolveVehicleLabel(SelectedTour.Source.VehicleId)}";
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
