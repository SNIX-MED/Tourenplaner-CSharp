using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class StartSectionViewModel : SectionViewModelBase
{
    private static readonly CultureInfo UiCulture = new("de-CH");
    private static readonly string[] SupportedDateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];
    private const int PreviewDayCount = 7;

    private readonly ITourRecordStore _tourRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IEmployeeDataStore _employeeRepository;
    private readonly IVehicleDataStore _vehicleRepository;
    private readonly ICalendarManualEntryStore _manualEntryRepository;
    private readonly IAppSettingsStore _settingsRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly Func<Task>? _openMapAsync;
    private readonly Func<Task>? _openToursAsync;
    private readonly Func<Task>? _openCalendarAsync;
    private readonly Func<Task>? _openOrdersAsync;
    private readonly Func<int, Task>? _openTourAsync;
    private readonly Func<Task>? _openEmployeesAsync;
    private readonly Func<Task>? _openVehiclesAsync;
    private readonly Func<string, OrderType, Task>? _openOrderAsync;
    private readonly string _bannerImagePath;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly List<CalendarManualEntry> _manualEntries = [];
    private string _statusText = "Startseite wird geladen...";
    private string _calendarHeadline = "Kalender";
    private string _dashboardSummary = "Tourenübersicht wird geladen...";
    private string _nextPlannedDayText = "Noch kein Eintragstag geplant";
    private string _todayText = string.Empty;
    private bool _hasTodayTours;
    private int _todayTourColumns = 1;
    private string _resourceSummary = "Verfügbarkeiten werden geladen...";
    private string _fixedDateSummary = "Keine anstehenden Fixtermine";

    public StartSectionViewModel(
        ITourRecordStore tourRepository,
        IOrderRepository orderRepository,
        IEmployeeDataStore employeeRepository,
        IVehicleDataStore vehicleRepository,
        ICalendarManualEntryStore manualEntryRepository,
        IAppSettingsStore settingsRepository,
        string bannerImagePath,
        Func<Task>? openMapAsync = null,
        Func<Task>? openToursAsync = null,
        Func<Task>? openCalendarAsync = null,
        Func<Task>? openOrdersAsync = null,
        Func<int, Task>? openTourAsync = null,
        Func<Task>? openEmployeesAsync = null,
        Func<Task>? openVehiclesAsync = null,
        Func<string, OrderType, Task>? openOrderAsync = null,
        AppDataSyncService? dataSyncService = null)
        : base("Start", "Schneller Einstieg in die Tourenplanung.")
    {
        _tourRepository = tourRepository;
        _orderRepository = orderRepository;
        _employeeRepository = employeeRepository;
        _vehicleRepository = vehicleRepository;
        _manualEntryRepository = manualEntryRepository;
        _settingsRepository = settingsRepository;
        _dataSyncService = dataSyncService ?? new AppDataSyncService();
        _openMapAsync = openMapAsync;
        _openToursAsync = openToursAsync;
        _openCalendarAsync = openCalendarAsync;
        _openOrdersAsync = openOrdersAsync;
        _openTourAsync = openTourAsync;
        _openEmployeesAsync = openEmployeesAsync;
        _openVehiclesAsync = openVehiclesAsync;
        _openOrderAsync = openOrderAsync;
        _bannerImagePath = bannerImagePath;

        NewTourPlanCommand = new AsyncCommand(OpenMapAsync);
        OpenMapCommand = new AsyncCommand(() => InvokeAsync(_openMapAsync));
        OpenToursCommand = new AsyncCommand(() => InvokeAsync(_openToursAsync));
        OpenCalendarCommand = new AsyncCommand(() => InvokeAsync(_openCalendarAsync));
        OpenOrdersCommand = new AsyncCommand(() => InvokeAsync(_openOrdersAsync));
        OpenEmployeesCommand = new AsyncCommand(() => InvokeAsync(_openEmployeesAsync));
        OpenVehiclesCommand = new AsyncCommand(() => InvokeAsync(_openVehiclesAsync));
        _dataSyncService.DataChanged += OnDataChanged;
    }

    public ObservableCollection<UpcomingDayCardItem> UpcomingDayCards { get; } = [];
    public ObservableCollection<StartTodayTourItem> TodayTours { get; } = [];
    public ObservableCollection<StartResourceAbsenceItem> EmployeeAbsences { get; } = [];
    public ObservableCollection<StartResourceAbsenceItem> VehicleOutages { get; } = [];
    public ObservableCollection<StartFixedDateOrderItem> FixedDateOrders { get; } = [];

    // Kept for compatibility with shared models used by other sections.
    public ObservableCollection<StartWeekGroupItem> UpcomingWeeks { get; } = [];

    public ICommand NewTourPlanCommand { get; }
    public ICommand OpenMapCommand { get; }
    public ICommand OpenToursCommand { get; }
    public ICommand OpenCalendarCommand { get; }
    public ICommand OpenOrdersCommand { get; }
    public ICommand OpenEmployeesCommand { get; }
    public ICommand OpenVehiclesCommand { get; }

    public string BannerImagePath => _bannerImagePath;

    public string TodayText { get => _todayText; private set => SetProperty(ref _todayText, value); }
    public bool HasTodayTours { get => _hasTodayTours; private set => SetProperty(ref _hasTodayTours, value); }
    public int TodayTourColumns { get => _todayTourColumns; private set => SetProperty(ref _todayTourColumns, value); }
    public bool HasEmployeeAbsences => EmployeeAbsences.Count > 0;
    public bool HasVehicleOutages => VehicleOutages.Count > 0;
    public string EmployeeAbsenceCountText => FormatOutageCount(EmployeeAbsences.Count);
    public string VehicleOutageCountText => FormatOutageCount(VehicleOutages.Count);
    public string ResourceSummary { get => _resourceSummary; private set => SetProperty(ref _resourceSummary, value); }
    public bool HasFixedDateOrders => FixedDateOrders.Count > 0;
    public string FixedDateSummary { get => _fixedDateSummary; private set => SetProperty(ref _fixedDateSummary, value); }

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
        var ordersTask = _orderRepository.GetAllAsync(cancellationToken);
        var employeesTask = _employeeRepository.LoadAsync(cancellationToken);
        var vehiclesTask = _vehicleRepository.LoadAsync(cancellationToken);
        await Task.WhenAll(settingsTask, toursTask, manualEntriesTask, ordersTask, employeesTask, vehiclesTask);

        var settings = await settingsTask;
        var userPreference = settings.ResolveUserPreference(LocalUserSessionService.CurrentUserName);
        var warningColor = NormalizeHexColor(userPreference.CalendarLoadWarningColor, AppSettings.DefaultCalendarLoadWarningColor);
        var criticalColor = NormalizeHexColor(userPreference.CalendarLoadCriticalColor, AppSettings.DefaultCalendarLoadCriticalColor);
        var warningThreshold = userPreference.CalendarLoadWarningPeopleThreshold < 1 ? 1 : userPreference.CalendarLoadWarningPeopleThreshold;
        var criticalThreshold = userPreference.CalendarLoadCriticalPeopleThreshold < warningThreshold
            ? warningThreshold
            : userPreference.CalendarLoadCriticalPeopleThreshold;

        var tours = await toursTask;
        var orders = await ordersTask;
        var employees = await employeesTask;
        var vehicleData = await vehiclesTask;
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
        TodayTours.Clear();
        EmployeeAbsences.Clear();
        VehicleOutages.Clear();
        FixedDateOrders.Clear();

        var today = DateTime.Today;
        toursByDate.TryGetValue(today, out var todayTours);
        todayTours ??= [];
        for (var tourIndex = 0; tourIndex < todayTours.Count; tourIndex++)
        {
            var tour = todayTours[tourIndex];
            var assignmentSummary = BuildTodayTourDetail(tour, employees, vehicleData);
            TodayTours.Add(new StartTodayTourItem
            {
                HeaderText = todayTours.Count > 1 ? $"Heutige Touren {tourIndex + 1}/{todayTours.Count}" : "Heutige Tour",
                TimeText = NormalizeStartTime(tour.StartTime),
                Name = string.IsNullOrWhiteSpace(tour.Name) ? $"Tour {tour.Id}" : tour.Name.Trim(),
                DetailText = assignmentSummary.CombinedText,
                StopCountText = assignmentSummary.StopCountText,
                EmployeesText = assignmentSummary.EmployeesText,
                VehiclesText = assignmentSummary.VehiclesText,
                TrailersText = assignmentSummary.TrailersText,
                HasEmployees = assignmentSummary.HasEmployees,
                HasVehicles = assignmentSummary.HasVehicles,
                HasTrailers = assignmentSummary.HasTrailers,
                Stops = BuildTodayTourStops(tour),
                OpenCommand = new AsyncCommand(() => OpenTourAsync(tour.Id))
            });
        }

        TodayText = today.ToString("dddd, dd. MMMM yyyy", UiCulture);
        HasTodayTours = todayTours.Count > 0;
        TodayTourColumns = todayTours.Count > 1 ? 2 : 1;
        BuildResourceAvailability(employees, vehicleData, today);
        BuildFixedDateOrders(orders, today);

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

    private async Task OpenTourAsync(int tourId)
    {
        if (_openTourAsync is not null)
        {
            await _openTourAsync(tourId);
        }
    }

    private static Task InvokeAsync(Func<Task>? callback) => callback?.Invoke() ?? Task.CompletedTask;

    private static StartTourAssignmentSummary BuildTodayTourDetail(
        TourRecord tour,
        IReadOnlyList<Employee> employees,
        VehicleDataRecord vehicleData)
    {
        var stops = tour.Stops.Count(x => !TourStopIdentity.IsCompanyStop(x) && !TourStopIdentity.IsPauseStop(x));
        var employeeNames = (tour.EmployeeIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => employees.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))?.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var vehicleNames = ResolveVehicleNames(vehicleData.Vehicles, tour.VehicleId, tour.SecondaryVehicleId);
        var trailerNames = ResolveTrailerNames(vehicleData.Trailers, tour.TrailerId, tour.SecondaryTrailerId);

        var stopText = $"{stops} {(stops == 1 ? "Stopp" : "Stopps")}";
        var employeeText = FormatAssignments(employeeNames);
        var vehicleText = FormatAssignments(vehicleNames);
        var trailerText = FormatAssignments(trailerNames);
        return new StartTourAssignmentSummary(
            stopText,
            employeeText,
            vehicleText,
            trailerText,
            employeeNames.Count > 0,
            vehicleNames.Count > 0,
            trailerNames.Count > 0,
            $"{stopText} · Mitarbeiter: {employeeText} · Fahrzeug: {vehicleText} · Anhänger: {trailerText}");
    }

    private static IReadOnlyList<string> ResolveVehicleNames(
        IReadOnlyList<Vehicle> vehicles,
        params string?[] ids)
    {
        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => vehicles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(vehicle => vehicle is not null)
            .Select(vehicle => FormatResourceName(vehicle!.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ResolveTrailerNames(
        IReadOnlyList<TrailerRecord> trailers,
        params string?[] ids)
    {
        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => trailers.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(trailer => trailer is not null)
            .Select(trailer => FormatResourceName(trailer!.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatResourceName(string? name) => (name ?? string.Empty).Trim();

    private static string FormatAssignments(IReadOnlyList<string> values) =>
        values.Count == 0 ? "nicht zugeteilt" : string.Join(", ", values);

    private static IReadOnlyList<StartTodayTourStopItem> BuildTodayTourStops(TourRecord tour)
    {
        var result = new List<StartTodayTourStopItem>();
        var customerIndex = 0;
        foreach (var stop in tour.Stops)
        {
            var isCompany = TourStopIdentity.IsCompanyStop(stop);
            var isPause = TourStopIdentity.IsPauseStop(stop);
            var marker = isCompany || isPause ? string.Empty : BuildStopMarker(customerIndex++);
            var name = isCompany
                ? TourStopIdentity.NormalizeCompanyStopDisplayName(stop.Name)
                : isPause
                    ? "Pause"
                    : string.IsNullOrWhiteSpace(stop.Auftragsnummer)
                        ? stop.Name
                        : $"{stop.Name} ({stop.Auftragsnummer})";
            var arrival = (stop.PlannedArrival ?? string.Empty).Trim();
            var departure = (stop.PlannedDeparture ?? string.Empty).Trim();
            result.Add(new StartTodayTourStopItem
            {
                Marker = marker,
                Name = string.IsNullOrWhiteSpace(name) ? "Stopp" : name.Trim(),
                Address = isCompany || isPause ? string.Empty : (stop.Address ?? string.Empty).Trim(),
                TimeText = !string.IsNullOrWhiteSpace(arrival) ? arrival : departure,
                TimeRangeText = !string.IsNullOrWhiteSpace(arrival) && !string.IsNullOrWhiteSpace(departure) && arrival != departure
                    ? $"{arrival} – {departure}"
                    : string.Empty,
                ServiceText = isPause
                    ? $"{Math.Max(0, stop.ServiceMinutes)} min"
                    : isCompany || stop.ServiceMinutes <= 0
                        ? string.Empty
                        : $"Aufenthalt: {stop.ServiceMinutes} min",
                IsCompanyStop = isCompany,
                IsPauseStop = isPause
            });
        }

        return result;
    }

    private static string BuildStopMarker(int index)
    {
        index = Math.Max(0, index);
        var marker = string.Empty;
        do
        {
            marker = (char)('A' + (index % 26)) + marker;
            index = (index / 26) - 1;
        } while (index >= 0);
        return marker;
    }

    private void BuildResourceAvailability(IReadOnlyList<Employee> employees, VehicleDataRecord vehicleData, DateTime today)
    {
        var from = DateOnly.FromDateTime(today);
        var to = from.AddDays(PreviewDayCount - 1);
        foreach (var employee in employees.Where(x => x.Active))
        {
            AddResourcePeriods(EmployeeAbsences, employee.DisplayName, employee.UnavailabilityPeriods, from, to, OpenEmployeesCommand, "#DC2626");
        }

        foreach (var vehicle in vehicleData.Vehicles.Where(x => x.Active))
        {
            var name = string.IsNullOrWhiteSpace(vehicle.LicensePlate) ? vehicle.Name : $"{vehicle.Name} · {vehicle.LicensePlate}";
            AddResourcePeriods(VehicleOutages, name, vehicle.UnavailabilityPeriods, from, to, OpenVehiclesCommand, "#F97316");
        }

        foreach (var trailer in vehicleData.Trailers.Where(x => x.Active))
        {
            AddResourcePeriods(VehicleOutages, trailer.Name, trailer.UnavailabilityPeriods, from, to, OpenVehiclesCommand, "#F97316");
        }

        SortResourceItems(EmployeeAbsences);
        SortResourceItems(VehicleOutages);
        OnPropertyChanged(nameof(HasEmployeeAbsences));
        OnPropertyChanged(nameof(HasVehicleOutages));
        OnPropertyChanged(nameof(EmployeeAbsenceCountText));
        OnPropertyChanged(nameof(VehicleOutageCountText));
        ResourceSummary = $"Nächste 7 Tage · {EmployeeAbsences.Count} Abwesenheiten · {VehicleOutages.Count} Fahrzeugausfälle";
    }

    private static string FormatOutageCount(int count) => count == 1 ? "1 Ausfall" : $"{count} Ausfälle";

    private static void AddResourcePeriods(
        ICollection<StartResourceAbsenceItem> target,
        string resourceName,
        IEnumerable<ResourceUnavailabilityPeriod>? periods,
        DateOnly from,
        DateOnly to,
        ICommand openCommand,
        string accentBrush)
    {
        foreach (var period in periods ?? [])
        {
            var start = ResourceAvailabilityService.ParseDate(period.StartDate);
            var end = ResourceAvailabilityService.ParseDate(period.EndDate);
            if (!start.HasValue || !end.HasValue) continue;
            var periodStart = start.Value <= end.Value ? start.Value : end.Value;
            var periodEnd = start.Value <= end.Value ? end.Value : start.Value;
            if (periodEnd < from || periodStart > to) continue;

            var visibleStart = periodStart < from ? from : periodStart;
            var visibleEnd = periodEnd > to ? to : periodEnd;
            target.Add(new StartResourceAbsenceItem
            {
                ResourceName = string.IsNullOrWhiteSpace(resourceName) ? "Ohne Bezeichnung" : resourceName.Trim(),
                PeriodText = BuildResourcePeriodText(periodStart, periodEnd, period.StartTime, period.EndTime, from),
                Note = (period.Note ?? string.Empty).Trim(),
                SortDate = visibleStart,
                IsToday = visibleStart <= from && visibleEnd >= from,
                AccentBrush = accentBrush,
                OpenCommand = openCommand
            });
        }
    }

    private static string BuildResourcePeriodText(DateOnly start, DateOnly end, string? startTime, string? endTime, DateOnly today)
    {
        var dateText = start == end
            ? (start == today ? "Heute" : start.ToString("ddd, dd.MM.", UiCulture))
            : $"{start:dd.MM.} – {end:dd.MM.}";
        var fromTime = (startTime ?? string.Empty).Trim();
        var toTime = (endTime ?? string.Empty).Trim();
        var timeText = fromTime.Length > 0 && toTime.Length > 0 ? $" · {fromTime}–{toTime}" : fromTime.Length > 0 ? $" · ab {fromTime}" : toTime.Length > 0 ? $" · bis {toTime}" : " · ganztägig";
        return dateText + timeText;
    }

    private static void SortResourceItems(ObservableCollection<StartResourceAbsenceItem> items)
    {
        var ordered = items.OrderByDescending(x => x.IsToday).ThenBy(x => x.SortDate).ThenBy(x => x.ResourceName, StringComparer.OrdinalIgnoreCase).ToList();
        items.Clear();
        foreach (var item in ordered) items.Add(item);
    }

    private void BuildFixedDateOrders(IReadOnlyList<Order> orders, DateTime today)
    {
        FixedDateOrders.Clear();
        foreach (var item in CreateFixedDateItems(orders, DateOnly.FromDateTime(today), OpenOrderAsync))
            FixedDateOrders.Add(item);

        OnPropertyChanged(nameof(HasFixedDateOrders));
        FixedDateSummary = FixedDateOrders.Count == 0
            ? "Keine anstehenden Fixtermine"
            : $"{FixedDateOrders.Count} {(FixedDateOrders.Count == 1 ? "Liefertermin / Zeitraum" : "Liefertermine / Zeiträume")}";
    }

    internal static IReadOnlyList<StartFixedDateOrderItem> CreateFixedDateItems(
        IReadOnlyList<Order> orders, DateOnly today, Func<string, OrderType, Task>? openOrderAsync = null)
    {
        DeliveryPeriod? ResolveWindow(Order order)
        {
            if (order.IsArchived) return null;
            // Keep the existing fixed-date rules; a period must not override a concrete date.
            if (order.DeliveryDate is { } date && date != DateOnly.MinValue)
                return !order.DeliveryCanOccurEarlier ? new DeliveryPeriod(date, date) : null;
            return DeliveryPeriodParser.Parse(order.DeliveryPeriod, today);
        }

        string FormatWindow(DeliveryPeriod window)
        {
            if (window.Start is null) return $"Bis {window.End:dd.MM.yyyy}";
            if (window.End is null) return $"Ab {window.Start:dd.MM.yyyy}";
            if (window.Start == window.End)
                return window.Start == today ? "Heute" : window.Start.Value.ToString("ddd, dd.MM.yyyy", UiCulture);
            return $"{window.Start:dd.MM.yyyy} – {window.End:dd.MM.yyyy}";
        }

        return orders.Select(order => (Order: order, Window: ResolveWindow(order)))
            .Where(x => x.Window is not null && (x.Window.End is null || x.Window.End >= today))
            .OrderBy(x => x.Window!.Start ?? x.Window.End)
            .ThenBy(x => x.Order.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Order.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => new StartFixedDateOrderItem
            {
                CustomerName = string.IsNullOrWhiteSpace(x.Order.CustomerName) ? $"Auftrag {x.Order.Id}" : x.Order.CustomerName.Trim(),
                OrderNumber = x.Order.Id,
                Address = x.Order.Address?.Trim() ?? string.Empty,
                DateText = FormatWindow(x.Window!),
                DeliveryPeriod = x.Order.DeliveryPeriod,
                IsToday = (x.Window!.Start is null || x.Window.Start <= today) && (x.Window.End is null || x.Window.End >= today),
                AssignmentText = string.IsNullOrWhiteSpace(x.Order.AssignedTourId) ? "Noch nicht eingeplant" : "Tour zugeteilt",
                OpenCommand = new AsyncCommand(() => openOrderAsync?.Invoke(x.Order.Id, x.Order.Type) ?? Task.CompletedTask)
            }).ToList();
    }

    private Task OpenOrderAsync(string orderId, OrderType orderType) =>
        _openOrderAsync?.Invoke(orderId, orderType) ?? Task.CompletedTask;

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId ||
            (args.Kinds & (AppDataKind.Tours | AppDataKind.Orders | AppDataKind.CalendarEntries | AppDataKind.Settings | AppDataKind.Employees | AppDataKind.Vehicles)) == 0)
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

public sealed class StartTodayTourItem
{
    public string HeaderText { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public string StopCountText { get; set; } = string.Empty;
    public string EmployeesText { get; set; } = string.Empty;
    public string VehiclesText { get; set; } = string.Empty;
    public string TrailersText { get; set; } = string.Empty;
    public bool HasEmployees { get; set; }
    public bool HasVehicles { get; set; }
    public bool HasTrailers { get; set; }
    public IReadOnlyList<StartTodayTourStopItem> Stops { get; set; } = [];
    public ICommand? OpenCommand { get; set; }
}

internal sealed record StartTourAssignmentSummary(
    string StopCountText,
    string EmployeesText,
    string VehiclesText,
    string TrailersText,
    bool HasEmployees,
    bool HasVehicles,
    bool HasTrailers,
    string CombinedText);

public sealed class StartTodayTourStopItem
{
    public string Marker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public string TimeRangeText { get; set; } = string.Empty;
    public string ServiceText { get; set; } = string.Empty;
    public bool IsCompanyStop { get; set; }
    public bool IsPauseStop { get; set; }
}

public sealed class StartResourceAbsenceItem
{
    public string ResourceName { get; set; } = string.Empty;
    public string PeriodText { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateOnly SortDate { get; set; }
    public bool IsToday { get; set; }
    public string AccentBrush { get; set; } = "#DC2626";
    public ICommand? OpenCommand { get; set; }
}

public sealed class StartFixedDateOrderItem
{
    public string DeliveryPeriod { get; set; } = string.Empty;
    public bool HasDeliveryPeriod => !string.IsNullOrWhiteSpace(DeliveryPeriod);
    public string CustomerName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string TitleText => string.IsNullOrWhiteSpace(OrderNumber)
        ? CustomerName
        : $"{CustomerName} ({OrderNumber})";
    public string Address { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public string AssignmentText { get; set; } = string.Empty;
    public bool IsToday { get; set; }
    public ICommand? OpenCommand { get; set; }
}
