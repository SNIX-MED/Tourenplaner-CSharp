using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class KalenderSectionViewModel : SectionViewModelBase
{
    private readonly JsonToursRepository _repository;
    private readonly List<TourRecord> _allTours = new();

    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string _statusText = "Loading calendar...";
    private CalendarDayItem? _selectedDay;

    public KalenderSectionViewModel(string toursJsonPath) : base("Kalender", "Monthly tour overview with per-day details.")
    {
        _repository = new JsonToursRepository(toursJsonPath);
        PreviousMonthCommand = new DelegateCommand(ShowPreviousMonth);
        NextMonthCommand = new DelegateCommand(ShowNextMonth);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        _ = RefreshAsync();
    }

    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = new();

    public ObservableCollection<CalendarTourItem> SelectedDayTours { get; } = new();

    public ICommand PreviousMonthCommand { get; }

    public ICommand NextMonthCommand { get; }

    public ICommand RefreshCommand { get; }

    public string DisplayMonthText => _displayMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public CalendarDayItem? SelectedDay
    {
        get => _selectedDay;
        set
        {
            if (SetProperty(ref _selectedDay, value))
            {
                LoadSelectedDayTours();
            }
        }
    }

    public async Task RefreshAsync()
    {
        _allTours.Clear();
        _allTours.AddRange(await _repository.LoadAsync());
        BuildMonth();
    }

    private void ShowPreviousMonth()
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        OnMonthChanged();
    }

    private void ShowNextMonth()
    {
        _displayMonth = _displayMonth.AddMonths(1);
        OnMonthChanged();
    }

    private void OnMonthChanged()
    {
        OnPropertyChanged(nameof(DisplayMonthText));
        BuildMonth();
    }

    private void BuildMonth()
    {
        CalendarDays.Clear();
        SelectedDayTours.Clear();

        var first = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var monthTourLookup = _allTours
            .Select(t => (Tour: t, Date: ParseTourDate(t.Date)))
            .Where(x => x.Date is not null && x.Date.Value.Year == _displayMonth.Year && x.Date.Value.Month == _displayMonth.Month)
            .GroupBy(x => x.Date!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Tour).ToList());

        var leadingEmpty = ((int)first.DayOfWeek + 6) % 7; // monday first
        for (var i = 0; i < leadingEmpty; i++)
        {
            CalendarDays.Add(new CalendarDayItem
            {
                IsPlaceholder = true,
                DayLabel = string.Empty,
                TourCount = 0
            });
        }

        for (var day = 1; day <= last.Day; day++)
        {
            var date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
            var count = monthTourLookup.TryGetValue(date.Date, out var tours) ? tours.Count : 0;
            CalendarDays.Add(new CalendarDayItem
            {
                IsPlaceholder = false,
                Date = date.Date,
                DayLabel = day.ToString(CultureInfo.InvariantCulture),
                TourCount = count
            });
        }

        SelectedDay = CalendarDays.FirstOrDefault(x => !x.IsPlaceholder && x.Date == DateTime.Today.Date)
            ?? CalendarDays.FirstOrDefault(x => !x.IsPlaceholder);

        var totalTours = monthTourLookup.Values.Sum(v => v.Count);
        StatusText = $"Month tours: {totalTours} | Days with tours: {monthTourLookup.Count}";
    }

    private void LoadSelectedDayTours()
    {
        SelectedDayTours.Clear();
        if (SelectedDay is null || SelectedDay.IsPlaceholder || SelectedDay.Date is null)
        {
            return;
        }

        var date = SelectedDay.Date.Value.Date;
        var tours = _allTours
            .Where(t => ParseTourDate(t.Date)?.Date == date)
            .OrderBy(t => t.StartTime)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var tour in tours)
        {
            SelectedDayTours.Add(new CalendarTourItem
            {
                TourId = tour.Id,
                Name = tour.Name,
                StartTime = tour.StartTime,
                VehicleId = tour.VehicleId ?? string.Empty,
                Employees = string.Join(", ", tour.EmployeeIds),
                StopCount = tour.Stops.Count
            });
        }

        if (SelectedDayTours.Count == 0)
        {
            StatusText = $"Selected day {date:dd.MM.yyyy}: no tours.";
        }
        else
        {
            StatusText = $"Selected day {date:dd.MM.yyyy}: {SelectedDayTours.Count} tour(s).";
        }
    }

    private static DateTime? ParseTourDate(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[] { "dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy" };
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.Date;
            }
        }

        return null;
    }
}

public sealed class CalendarDayItem
{
    public bool IsPlaceholder { get; set; }
    public DateTime? Date { get; set; }
    public string DayLabel { get; set; } = string.Empty;
    public int TourCount { get; set; }
}

public sealed class CalendarTourItem
{
    public int TourId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string Employees { get; set; } = string.Empty;
    public int StopCount { get; set; }
}
