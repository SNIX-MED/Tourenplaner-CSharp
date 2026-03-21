using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class ToursSectionViewModel : SectionViewModelBase
{
    private readonly JsonToursRepository _tourRepository;
    private readonly JsonEmployeesRepository _employeeRepository;
    private readonly JsonVehicleDataRepository _vehicleRepository;
    private readonly TourScheduleService _scheduleService;
    private readonly TourConflictService _conflictService;

    private readonly List<TourRecord> _loadedTours = new();
    private bool _editorSyncInProgress;

    private string _statusText = "Lade Touren...";
    private string _editorDate = string.Empty;
    private string _editorStartTime = "08:00";
    private string _editorConflictText = string.Empty;
    private LookupItem? _selectedVehicle;
    private LookupItem? _selectedTrailer;
    private TourOverviewItem? _selectedTour;

    public ToursSectionViewModel(string toursJsonPath, string employeesJsonPath, string vehiclesJsonPath)
        : base("Tours", "Tour creation, stop sequencing, ETA/ETD and assignment conflict checks.")
    {
        _tourRepository = new JsonToursRepository(toursJsonPath);
        _employeeRepository = new JsonEmployeesRepository(employeesJsonPath);
        _vehicleRepository = new JsonVehicleDataRepository(vehiclesJsonPath);
        _scheduleService = new TourScheduleService();
        _conflictService = new TourConflictService(_scheduleService);

        RefreshCommand = new AsyncCommand(RefreshAsync);
        RecalculateCommand = new AsyncCommand(RecalculateAndSaveAsync, () => Tours.Count > 0);
        SaveAssignmentCommand = new AsyncCommand(SaveSelectedAssignmentAsync, () => SelectedTour is not null);
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

    public TourOverviewItem? SelectedTour
    {
        get => _selectedTour;
        set
        {
            if (SetProperty(ref _selectedTour, value))
            {
                LoadSelectedTourStops();
                SyncEditorFromSelection();
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        await LoadReferenceDataAsync();
        _loadedTours.Clear();
        _loadedTours.AddRange(await _tourRepository.LoadAsync());

        RebuildTourRows(_loadedTours, keepSelectionTourId: SelectedTour?.TourId);
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

    private async Task LoadReferenceDataAsync()
    {
        var employees = await _employeeRepository.LoadAsync();
        var vehicles = await _vehicleRepository.LoadAsync();

        AvailableVehicles.Clear();
        AvailableVehicles.Add(new LookupItem { Id = string.Empty, Label = "(none)" });
        foreach (var vehicle in vehicles.Vehicles.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
        {
            AvailableVehicles.Add(new LookupItem { Id = vehicle.Id, Label = $"{vehicle.Name} [{vehicle.LicensePlate}]" });
        }

        AvailableTrailers.Clear();
        AvailableTrailers.Add(new LookupItem { Id = string.Empty, Label = "(none)" });
        foreach (var trailer in vehicles.Trailers.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
        {
            AvailableTrailers.Add(new LookupItem { Id = trailer.Id, Label = $"{trailer.Name} [{trailer.LicensePlate}]" });
        }

        AvailableEmployees.Clear();
        foreach (var employee in employees.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
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
            var employeeText = string.Join(", ", tour.EmployeeIds ?? []);
            Tours.Add(new TourOverviewItem
            {
                TourId = tour.Id,
                Name = tour.Name,
                Date = tour.Date,
                Start = schedule.Start.ToString("HH:mm"),
                End = schedule.End.ToString("HH:mm"),
                VehicleId = tour.VehicleId ?? string.Empty,
                TrailerId = tour.TrailerId ?? string.Empty,
                Employees = employeeText,
                StopCount = tour.Stops.Count,
                StopConflicts = schedule.Stops.Count(s => s.HasConflict),
                AssignmentConflicts = conflicts.TryGetValue(tour.Id, out var count) ? count : 0,
                Source = tour
            });
        }

        SelectedTour = Tours.FirstOrDefault(t => keepSelectionTourId.HasValue && t.TourId == keepSelectionTourId.Value) ?? Tours.FirstOrDefault();
        StatusText = $"Tours: {Tours.Count} | Stop conflicts: {Tours.Sum(t => t.StopConflicts)} | Assignment conflicts: {Tours.Sum(t => t.AssignmentConflicts)}";
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
            SelectedTourStops.Add(new TourStopOverviewItem
            {
                Order = stop.Order,
                Name = stop.Name,
                Address = stop.Address,
                Window = $"{stop.TimeWindowStart} - {stop.TimeWindowEnd}".Trim(' ', '-'),
                Arrival = stop.PlannedArrival,
                Departure = stop.PlannedDeparture,
                Conflict = stop.ScheduleConflict ? (string.IsNullOrWhiteSpace(stop.ScheduleConflictText) ? "Yes" : stop.ScheduleConflictText) : string.Empty
            });
        }
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
    public int StopConflicts { get; set; }
    public int AssignmentConflicts { get; set; }
    public TourRecord Source { get; set; } = new();
}

public sealed class TourStopOverviewItem
{
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public string Arrival { get; set; } = string.Empty;
    public string Departure { get; set; } = string.Empty;
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
