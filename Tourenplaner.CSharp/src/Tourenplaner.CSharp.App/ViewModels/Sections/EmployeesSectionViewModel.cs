using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class EmployeesSectionViewModel : SectionViewModelBase
{
    private readonly JsonEmployeesRepository _repository;
    private readonly JsonToursRepository _tourRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly List<Employee> _employees = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private string _statusText = "Lade Mitarbeiter...";
    private string _countText = string.Empty;

    public EmployeesSectionViewModel(string employeesJsonPath, string toursJsonPath, AppDataSyncService dataSyncService)
        : base("Mitarbeiterverwaltung", "Mitarbeiter anlegen, bearbeiten und Abwesenheiten planen.")
    {
        _repository = new JsonEmployeesRepository(employeesJsonPath);
        _tourRepository = new JsonToursRepository(toursJsonPath);
        _dataSyncService = dataSyncService;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        RequestAddEntryCommand = new DelegateCommand(() => AddEntryRequested?.Invoke(this, EventArgs.Empty));
        _dataSyncService.DataChanged += OnDataChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<EmployeeCardItem> Entries { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand RequestAddEntryCommand { get; }
    public event EventHandler? AddEntryRequested;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CountText
    {
        get => _countText;
        private set => SetProperty(ref _countText, value);
    }

    public EmployeeEditorSeed CreateSeedForCreate()
    {
        return new EmployeeEditorSeed(
            Id: null,
            Name: string.Empty,
            ShortCode: string.Empty,
            Phone: string.Empty,
            RegisterAbsence: false,
            AbsenceStartDate: string.Empty,
            AbsenceEndDate: string.Empty);
    }

    public EmployeeEditorSeed CreateSeedForEdit(EmployeeCardItem entry)
    {
        var source = _employees.FirstOrDefault(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        var editablePeriod = GetEditablePeriod(source?.UnavailabilityPeriods);
        return new EmployeeEditorSeed(
            Id: entry.Id,
            Name: entry.Name,
            ShortCode: entry.ShortCode,
            Phone: entry.Phone,
            RegisterAbsence: editablePeriod is not null,
            AbsenceStartDate: editablePeriod?.StartDate.ToString("dd.MM.yyyy") ?? string.Empty,
            AbsenceEndDate: editablePeriod?.EndDate.ToString("dd.MM.yyyy") ?? string.Empty);
    }

    public async Task<string?> ApplyEditorResultAsync(EmployeeEditorResult result)
    {
        var id = string.IsNullOrWhiteSpace(result.Id) ? Guid.NewGuid().ToString() : result.Id.Trim();
        var existing = _employees.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        var periods = new List<ResourceUnavailabilityPeriod>();

        string? warning = null;
        if (result.RegisterAbsence)
        {
            var absenceStart = ResourceAvailabilityService.ParseDate(result.AbsenceStartDate);
            var absenceEnd = ResourceAvailabilityService.ParseDate(result.AbsenceEndDate);
            if (absenceStart.HasValue && absenceEnd.HasValue)
            {
                var from = absenceStart.Value <= absenceEnd.Value ? absenceStart.Value : absenceEnd.Value;
                var to = absenceStart.Value <= absenceEnd.Value ? absenceEnd.Value : absenceStart.Value;
                periods.Add(new ResourceUnavailabilityPeriod
                {
                    StartDate = from.ToString("yyyy-MM-dd"),
                    EndDate = to.ToString("yyyy-MM-dd")
                });
                warning = await BuildAbsenceAssignmentWarningAsync(id, result.Name, from, to);
            }
        }

        _employees.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        _employees.Add(new Employee
        {
            Id = id,
            DisplayName = result.Name.Trim(),
            ShortCode = result.ShortCode.Trim(),
            Phone = result.Phone.Trim(),
            Role = result.ShortCode.Trim(),
            Active = existing?.Active ?? true,
            UnavailabilityPeriods = periods
        });

        await SaveCurrentStateAsync();
        return warning;
    }

    public async Task DeleteEntryAsync(EmployeeCardItem entry)
    {
        _employees.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        await SaveCurrentStateAsync();
    }

    public async Task RefreshAsync()
    {
        _employees.Clear();
        _employees.AddRange(await _repository.LoadAsync());
        RebuildEntries();
    }

    private async Task SaveCurrentStateAsync()
    {
        await _repository.SaveAsync(_employees);
        _dataSyncService.PublishEmployees(_instanceId);
        await RefreshAsync();
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId || !args.Kinds.HasFlag(AppDataKind.Employees))
        {
            return;
        }

        _ = RefreshAsync();
    }

    private void RebuildEntries()
    {
        Entries.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var employee in _employees
                     .OrderByDescending(x => ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, today))
                     .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var isUnavailableToday = ResourceAvailabilityService.IsUnavailableOnDate(employee.UnavailabilityPeriods, today);
            Entries.Add(new EmployeeCardItem
            {
                Id = employee.Id,
                Name = employee.DisplayName,
                ShortCode = employee.ShortCode,
                Phone = employee.Phone,
                IsUnavailableToday = isUnavailableToday,
                NextUnavailabilityText = BuildNextUnavailabilityText(employee.UnavailabilityPeriods, today)
            });
        }

        var unavailableToday = Entries.Count(x => x.IsUnavailableToday);
        StatusText = $"Mitarbeiter: {Entries.Count} | Heute abwesend: {unavailableToday}";
        CountText = $"Mitarbeiter: {Entries.Count}";
    }

    private async Task<string?> BuildAbsenceAssignmentWarningAsync(string employeeId, string employeeName, DateOnly from, DateOnly to)
    {
        var tours = await _tourRepository.LoadAsync();
        var affected = tours
            .Where(t =>
            {
                var date = ResourceAvailabilityService.ParseDate(t.Date);
                if (!date.HasValue || date.Value < from || date.Value > to)
                {
                    return false;
                }

                return t.EmployeeIds.Any(x => string.Equals((x ?? string.Empty).Trim(), employeeId, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(t => ResourceAvailabilityService.ParseDate(t.Date))
            .ThenBy(t => t.StartTime, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (affected.Count == 0)
        {
            return null;
        }

        var lines = affected
            .Take(6)
            .Select(t => $"{t.Date} {t.StartTime} - {t.Name}")
            .ToList();
        if (affected.Count > lines.Count)
        {
            lines.Add($"... und {affected.Count - lines.Count} weitere Tour(en).");
        }

        return $"Achtung: Mitarbeiter \"{employeeName}\" ist im gewählten Abwesenheitszeitraum bereits in Touren eingeplant:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static (DateOnly StartDate, DateOnly EndDate)? GetEditablePeriod(IEnumerable<ResourceUnavailabilityPeriod>? periods)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var upcoming = (periods ?? [])
            .Select(x => new
            {
                Start = ResourceAvailabilityService.ParseDate(x.StartDate),
                End = ResourceAvailabilityService.ParseDate(x.EndDate)
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .Select(x => new
            {
                Start = x.Start!.Value <= x.End!.Value ? x.Start.Value : x.End.Value,
                End = x.Start!.Value <= x.End!.Value ? x.End.Value : x.Start.Value
            })
            .Where(x => x.End >= today)
            .OrderBy(x => x.Start)
            .FirstOrDefault();

        return upcoming is null ? null : (upcoming.Start, upcoming.End);
    }

    private static string BuildNextUnavailabilityText(IEnumerable<ResourceUnavailabilityPeriod>? periods, DateOnly today)
    {
        var upcoming = (periods ?? [])
            .Select(x => new
            {
                Start = ResourceAvailabilityService.ParseDate(x.StartDate),
                End = ResourceAvailabilityService.ParseDate(x.EndDate)
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .Select(x => new
            {
                Start = x.Start!.Value <= x.End!.Value ? x.Start.Value : x.End.Value,
                End = x.Start!.Value <= x.End!.Value ? x.End.Value : x.Start.Value
            })
            .Where(x => x.End >= today)
            .OrderBy(x => x.Start)
            .FirstOrDefault();

        if (upcoming is null)
        {
            return "Keine Abwesenheit geplant";
        }

        return $"Abwesend: {upcoming.Start:dd.MM.yyyy} - {upcoming.End:dd.MM.yyyy}";
    }
}

public sealed class EmployeeCardItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsUnavailableToday { get; set; }
    public string NextUnavailabilityText { get; set; } = string.Empty;

    public string AvailabilityLabel => IsUnavailableToday ? "Abwesend" : "Verfügbar";

    public string DetailsLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ShortCode))
            {
                parts.Add($"Kürzel: {ShortCode}");
            }

            if (!string.IsNullOrWhiteSpace(Phone))
            {
                parts.Add($"Telefon: {Phone}");
            }

            if (!string.IsNullOrWhiteSpace(NextUnavailabilityText))
            {
                parts.Add(NextUnavailabilityText);
            }

            return string.Join(" | ", parts);
        }
    }
}

public sealed record EmployeeEditorSeed(
    string? Id,
    string Name,
    string ShortCode,
    string Phone,
    bool RegisterAbsence,
    string AbsenceStartDate,
    string AbsenceEndDate);

public sealed record EmployeeEditorResult(
    string? Id,
    string Name,
    string ShortCode,
    string Phone,
    bool RegisterAbsence,
    string AbsenceStartDate,
    string AbsenceEndDate);
