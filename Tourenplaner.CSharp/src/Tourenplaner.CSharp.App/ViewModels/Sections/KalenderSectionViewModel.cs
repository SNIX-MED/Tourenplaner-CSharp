using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class KalenderSectionViewModel : SectionViewModelBase
{
    private static readonly CultureInfo UiCulture = new("de-CH");
    private static readonly string[] SupportedDateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];
    private const int PreviewWeekCount = 4;
    private const int PreviewNavigationMonths = 6;

    private readonly JsonToursRepository _repository;
    private readonly JsonOrderRepository _orderRepository;
    private readonly JsonCalendarManualEntryRepository _manualEntryRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly Func<int, Task>? _openTourAsync;
    private readonly Func<int, Task>? _openTourOnMapAsync;
    private readonly Func<DateTime, Task>? _openDayInToursAsync;
    private readonly Func<string, Task>? _openOrderEditorAsync;
    private Func<Task>? _openSplitScreenAsync;
    private readonly List<TourRecord> _allTours = [];
    private readonly List<Order> _allOrders = [];
    private readonly List<CalendarManualEntry> _manualEntries = [];
    private readonly List<CalendarDayItem> _interactiveDays = [];
    private readonly Guid _instanceId = Guid.NewGuid();

    private DateTime _rangeStartMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _upcomingWeeksStartDate = GetStartOfWeek(DateTime.Today);
    private DateTime _upcomingWeeksMinStartDate = GetStartOfWeek(DateTime.Today.AddMonths(-PreviewNavigationMonths));
    private DateTime _upcomingWeeksMaxStartDate = GetStartOfWeek(DateTime.Today.AddMonths(PreviewNavigationMonths));
    private string _rangeTitleText = string.Empty;
    private string _statusText = "Kalender wird geladen...";
    private string _selectedDayHeadline = "Ausgewählter Tag";
    private string _calendarLoadWarningColor = AppSettings.DefaultCalendarLoadWarningColor;
    private string _calendarLoadCriticalColor = AppSettings.DefaultCalendarLoadCriticalColor;
    private int _calendarLoadWarningPeopleThreshold = 1;
    private int _calendarLoadCriticalPeopleThreshold = 2;
    private CalendarDayItem? _selectedDay;
    private CalendarTourItem? _selectedDayTour;
    private UpcomingDayCardItem? _selectedUpcomingDay;

    public KalenderSectionViewModel(
        string toursJsonPath,
        string ordersJsonPath,
        string settingsJsonPath,
        Func<int, Task>? openTourAsync = null,
        Func<int, Task>? openTourOnMapAsync = null,
        Func<DateTime, Task>? openDayInToursAsync = null,
        Func<string, Task>? openOrderEditorAsync = null,
        Func<Task>? openSplitScreenAsync = null,
        AppDataSyncService? dataSyncService = null)
        : base("Kalender", "Übersicht aller geplanten Touren. Ein Doppelklick öffnet den Tag in den Liefertouren.")
    {
        _repository = new JsonToursRepository(toursJsonPath);
        _orderRepository = new JsonOrderRepository(ordersJsonPath);
        var manualEntriesPath = Path.Combine(Path.GetDirectoryName(toursJsonPath) ?? string.Empty, "kalender-manuelle-eintraege.json");
        _manualEntryRepository = new JsonCalendarManualEntryRepository(manualEntriesPath);
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _dataSyncService = dataSyncService ?? new AppDataSyncService();
        _openTourAsync = openTourAsync;
        _openTourOnMapAsync = openTourOnMapAsync;
        _openDayInToursAsync = openDayInToursAsync;
        _openOrderEditorAsync = openOrderEditorAsync;
        _openSplitScreenAsync = openSplitScreenAsync;

        PreviousRangeCommand = new DelegateCommand(ShowPreviousRange);
        NextRangeCommand = new DelegateCommand(ShowNextRange);
        PreviousWeekRangeCommand = new DelegateCommand(ShowPreviousWeekRange, CanShowPreviousWeekRange);
        NextWeekRangeCommand = new DelegateCommand(ShowNextWeekRange, CanShowNextWeekRange);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        OpenSelectedTourCommand = new AsyncCommand(OpenSelectedTourAsync, () => SelectedDayTour is not null);
        DeleteSelectedTourCommand = new AsyncCommand(DeleteSelectedTourAsync, () => SelectedDayTour is not null);
        OpenSplitScreenCommand = new AsyncCommand(OpenSplitScreenAsync, () => _openSplitScreenAsync is not null);
        _dataSyncService.DataChanged += OnDataChanged;

        _ = RefreshAsync();
    }

    public ObservableCollection<CalendarMonthItem> VisibleMonths { get; } = [];

    public ObservableCollection<CalendarTourItem> SelectedDayTours { get; } = [];
    public ObservableCollection<CalendarManualEntryItem> SelectedDayManualEntries { get; } = [];

    public ObservableCollection<UpcomingDayCardItem> UpcomingDayCards { get; } = [];
    public ObservableCollection<StartWeekGroupItem> UpcomingWeeks { get; } = [];

    public ICommand PreviousRangeCommand { get; }

    public ICommand NextRangeCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenSelectedTourCommand { get; }

    public ICommand DeleteSelectedTourCommand { get; }

    public ICommand OpenSplitScreenCommand { get; }
    public ICommand PreviousWeekRangeCommand { get; }

    public ICommand NextWeekRangeCommand { get; }

    public IReadOnlyList<string> ManualEntryColorOptions { get; } =
    [
        "#0EA5E9",
        "#16A34A",
        "#F59E0B",
        "#DC2626",
        "#7C3AED",
        "#475569"
    ];

    public string DefaultManualEntryColor => ManualEntryColorOptions[0];

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
        var ordersTask = _orderRepository.GetAllAsync();
        var manualEntriesTask = _manualEntryRepository.LoadAsync();
        await Task.WhenAll(settingsTask, toursTask, ordersTask, manualEntriesTask);

        var settings = await settingsTask;
        _calendarLoadWarningColor = NormalizeHexColor(settings.CalendarLoadWarningColor, AppSettings.DefaultCalendarLoadWarningColor);
        _calendarLoadCriticalColor = NormalizeHexColor(settings.CalendarLoadCriticalColor, AppSettings.DefaultCalendarLoadCriticalColor);
        _calendarLoadWarningPeopleThreshold = settings.CalendarLoadWarningPeopleThreshold < 1 ? 1 : settings.CalendarLoadWarningPeopleThreshold;
        _calendarLoadCriticalPeopleThreshold = settings.CalendarLoadCriticalPeopleThreshold < _calendarLoadWarningPeopleThreshold
            ? _calendarLoadWarningPeopleThreshold
            : settings.CalendarLoadCriticalPeopleThreshold;

        _allTours.Clear();
        _allTours.AddRange(await toursTask);
        _allOrders.Clear();
        _allOrders.AddRange(await ordersTask);
        _manualEntries.Clear();
        _manualEntries.AddRange(await manualEntriesTask);
        _upcomingWeeksMinStartDate = GetStartOfWeek(DateTime.Today.AddMonths(-PreviewNavigationMonths));
        _upcomingWeeksMaxStartDate = GetStartOfWeek(DateTime.Today.AddMonths(PreviewNavigationMonths));
        _upcomingWeeksStartDate = ClampUpcomingWeekStart(_upcomingWeeksStartDate);
        BuildCalendarRange(preserveSelectionDate: SelectedDay?.Date ?? DateTime.Today);
        RaiseWeekRangeCommandStates();
    }

    public void HandleDayDoubleClick()
    {
        if (CanOpenSelectedDayInTours())
        {
            _ = OpenSelectedDayInToursAsync();
        }
    }

    public async Task<ManualEntrySaveResult> SaveManualEntryAsync(
        DateTime date,
        string? time,
        string? title,
        string? description,
        string? colorHex)
    {
        var validation = ValidateManualEntryInput(time, title);
        if (!validation.Success)
        {
            return ManualEntrySaveResult.Fail(validation.Message);
        }

        var entry = new CalendarManualEntry
        {
            Id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Time = validation.NormalizedTime,
            Title = validation.NormalizedTitle,
            Description = (description ?? string.Empty).Trim(),
            ColorHex = NormalizeHexColor(colorHex, DefaultManualEntryColor)
        };

        _manualEntries.Add(entry);
        await _manualEntryRepository.SaveAsync(_manualEntries);
        _dataSyncService.PublishTours(_instanceId);

        BuildCalendarRange(date);
        SelectDayByDate(date);

        return ManualEntrySaveResult.Ok();
    }

    public async Task<ManualEntrySaveResult> UpdateManualEntryAsync(
        string? entryId,
        DateTime date,
        string? time,
        string? title,
        string? description,
        string? colorHex)
    {
        var normalizedId = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return ManualEntrySaveResult.Fail("Manueller Eintrag konnte nicht gefunden werden.");
        }

        var existing = _manualEntries.FirstOrDefault(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return ManualEntrySaveResult.Fail("Manueller Eintrag konnte nicht gefunden werden.");
        }

        var validation = ValidateManualEntryInput(time, title);
        if (!validation.Success)
        {
            return ManualEntrySaveResult.Fail(validation.Message);
        }

        existing.Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        existing.Time = validation.NormalizedTime;
        existing.Title = validation.NormalizedTitle;
        existing.Description = (description ?? string.Empty).Trim();
        existing.ColorHex = NormalizeHexColor(colorHex, DefaultManualEntryColor);

        await _manualEntryRepository.SaveAsync(_manualEntries);
        _dataSyncService.PublishTours(_instanceId);

        BuildCalendarRange(date);
        SelectDayByDate(date);

        return ManualEntrySaveResult.Ok();
    }

    public async Task<ManualEntrySaveResult> DeleteManualEntryAsync(string? entryId)
    {
        var normalizedId = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return ManualEntrySaveResult.Fail("Manueller Eintrag konnte nicht gefunden werden.");
        }

        var removed = _manualEntries.RemoveAll(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return ManualEntrySaveResult.Fail("Manueller Eintrag konnte nicht gefunden werden.");
        }

        await _manualEntryRepository.SaveAsync(_manualEntries);
        _dataSyncService.PublishTours(_instanceId);

        var focusDate = SelectedDay?.Date ?? DateTime.Today;
        BuildCalendarRange(focusDate);
        SelectDayByDate(focusDate);

        return ManualEntrySaveResult.Ok();
    }

    public IReadOnlyList<CalendarManualEntryEditItem> GetManualEntriesForDate(DateTime date)
    {
        return _manualEntries
            .Where(x => ParseManualEntryDate(x.Date)?.Date == date.Date)
            .Select(x =>
            {
                var hasTime = TryNormalizeManualTime(x.Time, out var normalizedTime);
                return new
                {
                    Entry = x,
                    HasTime = hasTime,
                    Time = hasTime ? normalizedTime : string.Empty
                };
            })
            .OrderBy(x => x.HasTime ? ParseStartTimeMinutes(x.Time) : int.MaxValue)
            .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CalendarManualEntryEditItem
            {
                Id = (x.Entry.Id ?? string.Empty).Trim(),
                Date = ParseManualEntryDate(x.Entry.Date) ?? date.Date,
                Time = x.Time,
                Title = (x.Entry.Title ?? string.Empty).Trim(),
                Description = (x.Entry.Description ?? string.Empty).Trim(),
                ColorHex = NormalizeHexColor(x.Entry.ColorHex, DefaultManualEntryColor)
            })
            .ToList();
    }

    public IReadOnlyList<CalendarTourDayListItem> GetToursForDate(DateTime date)
    {
        return _allTours
            .Where(t => ParseTourDate(t.Date)?.Date == date.Date)
            .OrderBy(t => ParseStartTimeMinutes(t.StartTime))
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t =>
            {
                var normalizedTime = NormalizeStartTime(t.StartTime);
                var normalizedName = string.IsNullOrWhiteSpace(t.Name)
                    ? $"Tour {t.Id.ToString(CultureInfo.InvariantCulture)}"
                    : t.Name.Trim();
                return new CalendarTourDayListItem
                {
                    TourId = t.Id,
                    Date = date.Date,
                    Time = normalizedTime == "--:--" ? string.Empty : normalizedTime,
                    Name = normalizedName,
                    Summary = BuildTourSummary(t, includeName: false)
                };
            })
            .ToList();
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

    private bool CanShowPreviousWeekRange() => _upcomingWeeksStartDate > _upcomingWeeksMinStartDate;

    private bool CanShowNextWeekRange() => _upcomingWeeksStartDate < _upcomingWeeksMaxStartDate;

    private void ShowPreviousWeekRange()
    {
        var next = ClampUpcomingWeekStart(_upcomingWeeksStartDate.AddDays(-7));
        if (next == _upcomingWeeksStartDate)
        {
            return;
        }

        _upcomingWeeksStartDate = next;
        BuildUpcomingCards();
        SyncSelectedUpcomingCardFromSelectedDay();
        RaiseWeekRangeCommandStates();
    }

    private void ShowNextWeekRange()
    {
        var next = ClampUpcomingWeekStart(_upcomingWeeksStartDate.AddDays(7));
        if (next == _upcomingWeeksStartDate)
        {
            return;
        }

        _upcomingWeeksStartDate = next;
        BuildUpcomingCards();
        SyncSelectedUpcomingCardFromSelectedDay();
        RaiseWeekRangeCommandStates();
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

            var agendaEntries = BuildAgendaEntriesForDate(date);
            var item = new CalendarDayItem
            {
                IsPlaceholder = false,
                Date = date,
                DayLabel = day.ToString(UiCulture),
                TourCount = dayLoad.TourCount,
                ManualEntryCount = agendaEntries.Count(x => x.IsManual),
                EntryCount = agendaEntries.Count,
                AssignedPeopleCount = dayLoad.AssignedPeopleCount,
                IsToday = date == DateTime.Today,
                PreviewEntries = agendaEntries
                    .Take(3)
                    .Select(x => new CalendarDayPreviewEntry
                    {
                        TimeText = x.TimeText,
                        Title = x.Title,
                        KindLabel = x.IsManual ? "Manuell" : "Tour",
                        AccentColor = x.ColorHex
                    })
                    .ToList(),
                TooltipEntries = agendaEntries
                    .Select(x => new CalendarDayTooltipEntry
                    {
                        TimeText = x.TimeText,
                        Title = x.Title,
                        Description = x.Description,
                        KindLabel = x.IsManual ? "Manuell" : "Tour",
                        AccentColor = x.ColorHex
                    })
                    .ToList(),
                MoreEntryCount = Math.Max(0, agendaEntries.Count - 3)
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

        var previewStartDate = _upcomingWeeksStartDate.Date;

        StartWeekGroupItem? currentWeek = null;
        for (var i = 0; i < PreviewWeekCount * 7; i++)
        {
            var date = previewStartDate.AddDays(i).Date;
            var toursForDay = _allTours
                .Where(t => ParseTourDate(t.Date)?.Date == date)
                .ToList();
            var dayEntries = BuildAgendaEntriesForDate(date);
            var assignedPeopleCount = toursForDay.Sum(GetAssignedPeopleCount);
            var weekdayShort = UiCulture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek).TrimEnd('.');

            var card = new UpcomingDayCardItem
            {
                Date = date,
                DateText = $"{weekdayShort} {date:dd.MM.yyyy}",
                TourCountText = $"{dayEntries.Count} Eintrag/Einträge",
                SummaryText = dayEntries.Count == 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, dayEntries.Select(x => x.CompactText)),
                IsToday = date == DateTime.Today,
                IsPast = date < DateTime.Today
            };
            ApplyDayLoadAppearance(card, assignedPeopleCount);
            UpcomingDayCards.Add(card);

            var weekLabel = BuildWeekLabel(date);
            if (currentWeek is null || !string.Equals(currentWeek.Label, weekLabel, StringComparison.Ordinal))
            {
                currentWeek = new StartWeekGroupItem
                {
                    Label = weekLabel
                };
                UpcomingWeeks.Add(currentWeek);
            }

            currentWeek.Days.Add(card);
        }
    }

    private DateTime ClampUpcomingWeekStart(DateTime value)
    {
        var normalized = GetStartOfWeek(value);
        if (normalized < _upcomingWeeksMinStartDate)
        {
            return _upcomingWeeksMinStartDate;
        }

        if (normalized > _upcomingWeeksMaxStartDate)
        {
            return _upcomingWeeksMaxStartDate;
        }

        return normalized;
    }

    private void RaiseWeekRangeCommandStates()
    {
        if (PreviousWeekRangeCommand is DelegateCommand previous)
        {
            previous.RaiseCanExecuteChanged();
        }

        if (NextWeekRangeCommand is DelegateCommand next)
        {
            next.RaiseCanExecuteChanged();
        }
    }

    private void LoadSelectedDayTours()
    {
        SelectedDayTours.Clear();
        SelectedDayManualEntries.Clear();

        if (SelectedDay?.Date is not DateTime selectedDate)
        {
            SelectedDayHeadline = "Ausgewählter Tag";
            StatusText = "Kein Kalendertag ausgewählt.";
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
            var stops = BuildTourStopCards(tour);
            SelectedDayTours.Add(new CalendarTourItem
            {
                TourId = tour.Id,
                DateText = selectedDate.ToString("dd.MM.yyyy", UiCulture),
                Name = tour.Name,
                StartTime = NormalizeStartTime(tour.StartTime),
                VehicleId = BuildVehicleSummary(tour),
                Employees = string.Join(", ", tour.EmployeeIds),
                StopCount = tour.Stops.Count(s => !TourStopIdentity.IsCompanyStop(s)),
                Summary = BuildTourSummary(tour, includeName: false),
                Stops = stops
            });
        }

        var dayManualEntries = _manualEntries
            .Where(x => ParseManualEntryDate(x.Date)?.Date == selectedDate)
            .Select(x =>
            {
                var hasTime = TryNormalizeManualTime(x.Time, out var normalizedTime);
                return new
                {
                    Entry = x,
                    HasTime = hasTime,
                    Time = hasTime ? normalizedTime : string.Empty
                };
            })
            .OrderBy(x => x.HasTime ? ParseStartTimeMinutes(x.Time) : int.MaxValue)
            .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CalendarManualEntryItem
            {
                EntryId = x.Entry.Id,
                TimeText = x.HasTime ? x.Time : "--:--",
                Title = x.Entry.Title,
                Description = x.Entry.Description,
                ColorHex = NormalizeHexColor(x.Entry.ColorHex, "#0EA5E9")
            })
            .ToList();

        foreach (var manualEntry in dayManualEntries)
        {
            SelectedDayManualEntries.Add(manualEntry);
        }

        SelectedDayTour = SelectedDayTours.FirstOrDefault();
        SelectedDayHeadline = $"Einträge am {selectedDate.ToString("dddd, dd.MM.yyyy", UiCulture)}";

        if (SelectedDayTours.Count == 0 && SelectedDayManualEntries.Count == 0)
        {
            StatusText = $"{selectedDate:dd.MM.yyyy}: kein Eintrag.";
        }
        else
        {
            StatusText = $"{selectedDate:dd.MM.yyyy}: {SelectedDayTours.Count} Tour(en), {SelectedDayManualEntries.Count} manueller Eintrag/Einträge.";
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

    private static string BuildVehicleSummary(TourRecord tour)
    {
        var assignments = new List<(string VehicleId, string TrailerId)>
        {
            ((tour.VehicleId ?? string.Empty).Trim(), (tour.TrailerId ?? string.Empty).Trim()),
            ((tour.SecondaryVehicleId ?? string.Empty).Trim(), (tour.SecondaryTrailerId ?? string.Empty).Trim())
        }
        .Where(x => !string.IsNullOrWhiteSpace(x.VehicleId) || !string.IsNullOrWhiteSpace(x.TrailerId))
        .GroupBy(x => $"{x.VehicleId}|{x.TrailerId}", StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();

        var lines = assignments
            .Select(x => $"{(string.IsNullOrWhiteSpace(x.VehicleId) ? "-" : x.VehicleId)} & {(string.IsNullOrWhiteSpace(x.TrailerId) ? "-" : x.TrailerId)}")
            .ToList();

        return lines.Count == 0 ? "-" : string.Join(Environment.NewLine, lines);
    }

    public async Task OpenOrderEditorAsync(string? orderId)
    {
        var normalized = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || _openOrderEditorAsync is null)
        {
            return;
        }

        await _openOrderEditorAsync(normalized);
    }

    private async Task OpenSelectedTourAsync()
    {
        if (SelectedDayTour is null || _openTourAsync is null)
        {
            return;
        }

        await _openTourAsync(SelectedDayTour.TourId);
    }

    public async Task OpenTourOnMapAsync(int tourId)
    {
        if (_openTourOnMapAsync is null || tourId <= 0)
        {
            return;
        }

        await _openTourOnMapAsync(tourId);
    }

    public bool NavigateSelectedDay(int deltaDays)
    {
        if (SelectedDay?.Date is not DateTime selectedDate)
        {
            return false;
        }

        var target = selectedDate.AddDays(deltaDays).Date;
        var rangeStart = new DateTime(_rangeStartMonth.Year, _rangeStartMonth.Month, 1);
        var rangeEnd = rangeStart.AddMonths(2).AddDays(-1);
        if (target < rangeStart || target > rangeEnd)
        {
            _rangeStartMonth = new DateTime(target.Year, target.Month, 1);
            BuildCalendarRange(target);
            return true;
        }

        SelectDayByDate(target);
        return SelectedDay?.Date == target;
    }

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

        var tourLabel = string.IsNullOrWhiteSpace(toRemove.Name)
            ? $"Tour {toRemove.Id.ToString(CultureInfo.InvariantCulture)}"
            : toRemove.Name.Trim();
        var confirmDelete = System.Windows.MessageBox.Show(
            $"Soll {tourLabel} wirklich gelöscht werden?",
            "Tour löschen",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirmDelete != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _allTours.Remove(toRemove);
        await _repository.SaveAsync(_allTours);
        await ClearAssignedTourReferencesAsync(toRemove.Id);
        _dataSyncService.PublishTours(_instanceId, toRemove.Id.ToString(CultureInfo.InvariantCulture), null);
        _dataSyncService.PublishOrders(_instanceId);

        var keepDate = SelectedDay?.Date ?? DateTime.Today;
        BuildCalendarRange(keepDate);
    }

    private void SelectDayByDate(DateTime date)
    {
        var match = _interactiveDays.FirstOrDefault(day => day.Date == date.Date);
        if (match is not null)
        {
            SelectedDay = match;
            return;
        }

        var rangeStart = new DateTime(_rangeStartMonth.Year, _rangeStartMonth.Month, 1);
        var rangeEnd = rangeStart.AddMonths(2).AddDays(-1);
        if (date.Date < rangeStart || date.Date > rangeEnd)
        {
            _rangeStartMonth = new DateTime(date.Year, date.Month, 1);
            BuildCalendarRange(date);
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

    private static bool TryNormalizeManualTime(string? raw, out string normalized)
    {
        normalized = string.Empty;
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
        {
            normalized = parsed.ToString("hh\\:mm", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static ManualEntryValidationResult ValidateManualEntryInput(string? time, string? title)
    {
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return ManualEntryValidationResult.Fail("Bitte einen Titel für den manuellen Eintrag eingeben.");
        }

        if (!TryNormalizeManualTime(time, out var normalizedTime))
        {
            return ManualEntryValidationResult.Fail("Zeitformat ungültig. Bitte HH:mm verwenden (z. B. 08:30).");
        }

        return ManualEntryValidationResult.Ok(normalizedTitle, normalizedTime);
    }

    private List<CalendarAgendaEntry> BuildAgendaEntriesForDate(DateTime date)
    {
        var entries = new List<CalendarAgendaEntry>();
        foreach (var tour in _allTours
                     .Where(t => ParseTourDate(t.Date)?.Date == date.Date)
                     .OrderBy(t => ParseStartTimeMinutes(t.StartTime))
                     .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var startTime = NormalizeStartTime(tour.StartTime);
            var title = string.IsNullOrWhiteSpace(tour.Name)
                ? $"Tour {tour.Id.ToString(CultureInfo.InvariantCulture)}"
                : tour.Name.Trim();
            var stopCount = tour.Stops.Count(s => !TourStopIdentity.IsCompanyStop(s));
            var peopleCount = GetAssignedPeopleCount(tour);
            entries.Add(new CalendarAgendaEntry
            {
                IsManual = false,
                TimeText = startTime,
                Title = title,
                Description = $"{stopCount} Stopps · {peopleCount} zugewiesen",
                ColorHex = "#475569",
                SortTimeMinutes = ParseStartTimeMinutes(startTime)
            });
        }

        foreach (var manualEntry in _manualEntries
                     .Where(x => ParseManualEntryDate(x.Date)?.Date == date.Date))
        {
            var hasTime = TryNormalizeManualTime(manualEntry.Time, out var normalizedTime);
            entries.Add(new CalendarAgendaEntry
            {
                IsManual = true,
                TimeText = hasTime ? normalizedTime : "--:--",
                Title = string.IsNullOrWhiteSpace(manualEntry.Title) ? "(Ohne Titel)" : manualEntry.Title.Trim(),
                Description = (manualEntry.Description ?? string.Empty).Trim(),
                ColorHex = NormalizeHexColor(manualEntry.ColorHex, "#0EA5E9"),
                SortTimeMinutes = hasTime ? ParseStartTimeMinutes(normalizedTime) : int.MaxValue
            });
        }

        return entries
            .OrderBy(x => x.SortTimeMinutes)
            .ThenBy(x => x.IsManual)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildTourSummary(TourRecord tour, bool includeName)
    {
        var stopCount = tour.Stops.Count(s => !TourStopIdentity.IsCompanyStop(s));
        var lead = includeName
            ? $"{NormalizeStartTime(tour.StartTime)} {tour.Name}".Trim()
            : $"{NormalizeStartTime(tour.StartTime)}";
        return $"{lead} ({stopCount} Stopps)";
    }

    private List<CalendarTourStopCardItem> BuildTourStopCards(TourRecord tour)
    {
        var cards = new List<CalendarTourStopCardItem>();
        var letterIndex = 1;
        foreach (var stop in tour.Stops
                     .Where(s => !TourStopIdentity.IsCompanyStop(s))
                     .OrderBy(s => s.Order))
        {
            var stopLabel = ToStopLetter(letterIndex);
            letterIndex++;

            var order = ResolveOrderForStop(stop, tour.Id);
            cards.Add(new CalendarTourStopCardItem
            {
                StopLetter = stopLabel,
                OrderId = ResolveOrderId(order, stop),
                OrderAddress = FormatOrderAddress(order, stop),
                DeliveryAddress = FormatDeliveryAddress(order, stop),
                ProductLines = BuildProductLines(order, stop),
                WeightText = BuildWeightText(order, stop)
            });
        }

        return cards;
    }

    private Order? ResolveOrderForStop(TourStopRecord stop, int tourId)
    {
        var byNumber = (stop.Auftragsnummer ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(byNumber))
        {
            var byId = _allOrders.FirstOrDefault(o => string.Equals(o.Id, byNumber, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        var byStopId = (stop.Id ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(byStopId))
        {
            var byId = _allOrders.FirstOrDefault(o => string.Equals(o.Id, byStopId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        var tourKey = tourId.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(byNumber))
        {
            return _allOrders.FirstOrDefault(o =>
                string.Equals(o.AssignedTourId, tourKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.Id, byNumber, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string FormatOrderAddress(Order? order, TourStopRecord stop)
    {
        if (order is not null)
        {
            var line = string.Join(", ", new[]
            {
                (order.OrderAddress?.Name ?? string.Empty).Trim(),
                (order.OrderAddress?.Street ?? string.Empty).Trim(),
                string.Join(" ", new[]
                {
                    (order.OrderAddress?.PostalCode ?? string.Empty).Trim(),
                    (order.OrderAddress?.City ?? string.Empty).Trim()
                }.Where(x => !string.IsNullOrWhiteSpace(x)))
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return string.IsNullOrWhiteSpace(stop.Auftragsnummer) ? "-" : $"Auftrag {stop.Auftragsnummer}";
    }

    private static string FormatDeliveryAddress(Order? order, TourStopRecord stop)
    {
        if (order is not null)
        {
            var line = string.Join(", ", new[]
            {
                (order.DeliveryAddress?.Name ?? string.Empty).Trim(),
                (order.DeliveryAddress?.Street ?? string.Empty).Trim(),
                string.Join(" ", new[]
                {
                    (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim(),
                    (order.DeliveryAddress?.City ?? string.Empty).Trim()
                }.Where(x => !string.IsNullOrWhiteSpace(x)))
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return (stop.Address ?? string.Empty).Trim();
    }

    private static List<string> BuildProductLines(Order? order, TourStopRecord stop)
    {
        if (order?.Products is not null && order.Products.Count > 0)
        {
            return order.Products
                .Select(p =>
                {
                    var name = (p.Name ?? string.Empty).Trim();
                    var supplier = (p.Supplier ?? string.Empty).Trim();
                    var quantity = Math.Max(1, p.Quantity);
                    var weight = Math.Max(0d, p.WeightKg);
                    var supplierSuffix = string.IsNullOrWhiteSpace(supplier) ? string.Empty : $" [{supplier}]";
                    return string.IsNullOrWhiteSpace(name)
                        ? $"{quantity}x ({weight:0.##} kg)"
                        : $"{quantity}x {name}{supplierSuffix} ({weight:0.##} kg)";
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        var fallback = ParseStopWeight(stop.Gewicht);
        return fallback > 0
            ? [$"Gewicht: {fallback} kg"]
            : [];
    }

    private static string BuildWeightText(Order? order, TourStopRecord stop)
    {
        if (order?.Products is not null && order.Products.Count > 0)
        {
            var total = order.Products.Sum(p => Math.Max(0d, p.WeightKg));
            return $"Total: {total:0.##} kg";
        }

        var parsed = ParseStopWeight(stop.Gewicht);
        return parsed > 0 ? $"Total: {parsed:0.##} kg" : string.Empty;
    }

    private static string ResolveOrderId(Order? order, TourStopRecord stop)
    {
        if (!string.IsNullOrWhiteSpace(order?.Id))
        {
            return order.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(stop.Auftragsnummer))
        {
            return stop.Auftragsnummer.Trim();
        }

        return string.Empty;
    }

    private static double ParseStopWeight(string? raw)
    {
        var text = (raw ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0d, parsed)
            : 0d;
    }

    private static string ToStopLetter(int index)
    {
        // 1 -> A, 2 -> B, ... 26 -> Z, 27 -> AA
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
        item.LoadBackground = "#FFFFFF";
        item.LoadBorderBrush = "#D8DEE7";
        item.LoadForeground = "#0F172A";

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

        var toursInRange = _allTours.Count(t =>
        {
            var date = ParseTourDate(t.Date);
            return date is not null && date.Value.Date >= rangeStart && date.Value.Date <= rangeEnd;
        });

        var manualInRange = _manualEntries.Count(x =>
        {
            var date = ParseManualEntryDate(x.Date);
            return date is not null && date.Value.Date >= rangeStart && date.Value.Date <= rangeEnd;
        });

        var uniqueDays = _allTours
            .Select(t => ParseTourDate(t.Date))
            .Where(d => d is not null && d.Value.Date >= rangeStart && d.Value.Date <= rangeEnd)
            .Select(d => d!.Value.Date)
            .Concat(_manualEntries
                .Select(x => ParseManualEntryDate(x.Date))
                .Where(d => d is not null && d.Value.Date >= rangeStart && d.Value.Date <= rangeEnd)
                .Select(d => d!.Value.Date))
            .Distinct()
            .Count();

        StatusText = $"Zeitraum: {toursInRange} Tour(en), {manualInRange} manueller Eintrag/Einträge auf {uniqueDays} Tag(en).";
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
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId || !args.Kinds.HasFlag(AppDataKind.Tours))
        {
            return;
        }

        _ = RefreshAsync();
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

    private static DateTime? ParseManualEntryDate(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact.Date;
        }

        return ParseTourDate(value);
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

    private static DateTime GetStartOfWeek(DateTime date)
    {
        var daysFromWeekStart = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.Date.AddDays(-daysFromWeekStart);
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

    public int ManualEntryCount { get; set; }

    public int EntryCount { get; set; }

    public int MoreEntryCount { get; set; }

    public int AssignedPeopleCount { get; set; }

    public bool IsToday { get; set; }

    public IReadOnlyList<CalendarDayPreviewEntry> PreviewEntries { get; set; } = [];

    public IReadOnlyList<CalendarDayTooltipEntry> TooltipEntries { get; set; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool HasTours => TourCount > 0;

    public bool HasEntries => EntryCount > 0;

    public bool HasMoreEntries => MoreEntryCount > 0;

    public string TourCountBadge => TourCount.ToString(CultureInfo.InvariantCulture);

    public string EntryCountBadge => EntryCount.ToString(CultureInfo.InvariantCulture);
}

public sealed class CalendarDayPreviewEntry
{
    public string TimeText { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string KindLabel { get; set; } = string.Empty;

    public string AccentColor { get; set; } = "#475569";
}

public sealed class CalendarDayTooltipEntry
{
    public string TimeText { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string KindLabel { get; set; } = string.Empty;

    public string AccentColor { get; set; } = "#475569";
}

public sealed class CalendarTourItem
{
    public int TourId { get; set; }

    public string DateText { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string StartTime { get; set; } = string.Empty;

    public string VehicleId { get; set; } = string.Empty;

    public string Employees { get; set; } = string.Empty;

    public int StopCount { get; set; }

    public string Summary { get; set; } = string.Empty;

    public IReadOnlyList<CalendarTourStopCardItem> Stops { get; set; } = [];
}

public sealed class CalendarManualEntryItem
{
    public string EntryId { get; set; } = string.Empty;

    public string TimeText { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#0EA5E9";
}

public sealed class CalendarManualEntryEditItem
{
    public string Id { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.Today;

    public string Time { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#0EA5E9";
}

public sealed class CalendarTourDayListItem
{
    public int TourId { get; set; }

    public DateTime Date { get; set; } = DateTime.Today;

    public string Time { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}

public sealed class CalendarTourStopCardItem
{
    public string OrderId { get; set; } = string.Empty;

    public string StopLetter { get; set; } = string.Empty;

    public string OrderAddress { get; set; } = string.Empty;

    public string DeliveryAddress { get; set; } = string.Empty;

    public IReadOnlyList<string> ProductLines { get; set; } = [];

    public string WeightText { get; set; } = string.Empty;
}

public sealed class UpcomingDayCardItem : CalendarLoadItem
{
    private bool _isSelected;

    public DateTime Date { get; set; }

    public string DateText { get; set; } = string.Empty;

    public string TourCountText { get; set; } = string.Empty;

    public string SummaryText { get; set; } = string.Empty;

    public bool IsToday { get; set; }

    public bool IsPast { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record CalendarDayLoad(int TourCount, int AssignedPeopleCount);

internal sealed class CalendarAgendaEntry
{
    public bool IsManual { get; set; }

    public string TimeText { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#475569";

    public int SortTimeMinutes { get; set; } = int.MaxValue;

    public string CompactText => $"{TimeText} {Title}".Trim();
}

public sealed class ManualEntrySaveResult
{
    private ManualEntrySaveResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }

    public string Message { get; }

    public static ManualEntrySaveResult Ok()
    {
        return new ManualEntrySaveResult(true, string.Empty);
    }

    public static ManualEntrySaveResult Fail(string message)
    {
        return new ManualEntrySaveResult(false, (message ?? string.Empty).Trim());
    }
}

internal sealed class ManualEntryValidationResult
{
    private ManualEntryValidationResult(bool success, string message, string normalizedTitle, string normalizedTime)
    {
        Success = success;
        Message = message;
        NormalizedTitle = normalizedTitle;
        NormalizedTime = normalizedTime;
    }

    public bool Success { get; }

    public string Message { get; }

    public string NormalizedTitle { get; }

    public string NormalizedTime { get; }

    public static ManualEntryValidationResult Ok(string normalizedTitle, string normalizedTime)
    {
        return new ManualEntryValidationResult(true, string.Empty, normalizedTitle, normalizedTime);
    }

    public static ManualEntryValidationResult Fail(string message)
    {
        return new ManualEntryValidationResult(false, (message ?? string.Empty).Trim(), string.Empty, string.Empty);
    }
}
