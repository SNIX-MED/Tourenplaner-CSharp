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
    private readonly AppDataSyncService _dataSyncService;
    private readonly List<Employee> _employees = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private string _statusText = "Lade Mitarbeiter...";
    private string _countText = string.Empty;

    public EmployeesSectionViewModel(string employeesJsonPath, AppDataSyncService dataSyncService)
        : base("Mitarbeiterverwaltung", "Mitarbeiter anlegen, bearbeiten und deaktivieren.")
    {
        _repository = new JsonEmployeesRepository(employeesJsonPath);
        _dataSyncService = dataSyncService;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        _dataSyncService.DataChanged += OnDataChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<EmployeeCardItem> Entries { get; } = new();

    public ICommand RefreshCommand { get; }

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
            Active: true);
    }

    public EmployeeEditorSeed CreateSeedForEdit(EmployeeCardItem entry)
    {
        return new EmployeeEditorSeed(
            Id: entry.Id,
            Name: entry.Name,
            ShortCode: entry.ShortCode,
            Phone: entry.Phone,
            Active: entry.Active);
    }

    public async Task ApplyEditorResultAsync(EmployeeEditorResult result)
    {
        var id = string.IsNullOrWhiteSpace(result.Id) ? Guid.NewGuid().ToString() : result.Id.Trim();
        _employees.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        _employees.Add(new Employee
        {
            Id = id,
            DisplayName = result.Name.Trim(),
            ShortCode = result.ShortCode.Trim(),
            Phone = result.Phone.Trim(),
            Role = result.ShortCode.Trim(),
            Active = result.Active
        });

        await SaveCurrentStateAsync();
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
        foreach (var employee in _employees.OrderBy(x => !x.Active).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Entries.Add(new EmployeeCardItem
            {
                Id = employee.Id,
                Name = employee.DisplayName,
                ShortCode = employee.ShortCode,
                Phone = employee.Phone,
                Active = employee.Active
            });
        }

        var active = Entries.Count(x => x.Active);
        StatusText = $"Mitarbeiter: {Entries.Count} | Aktiv: {active} | Inaktiv: {Entries.Count - active}";
        CountText = $"Mitarbeiter: {Entries.Count}";
    }
}

public sealed class EmployeeCardItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool Active { get; set; } = true;

    public string ActiveLabel => Active ? "Aktiv" : "Inaktiv";

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

            return string.Join(" | ", parts);
        }
    }
}

public sealed record EmployeeEditorSeed(
    string? Id,
    string Name,
    string ShortCode,
    string Phone,
    bool Active);

public sealed record EmployeeEditorResult(
    string? Id,
    string Name,
    string ShortCode,
    string Phone,
    bool Active);
