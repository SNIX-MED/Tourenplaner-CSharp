using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class ToursSectionViewModel : SectionViewModelBase
{
    private readonly JsonToursRepository _repository;
    private readonly TourScheduleService _scheduleService;
    private readonly TourConflictService _conflictService;

    private string _statusText = "Lade Touren...";
    private TourOverviewItem? _selectedTour;

    public ToursSectionViewModel(string toursJsonPath)
        : base("Tours", "Tour creation, stop sequencing, ETA/ETD and assignment conflict checks.")
    {
        _repository = new JsonToursRepository(toursJsonPath);
        _scheduleService = new TourScheduleService();
        _conflictService = new TourConflictService(_scheduleService);

        RefreshCommand = new AsyncCommand(RefreshAsync);
        RecalculateCommand = new AsyncCommand(RecalculateAndSaveAsync, () => Tours.Count > 0);
        _ = RefreshAsync();
    }

    public ObservableCollection<TourOverviewItem> Tours { get; } = new();

    public ObservableCollection<TourStopOverviewItem> SelectedTourStops { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand RecalculateCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public TourOverviewItem? SelectedTour
    {
        get => _selectedTour;
        set
        {
            if (SetProperty(ref _selectedTour, value))
            {
                LoadSelectedTourStops();
            }
        }
    }

    public async Task RefreshAsync()
    {
        var tours = (await _repository.LoadAsync()).ToList();
        var conflicts = _conflictService.FindAssignmentConflicts(tours)
            .GroupBy(c => c.TourIdA)
            .ToDictionary(g => g.Key, g => g.Count());

        Tours.Clear();
        foreach (var tour in tours.OrderBy(t => t.Date).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var schedule = _scheduleService.BuildSchedule(tour);
            var employeeText = string.Join(", ", tour.EmployeeIds ?? []);
            var row = new TourOverviewItem
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
            };

            Tours.Add(row);
        }

        SelectedTour = Tours.FirstOrDefault();
        StatusText = $"Touren: {Tours.Count} | Stop-Konflikte: {Tours.Sum(t => t.StopConflicts)} | Ressourcenkonflikte: {Tours.Sum(t => t.AssignmentConflicts)}";
        RaiseCommandStates();
    }

    public async Task RecalculateAndSaveAsync()
    {
        var tours = (await _repository.LoadAsync()).ToList();
        foreach (var tour in tours)
        {
            _scheduleService.ApplySchedule(tour);
        }

        await _repository.SaveAsync(tours);
        await RefreshAsync();
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
                Conflict = stop.ScheduleConflict ? (string.IsNullOrWhiteSpace(stop.ScheduleConflictText) ? "Ja" : stop.ScheduleConflictText) : string.Empty
            });
        }
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
