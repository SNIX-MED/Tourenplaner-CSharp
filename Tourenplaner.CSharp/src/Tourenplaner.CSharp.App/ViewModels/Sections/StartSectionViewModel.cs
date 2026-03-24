using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class StartSectionViewModel : SectionViewModelBase
{
    private static readonly CultureInfo UiCulture = new("de-CH");
    private static readonly string[] SupportedDateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];
    private const int PreviewDayCount = 21;

    private readonly JsonToursRepository _tourRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly Func<Task>? _openMapAsync;
    private readonly string _bannerImagePath;
    private string _statusText = "Startseite wird geladen...";
    private string _calendarHeadline = $"Kalender der nächsten {PreviewDayCount} Tage";
    private string _calendarSubtitle = "Drei Wochen im Überblick, gruppiert nach Kalenderwochen.";
    private string _dashboardSummary = "Tourenübersicht wird geladen...";
    private string _nextPlannedDayText = "Noch kein Tourtag geplant";

    public StartSectionViewModel(
        string toursJsonPath,
        string settingsJsonPath,
        string bannerImagePath,
        Func<Task>? openMapAsync = null)
        : base("Start", "Schneller Einstieg in die Tourenplanung.")
    {
        _tourRepository = new JsonToursRepository(toursJsonPath);
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _openMapAsync = openMapAsync;
        _bannerImagePath = bannerImagePath;

        NewTourPlanCommand = new AsyncCommand(OpenMapAsync);
    }

    public ObservableCollection<UpcomingDayCardItem> UpcomingDayCards { get; } = [];

    public ObservableCollection<StartWeekGroupItem> UpcomingWeeks { get; } = [];

    public ICommand NewTourPlanCommand { get; }

    public string BannerImagePath => _bannerImagePath;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CalendarHeadline
    {
        get => _calendarHeadline;
        private set => SetProperty(ref _calendarHeadline, value);
    }

    public string CalendarSubtitle
    {
        get => _calendarSubtitle;
        private set => SetProperty(ref _calendarSubtitle, value);
    }

    public string DashboardSummary
    {
        get => _dashboardSummary;
        private set => SetProperty(ref _dashboardSummary, value);
    }

    public string NextPlannedDayText
    {
        get => _nextPlannedDayText;
        private set => SetProperty(ref _nextPlannedDayText, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var settingsTask = _settingsRepository.LoadAsync(cancellationToken);
        var toursTask = _tourRepository.LoadAsync(cancellationToken);
        await Task.WhenAll(settingsTask, toursTask);

        var settings = await settingsTask;
        var warningColor = NormalizeHexColor(settings.CalendarLoadWarningColor, AppSettings.DefaultCalendarLoadWarningColor);
        var criticalColor = NormalizeHexColor(settings.CalendarLoadCriticalColor, AppSettings.DefaultCalendarLoadCriticalColor);
        var warningThreshold = settings.CalendarLoadWarningPeopleThreshold < 1 ? 1 : settings.CalendarLoadWarningPeopleThreshold;
        var criticalThreshold = settings.CalendarLoadCriticalPeopleThreshold < warningThreshold
            ? warningThreshold
            : settings.CalendarLoadCriticalPeopleThreshold;

        var tours = await toursTask;
        var toursByDate = tours
            .Select(t => new { Tour = t, Date = ParseTourDate(t.Date) })
            .Where(x => x.Date is not null)
            .GroupBy(x => x.Date!.Value.Date)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Tour)
                    .OrderBy(t => ParseStartTimeMinutes(t.StartTime))
                    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        UpcomingDayCards.Clear();
        UpcomingWeeks.Clear();
        StartWeekGroupItem? currentWeek = null;
        for (var i = 0; i < PreviewDayCount; i++)
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
                    : string.Join(" | ", toursForDay.Take(2).Select(BuildTourSummary)),
                IsToday = date == DateTime.Today
            };

            ApplyDayLoadAppearance(card, assignedPeopleCount, warningThreshold, criticalThreshold, warningColor, criticalColor);
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

        var plannedCards = UpcomingDayCards
            .Where(x => !string.Equals(x.TourCountText, "0 geplante Tour(en)", StringComparison.Ordinal))
            .ToList();
        var plannedDayCount = plannedCards.Count;
        var plannedTourCount = plannedCards.Sum(x => toursByDate.TryGetValue(x.Date, out var toursForDay) ? toursForDay.Count : 0);
        var nextPlannedDay = plannedCards.FirstOrDefault();

        CalendarHeadline = $"Kalender der nächsten {PreviewDayCount} Tage";
        CalendarSubtitle = "Drei Wochen im Überblick, gruppiert nach Kalenderwochen.";
        DashboardSummary = plannedDayCount == 0
            ? $"In den nächsten {PreviewDayCount} Tagen ist aktuell keine Tour geplant."
            : $"{plannedTourCount} Tour(en) an {plannedDayCount} Tag(en) in den nächsten {PreviewDayCount} Tagen.";
        NextPlannedDayText = nextPlannedDay is null
            ? "Noch kein Tourtag geplant"
            : $"Nächster geplanter Tag: {nextPlannedDay.Date:dddd, dd.MM.yyyy}";
        StatusText = plannedDayCount == 0
            ? $"In den nächsten {PreviewDayCount} Tagen ist aktuell keine Tour geplant."
            : $"In den nächsten {PreviewDayCount} Tagen sind an {plannedDayCount} Tag(en) Touren eingeplant.";
    }

    private async Task OpenMapAsync()
    {
        if (_openMapAsync is null)
        {
            return;
        }

        await _openMapAsync();
    }

    private static void ApplyDayLoadAppearance(
        CalendarLoadItem item,
        int assignedPeopleCount,
        int warningThreshold,
        int criticalThreshold,
        string warningColor,
        string criticalColor)
    {
        item.LoadBackground = "#FFFFFF";
        item.LoadBorderBrush = "#D8DEE7";
        item.LoadForeground = "#0F172A";

        if (assignedPeopleCount >= criticalThreshold)
        {
            item.LoadBackground = criticalColor;
            item.LoadBorderBrush = criticalColor;
            item.LoadForeground = "#FFFFFF";
            return;
        }

        if (assignedPeopleCount >= warningThreshold)
        {
            item.LoadBackground = warningColor;
            item.LoadBorderBrush = warningColor;
            item.LoadForeground = "#FFFFFF";
        }
    }

    private static int GetAssignedPeopleCount(TourRecord tour)
    {
        return (tour.EmployeeIds ?? []).Count(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string BuildTourSummary(TourRecord tour)
    {
        var stopCount = tour.Stops.Count(s => !TourStopIdentity.IsCompanyStop(s));
        return $"{NormalizeStartTime(tour.StartTime)} {tour.Name} ({stopCount} Stopps)".Trim();
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

        return DateTime.TryParse(value, UiCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed.Date
            : null;
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

public sealed class StartWeekGroupItem
{
    public string Label { get; set; } = string.Empty;

    public ObservableCollection<UpcomingDayCardItem> Days { get; } = [];
}
