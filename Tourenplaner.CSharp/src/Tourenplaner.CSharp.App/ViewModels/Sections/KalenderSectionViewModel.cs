using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class KalenderSectionViewModel : SectionViewModelBase
{
    private static readonly CultureInfo UiCulture = new("de-CH");
    private static readonly string[] SupportedDateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];
    private const int PreviewWeekCount = 4;

    private readonly JsonToursRepository _repository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly Func<int, Task>? _openTourAsync;
    private readonly Func<DateTime, Task>? _openDayInToursAsync;
    private readonly List<TourRecord> _allTours = [];
    private readonly List<CalendarDayItem> _interactiveDays = [];

    private DateTime _rangeStartMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string _rangeTitleText = string.Empty;
    private string _statusText = "Kalender wird geladen...";
    private string _selectedDayHeadline = "Ausgewaehlter Tag";
    private string _calendarLoadWarningColor = AppSettings.DefaultCalendarLoadWarningColor;
    private string _calendarLoadCriticalColor = AppSettings.DefaultCalendarLoadCriticalColor;
    private int _calendarLoadWarningPeopleThreshold = 1;
    private int _calendarLoadCriticalPeopleThreshold = 2;
    private CalendarDayItem? _selectedDay;
    private CalendarTourItem? _selectedDayTour;
    private UpcomingDayCardItem? _selectedUpcomingDay;

    public KalenderSectionViewModel(
        string toursJsonPath,
        string settingsJsonPath,
        Func<int, Task>? openTourAsync = null,
        Func<DateTime, Task>? openDayInToursAsync = null)
        : base("Kalender", "Uebersicht aller geplanten Touren. Ein Doppelklick oeffnet den Tag in den Liefertouren.")
    {
        _repository = new JsonToursRepository(toursJsonPath);
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _openTourAsync = openTourAsync;
        _openDayInToursAsync = openDayInToursAsync;

        PreviousRangeCommand = new DelegateCommand(ShowPreviousRange);
        NextRangeCommand = new DelegateCommand(ShowNextRange);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        OpenSelectedTourCommand = new AsyncCommand(OpenSelectedTourAsync, () => SelectedDayTour is not null);
        DeleteSelectedTourCommand = new AsyncCommand(DeleteSelectedTourAsync, () => SelectedDayTour is not null);
        OpenSelectedDayInToursCommand = new AsyncCommand(OpenSelectedDayInToursAsync, CanOpenSelectedDayInTours);

        _ = RefreshAsync();
    }

    public ObservableCollection<CalendarMonthItem> VisibleMonths { get; } = [];

    public ObservableCollection<CalendarTourItem> SelectedDayTours { get; } = [];

    public ObservableCollection<UpcomingDayCardItem> UpcomingDayCards { get; } = [];
    public ObservableCollection<StartWeekGroupItem> UpcomingWeeks { get; } = [];

    public ICommand PreviousRangeCommand { get; }

    public ICommand NextRangeCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenSelectedTourCommand { get; }

    public ICommand DeleteSelectedTourCommand { get; }

    public ICommand OpenSelectedDayInToursCommand { get; }

    public string RangeTitleText
    {
        get => _rangeTitleText;
        private set => SetProperty(ref _rangeTitleText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SelectedDayHeadline
    {
        get => _selectedDayHeadline;
        private set => SetProperty(ref _selectedDayHeadline, value);
    }

    public CalendarDayItem? SelectedDay
    {
        get => _selectedDay;
        set
        {
            if (!SetProperty(ref _selectedDay, value))
            {
                return;
            }

            UpdateDaySelectionFlags();
            LoadSelectedDayTours();
            SyncSelectedUpcomingCardFromSelectedDay();
            RaiseCommandStates();
        }
    }

    public CalendarTourItem? SelectedDayTour
    {
        get => _selectedDayTour;
        set
        {
            if (SetProperty(ref _selectedDayTour, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public UpcomingDayCardItem? SelectedUpcomingDay
    {
        get => _selectedUpcomingDay;
        set
        {
            if (!SetProperty(ref _selectedUpcomingDay, value))
            {
                return;
            }

            if (value is null)
            {
                return;
            }

            SelectDayByDate(value.Date);
        }
    }

    public async Task RefreshAsync()
    {
        var settingsTask = _settingsRepository.LoadAsync();
        var toursTask = _repository.LoadAsync();
        await Task.WhenAll(settingsTask, toursTask);

        var settings = await settingsTask;
        _calendarLoadWarningColor = NormalizeHexColor(settings.CalendarLoadWarningColor, AppSettings.DefaultCalendarLoadWarningColor);
        _calendarLoadCriticalColor = NormalizeHexColor(settings.CalendarLoadCriticalColor, AppSettings.DefaultCalendarLoadCriticalColor);
        _calendarLoadWarningPeopleThreshold = settings.CalendarLoadWarningPeopleThreshold < 1 ? 1 : settings.CalendarLoadWarningPeopleThreshold;
        _calendarLoadCriticalPeopleThreshold = settings.CalendarLoadCriticalPeopleThreshold < _calendarLoadWarningPeopleThreshold
            ? _calendarLoadWarningPeopleThreshold
            : settings.CalendarLoadCriticalPeopleThreshold;

        _allTours.Clear();
        _allTours.AddRange(await toursTask);
        BuildCalendarRange(preserveSelectionDate: SelectedDay?.Date ?? DateTime.Today);
    }

    public void HandleDayDoubleClick()
    {
        if (CanOpenSelectedDayInTours())
        {
            _ = OpenSelectedDayInToursAsync();
        }
    }

    private void ShowPreviousRange()
    {
        _rangeStartMonth = _rangeStartMonth.AddMonths(-1);
        BuildCalendarRange(preserveSelectionDate: SelectedDay?.Date ?? _rangeStartMonth);
    }

    private void ShowNextRange()
    {
        _rangeStartMonth = _rangeStartMonth.AddMonths(1);
        BuildCalendarRange(preserveSelectionDate: SelectedDay?.Date ?? _rangeStartMonth);
    }

    private void BuildCalendarRange(DateTime? preserveSelectionDate)
    {
        VisibleMonths.Clear();
        _interactiveDays.Clear();

        var firstMonth = new DateTime(_rangeStartMonth.Year, _rangeStartMonth.Month, 1);
        var secondMonth = firstMonth.AddMonths(1);

        VisibleMonths.Add(BuildMonth(firstMonth));
        VisibleMonths.Add(BuildMonth(secondMonth));

        RangeTitleText = $"{firstMonth.ToString("MMMM yyyy", UiCulture)}  |  {secondMonth.ToString("MMMM yyyy", UiCulture)}";

        BuildUpcomingCards();

        if (preserveSelectionDate is DateTime date)
        {
            SelectDayByDate(date);
        }

        if (SelectedDay is null)
        {
            SelectDayByDate(DateTime.Today);
        }

        if (SelectedDay is null)
        {
            SelectedDay = _interactiveDays.FirstOrDefault();
        }

        UpdateStatusText();
    }

    private CalendarMonthItem BuildMonth(DateTime monthStart)
    {
        var monthItem = new CalendarMonthItem
        {
            MonthTitle = monthStart.ToString("MMMM yyyy", UiCulture)
        };

        var firstDay = monthStart;
        var lastDay = monthStart.AddMonths(1).AddDays(-1);
        var leadingPlaceholderCount = ((int)firstDay.DayOfWeek + 6) % 7;

        for (var i = 0; i < leadingPlaceholderCount; i++)
        {
            monthItem.DayCells.Add(new CalendarDayItem
            {
                IsPlaceholder = true,
                DayLabel = string.Empty,
                TourCount = 0
            });
        }

        var dayLoadByDate = BuildDayLoadByDate();

        for (var day = 1; day <= lastDay.Day; day++)
        {
            var date = new DateTime(monthStart.Year, monthStart.Month, day).Date;
            if (!dayLoadByDate.TryGetValue(date, out var dayLoad))
            {
                dayLoad = new CalendarDayLoad(0, 0);
            }
            var item = new CalendarDayItem
            {
                IsPlaceholder = false,
                Date = date,
                DayLabel = day.ToString(UiCulture),
                TourCount = dayLoad.TourCount,
                AssignedPeopleCount = dayLoad.AssignedPeopleCount,
                IsToday = date == DateTime.Today
            };

            ApplyDayLoadAppearance(item, dayLoad.AssignedPeopleCount);

            monthItem.DayCells.Add(item);
            _interactiveDays.Add(item);
        }

        return monthItem;
    }

    private void BuildUpcomingCards()
    {
        UpcomingDayCards.Clear();
        UpcomingWeeks.Clear();

        var toursByDate = _allTours
            .Select(t => new { Tour = t, Date = ParseTourDate(t.Date) })
            .Where(x => x.Date is not null)
            .GroupBy(x => x.Date!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Tour).OrderBy(t => t.StartTime).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList());

        StartWeekGroupItem? currentWeek = null;
        for (var i = 0; i < 60; i++)
        {
            var date = DateTime.Today.AddDays(i).Date;
            toursByDate.TryGetValue(date, out var toursForDay);
            toursForDay ??= [];
            var assignedPeopleCount = toursForDay.Sum(GetAssignedPeopleCount);

            var card = new UpcomingDayCardItem
            {
                Date = date,
                DateText = date.ToString("dd.MM.yyyy", UiCulture),
                TourCountText = $"{toursForDay.Count} geplante Tour(en)",
                SummaryText = toursForDay.Count == 0
                    ? "Keine Tour geplant."
                    : string.Join(" | ", toursForDay.Take(2).Select(t => BuildTourSummary(t, includeName: true))),
                IsToday = date == DateTime.Today
            };
            ApplyDayLoadAppearance(card, assignedPeopleCount);
            UpcomingDayCards.Add(card);

            var weekLabel = BuildWeekLabel(date);
            if (currentWeek is null || !string.Equals(currentWeek.Label, weekLabel, StringComparison.Ordinal))
            {
                if (UpcomingWeeks.Count >= PreviewWeekCount)
                {
                    break;
                }

                currentWeek = new StartWeekGroupItem
                {
                    Label = weekLabel
                };
                UpcomingWeeks.Add(currentWeek);
            }

            currentWeek.Days.Add(card);
        }
    }

    private void LoadSelectedDayTours()
    {
        SelectedDayTours.Clear();

        if (SelectedDay?.Date is not DateTime selectedDate)
        {
            SelectedDayHeadline = "Ausgewaehlter Tag";
            StatusText = "Kein Kalendertag ausgewaehlt.";
            SelectedDayTour = null;
            return;
        }

        var dayTours = _allTours
            .Where(t => ParseTourDate(t.Date)?.Date == selectedDate)
            .OrderBy(t => ParseStartTimeMinutes(t.StartTime))
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tour in dayTours)
        {
            SelectedDayTours.Add(new CalendarTourItem
            {
                TourId = tour.Id,
                Name = tour.Name,
                StartTime = NormalizeStartTime(tour.StartTime),
                VehicleId = string.IsNullOrWhiteSpace(tour.VehicleId) ? "-" : tour.VehicleId!,
                Employees = string.Join(", ", tour.EmployeeIds),
                StopCount = tour.Stops.Count(s => !TourStopIdentity.IsCompanyStop(s)),
                Summary = BuildTourSummary(tour, includeName: false)
            });
        }

        SelectedDayTour = SelectedDayTours.FirstOrDefault();
        SelectedDayHeadline = $"Touren am {selectedDate.ToString("dddd, dd.MM.yyyy", UiCulture)}";

        if (SelectedDayTours.Count == 0)
        {
            StatusText = $"{selectedDate:dd.MM.yyyy}: keine Tour geplant.";
        }
        else
        {
            StatusText = $"{selectedDate:dd.MM.yyyy}: {SelectedDayTours.Count} Tour(en) gefunden.";
        }
    }

    private async Task OpenSelectedDayInToursAsync()
    {
        if (SelectedDay?.Date is not DateTime selectedDate || _openDayInToursAsync is null)
        {
            return;
        }

        await _openDayInToursAsync(selectedDate);
    }

    private async Task OpenSelectedTourAsync()
    {
        if (SelectedDayTour is null || _openTourAsync is null)
        {
            return;
        }

        await _openTourAsync(SelectedDayTour.TourId);
    }

    private async Task DeleteSelectedTourAsync()
    {
        if (SelectedDayTour is null)
        {
            return;
        }

        var toRemove = _allTours.FirstOrDefault(t => t.Id == SelectedDayTour.TourId);
        if (toRemove is null)
        {
            return;
        }

        _allTours.Remove(toRemove);
        await _repository.SaveAsync(_allTours);

        var keepDate = SelectedDay?.Date ?? DateTime.Today;
        BuildCalendarRange(keepDate);
    }

    private void SelectDayByDate(DateTime date)
    {
        var match = _interactiveDays.FirstOrDefault(day => day.Date == date.Date);
        if (match is not null)
        {
            SelectedDay = match;
        }
    }

    private void UpdateDaySelectionFlags()
    {
        foreach (var day in _interactiveDays)
        {
            day.IsSelected = SelectedDay?.Date == day.Date;
        }
    }

    private void SyncSelectedUpcomingCardFromSelectedDay()
    {
        if (SelectedDay?.Date is not DateTime selectedDate)
        {
            return;
        }

        foreach (var card in UpcomingDayCards)
        {
            card.IsSelected = card.Date == selectedDate;
        }

        var match = UpcomingDayCards.FirstOrDefault(card => card.Date == selectedDate);
        if (!ReferenceEquals(_selectedUpcomingDay, match))
        {
            _selectedUpcomingDay = match;
            OnPropertyChanged(nameof(SelectedUpcomingDay));
        }
    }

    private static int ParseStartTimeMinutes(string? raw)
    {
        var value = NormalizeStartTime(raw);
        return TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out var parsed)
            ? (int)parsed.TotalMinutes
            : int.MaxValue;
    }

    private static string NormalizeStartTime(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed.ToString("hh\\:mm", CultureInfo.InvariantCulture);
        }

        return string.IsNullOrWhiteSpace(text) ? "--:--" : text;
    }

    private static string BuildTourSummary(TourRecord tour, bool includeName)
    {
        var stopCount = tour.Stops.Count(s => !TourStopIdentity.IsCompanyStop(s));
        var lead = includeName
            ? $"{NormalizeStartTime(tour.StartTime)} {tour.Name}".Trim()
            : $"{NormalizeStartTime(tour.StartTime)}";
        return $"{lead} ({stopCount} Stopps)";
    }

    private Dictionary<DateTime, CalendarDayLoad> BuildDayLoadByDate()
    {
        return _allTours
            .Select(t => new { Tour = t, Date = ParseTourDate(t.Date) })
            .Where(x => x.Date is not null)
            .GroupBy(x => x.Date!.Value.Date)
            .ToDictionary(
                g => g.Key,
                g => new CalendarDayLoad(
                    g.Count(),
                    g.Sum(x => GetAssignedPeopleCount(x.Tour))));
    }

    private void ApplyDayLoadAppearance(CalendarLoadItem item, int assignedPeopleCount)
    {
        item.LoadBackground = string.Empty;
        item.LoadBorderBrush = string.Empty;
        item.LoadForeground = string.Empty;

        if (assignedPeopleCount >= _calendarLoadCriticalPeopleThreshold)
        {
            item.LoadBackground = _calendarLoadCriticalColor;
            item.LoadBorderBrush = _calendarLoadCriticalColor;
            item.LoadForeground = "#FFFFFF";
            return;
        }

        if (assignedPeopleCount >= _calendarLoadWarningPeopleThreshold)
        {
            item.LoadBackground = _calendarLoadWarningColor;
            item.LoadBorderBrush = _calendarLoadWarningColor;
            item.LoadForeground = "#FFFFFF";
        }
    }

    private bool CanOpenSelectedDayInTours()
    {
        return SelectedDay?.Date is not null && _openDayInToursAsync is not null;
    }

    private void UpdateStatusText()
    {
        var rangeStart = new DateTime(_rangeStartMonth.Year, _rangeStartMonth.Month, 1);
        var rangeEnd = rangeStart.AddMonths(2).AddDays(-1);

        var inRange = _allTours.Count(t =>
        {
            var date = ParseTourDate(t.Date);
            return date is not null && date.Value.Date >= rangeStart && date.Value.Date <= rangeEnd;
        });

        var uniqueDays = _allTours
            .Select(t => ParseTourDate(t.Date))
            .Where(d => d is not null && d.Value.Date >= rangeStart && d.Value.Date <= rangeEnd)
            .Select(d => d!.Value.Date)
            .Distinct()
            .Count();

        StatusText = $"Zeitraum: {inRange} Tour(en) auf {uniqueDays} Tag(en).";
    }

    private void RaiseCommandStates()
    {
        if (OpenSelectedTourCommand is AsyncCommand openTour)
        {
            openTour.RaiseCanExecuteChanged();
        }

        if (DeleteSelectedTourCommand is AsyncCommand deleteTour)
        {
            deleteTour.RaiseCanExecuteChanged();
        }

        if (OpenSelectedDayInToursCommand is AsyncCommand openDay)
        {
            openDay.RaiseCanExecuteChanged();
        }
    }

    private static DateTime? ParseTourDate(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var format in SupportedDateFormats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            {
                return exact.Date;
            }
        }

        if (DateTime.TryParse(value, UiCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    private static int GetAssignedPeopleCount(TourRecord tour)
    {
        return (tour.EmployeeIds ?? [])
            .Count(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 7 && normalized.StartsWith('#') ? normalized.ToUpperInvariant() : fallback;
    }

    private static string BuildWeekLabel(DateTime date)
    {
        var week = UiCulture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        var weekStart = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(6);
        return $"KW {week} · {weekStart:dd.MM} - {weekEnd:dd.MM}";
    }
}

public sealed class CalendarMonthItem
{
    public string MonthTitle { get; set; } = string.Empty;

    public ObservableCollection<CalendarDayItem> DayCells { get; } = [];
}

public abstract class CalendarLoadItem : ObservableObject
{
    private string _loadBackground = "#FFFFFF";
    private string _loadBorderBrush = "#D8DEE7";
    private string _loadForeground = "#0F172A";

    public string LoadBackground
    {
        get => _loadBackground;
        set => SetProperty(ref _loadBackground, value);
    }

    public string LoadBorderBrush
    {
        get => _loadBorderBrush;
        set => SetProperty(ref _loadBorderBrush, value);
    }

    public string LoadForeground
    {
        get => _loadForeground;
        set => SetProperty(ref _loadForeground, value);
    }
}

public sealed class CalendarDayItem : CalendarLoadItem
{
    private bool _isSelected;

    public bool IsPlaceholder { get; set; }

    public DateTime? Date { get; set; }

    public string DayLabel { get; set; } = string.Empty;

    public int TourCount { get; set; }

    public int AssignedPeopleCount { get; set; }

    public bool IsToday { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool HasTours => TourCount > 0;

    public string TourCountBadge => TourCount.ToString(CultureInfo.InvariantCulture);
}

public sealed class CalendarTourItem
{
    public int TourId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string StartTime { get; set; } = string.Empty;

    public string VehicleId { get; set; } = string.Empty;

    public string Employees { get; set; } = string.Empty;

    public int StopCount { get; set; }

    public string Summary { get; set; } = string.Empty;
}

public sealed class UpcomingDayCardItem : CalendarLoadItem
{
    private bool _isSelected;

    public DateTime Date { get; set; }

    public string DateText { get; set; } = string.Empty;

    public string TourCountText { get; set; } = string.Empty;

    public string SummaryText { get; set; } = string.Empty;

    public bool IsToday { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record CalendarDayLoad(int TourCount, int AssignedPeopleCount);
