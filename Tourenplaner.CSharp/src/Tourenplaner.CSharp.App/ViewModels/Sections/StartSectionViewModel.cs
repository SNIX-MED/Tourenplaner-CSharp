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

public sealed class StartSectionViewModel : SectionViewModelBase
{
    private static readonly CultureInfo UiCulture = new("de-CH");
    private static readonly string[] SupportedDateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];
    private const int PreviewDayCount = 14;

    private readonly JsonToursRepository _tourRepository;
    private readonly JsonCalendarManualEntryRepository _manualEntryRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly Func<Task>? _openMapAsync;
    private readonly string _bannerImagePath;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly List<CalendarManualEntry> _manualEntries = [];
    private string _statusText = "Startseite wird geladen...";
    private string _calendarHeadline = "Kalender";
    private string _dashboardSummary = "Tourenuebersicht wird geladen...";
    private string _nextPlannedDayText = "Noch kein Eintragstag geplant";

    public StartSectionViewModel(
        string toursJsonPath,
        string settingsJsonPath,
        string bannerImagePath,
        Func<Task>? openMapAsync = null,
        AppDataSyncService? dataSyncService = null)
        : base("Start", "Schneller Einstieg in die Tourenplanung.")
    {
        _tourRepository = new JsonToursRepository(toursJsonPath);
        var manualEntriesPath = Path.Combine(Path.GetDirectoryName(toursJsonPath) ?? string.Empty, "kalender-manuelle-eintraege.json");
        _manualEntryRepository = new JsonCalendarManualEntryRepository(manualEntriesPath);
        _settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _dataSyncService = dataSyncService ?? new AppDataSyncService();
        _openMapAsync = openMapAsync;
        _bannerImagePath = bannerImagePath;

        NewTourPlanCommand = new AsyncCommand(OpenMapAsync);
        _dataSyncService.DataChanged += OnDataChanged;
    }

    public ObservableCollection<UpcomingDayCardItem> UpcomingDayCards { get; } = [];

    // Kept for compatibility with shared models used by other sections.
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
        var manualEntriesTask = _manualEntryRepository.LoadAsync(cancellationToken);
        await Task.WhenAll(settingsTask, toursTask, manualEntriesTask);

        var settings = await settingsTask;
        var warningColor = NormalizeHexColor(settings.CalendarLoadWarningColor, AppSettings.DefaultCalendarLoadWarningColor);
        var criticalColor = NormalizeHexColor(settings.CalendarLoadCriticalColor, AppSettings.DefaultCalendarLoadCriticalColor);
        var warningThreshold = settings.CalendarLoadWarningPeopleThreshold < 1 ? 1 : settings.CalendarLoadWarningPeopleThreshold;
        var criticalThreshold = settings.CalendarLoadCriticalPeopleThreshold < warningThreshold
            ? warningThreshold
            : settings.CalendarLoadCriticalPeopleThreshold;

        var tours = await toursTask;
        _manualEntries.Clear();
        _manualEntries.AddRange(await manualEntriesTask);

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

        var manualByDate = _manualEntries
            .Select(x => new { Entry = x, Date = ParseManualEntryDate(x.Date) })
            .Where(x => x.Date is not null)
            .GroupBy(x => x.Date!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Entry).ToList());

        UpcomingDayCards.Clear();
        UpcomingWeeks.Clear();

        var daysWithEntries = 0;
        var totalTours = 0;
        var totalManualEntries = 0;
        DateTime? nextEntryDay = null;

        for (var i = 0; i < PreviewDayCount; i++)
        {
            var date = DateTime.Today.AddDays(i).Date;
            toursByDate.TryGetValue(date, out var toursForDay);
            toursForDay ??= [];

            manualByDate.TryGetValue(date, out var manualForDay);
            manualForDay ??= [];

            var assignedPeopleCount = toursForDay.Sum(GetAssignedPeopleCount);
            var agendaEntries = BuildAgendaEntriesForDate(toursForDay, manualForDay);
            var entryCount = agendaEntries.Count;
            if (entryCount > 0)
            {
                daysWithEntries++;
                totalTours += toursForDay.Count;
                totalManualEntries += manualForDay.Count;
                nextEntryDay ??= date;
            }

            var card = new UpcomingDayCardItem
            {
                Date = date,
                DateText = date.ToString("dd.MM.yyyy", UiCulture),
                TourCountText = $"{toursForDay.Count} Tour(en), {manualForDay.Count} manuell",
                SummaryLines = BuildStartSummaryLines(toursForDay, manualForDay),
                SummaryText = BuildStartCardSummary(toursForDay, manualForDay),
                IsToday = date == DateTime.Today
            };

            ApplyDayLoadAppearance(card, assignedPeopleCount, warningThreshold, criticalThreshold, warningColor, criticalColor);
            UpcomingDayCards.Add(card);
        }

        var displayedDayCount = UpcomingDayCards.Count;
        CalendarHeadline = "Kalender";
        DashboardSummary = daysWithEntries == 0
            ? $"In den naechsten {displayedDayCount} Tagen ist aktuell kein Eintrag geplant."
            : $"{totalTours} Tour(en), {totalManualEntries} manueller Eintrag/Eintraege an {daysWithEntries} Tag(en) in den naechsten {displayedDayCount} Tagen.";
        NextPlannedDayText = nextEntryDay is null
            ? "Noch kein Eintragstag geplant"
            : $"Naechster Eintragstag: {nextEntryDay.Value:dddd, dd.MM.yyyy}";
        StatusText = daysWithEntries == 0
            ? $"In den naechsten {displayedDayCount} Tagen ist aktuell kein Eintrag geplant."
            : $"In den naechsten {displayedDayCount} Tagen sind an {daysWithEntries} Tag(en) Eintraege eingeplant.";
    }

    private async Task OpenMapAsync()
    {
        if (_openMapAsync is null)
        {
            return;
        }

        await _openMapAsync();
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId || !args.Kinds.HasFlag(AppDataKind.Tours))
        {
            return;
        }

        _ = RefreshAsync();
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

    private static string BuildManualSummary(CalendarManualEntry entry)
    {
        var title = string.IsNullOrWhiteSpace(entry.Title) ? "(Ohne Titel)" : entry.Title.Trim();
        if (TryNormalizeManualTime(entry.Time, out var normalized))
        {
            return $"{normalized} {title}";
        }

        return title;
    }

    private static List<string> BuildAgendaEntriesForDate(
        IReadOnlyList<TourRecord> toursForDay,
        IReadOnlyList<CalendarManualEntry> manualForDay)
    {
        var entries = new List<(int Sort, bool IsManual, string Text)>();

        foreach (var tour in toursForDay)
        {
            entries.Add((ParseStartTimeMinutes(tour.StartTime), false, BuildTourSummary(tour)));
        }

        foreach (var manual in manualForDay)
        {
            var hasTime = TryNormalizeManualTime(manual.Time, out var normalizedTime);
            entries.Add((hasTime ? ParseStartTimeMinutes(normalizedTime) : int.MaxValue, true, BuildManualSummary(manual)));
        }

        return entries
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.IsManual)
            .Select(x => x.Text)
            .ToList();
    }

    private static string BuildStartCardSummary(
        IReadOnlyList<TourRecord> toursForDay,
        IReadOnlyList<CalendarManualEntry> manualForDay)
    {
        var lines = BuildStartSummaryLines(toursForDay, manualForDay);
        return string.Join(" | ", lines.Select(x => x.Text));
    }

    private static IReadOnlyList<UpcomingDaySummaryLine> BuildStartSummaryLines(
        IReadOnlyList<TourRecord> toursForDay,
        IReadOnlyList<CalendarManualEntry> manualForDay)
    {
        var tourSummaries = toursForDay
            .OrderBy(t => ParseStartTimeMinutes(t.StartTime))
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildTourSummary)
            .ToList();

        var manualSummaries = manualForDay
            .Select(x =>
            {
                var hasTime = TryNormalizeManualTime(x.Time, out var normalizedTime);
                return new
                {
                    Text = BuildManualSummary(x),
                    Sort = hasTime ? ParseStartTimeMinutes(normalizedTime) : int.MaxValue,
                    Color = NormalizeHexColor(x.ColorHex, "#7DD3FC")
                };
            })
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tourSummaries.Count == 0 && manualSummaries.Count == 0)
        {
            return
            [
                new UpcomingDaySummaryLine
                {
                    Text = "Keine Eintraege.",
                    ShowManualDot = false
                }
            ];
        }

        var lines = new List<UpcomingDaySummaryLine>();
        if (tourSummaries.Count > 0)
        {
            lines.Add(new UpcomingDaySummaryLine
            {
                Text = tourSummaries[0],
                ShowManualDot = false
            });
        }

        if (manualSummaries.Count > 0)
        {
            lines.Add(new UpcomingDaySummaryLine
            {
                Text = manualSummaries[0].Text,
                ShowManualDot = true,
                DotColor = manualSummaries[0].Color
            });
        }

        if (lines.Count == 0 && tourSummaries.Count > 1)
        {
            lines.Add(new UpcomingDaySummaryLine
            {
                Text = tourSummaries[1],
                ShowManualDot = false
            });
        }

        if (lines.Count == 0)
        {
            lines.Add(new UpcomingDaySummaryLine
            {
                Text = manualSummaries[0].Text,
                ShowManualDot = true,
                DotColor = manualSummaries[0].Color
            });
        }

        return lines.Take(2).ToList();
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

    private static bool TryNormalizeManualTime(string? raw, out string normalized)
    {
        var value = (raw ?? string.Empty).Trim();
        if (TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out var exact))
        {
            normalized = exact.ToString("hh\\:mm", CultureInfo.InvariantCulture);
            return true;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            normalized = parsed.ToString("hh\\:mm", CultureInfo.InvariantCulture);
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 7 && normalized.StartsWith('#') ? normalized.ToUpperInvariant() : fallback;
    }
}

public sealed class StartWeekGroupItem
{
    public string Label { get; set; } = string.Empty;

    public ObservableCollection<UpcomingDayCardItem> Days { get; } = [];
}
