using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class EmployeesSectionViewModel : SectionViewModelBase
{
    private readonly JsonEmployeesRepository _repository;
    private EmployeeItem? _selectedEmployee;
    private string _statusText = "Lade Mitarbeiter...";

    public EmployeesSectionViewModel(string employeesJsonPath)
        : base("Employees", "Mitarbeiterverwaltung inklusive Aktiv/Inaktiv-Status.")
    {
        _repository = new JsonEmployeesRepository(employeesJsonPath);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => Employees.Count > 0);
        AddCommand = new DelegateCommand(AddEmployee);
        RemoveCommand = new DelegateCommand(RemoveSelectedEmployee, () => SelectedEmployee is not null);
        _ = RefreshAsync();
    }

    public ObservableCollection<EmployeeItem> Employees { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public EmployeeItem? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        var items = await _repository.LoadAsync();
        Employees.Clear();

        foreach (var employee in items.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Employees.Add(new EmployeeItem
            {
                Id = employee.Id,
                DisplayName = employee.DisplayName,
                Role = employee.Role,
                Active = employee.Active
            });
        }

        SelectedEmployee = Employees.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    public async Task SaveAsync()
    {
        var payload = Employees
            .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName))
            .Select(e => new Employee
            {
                Id = string.IsNullOrWhiteSpace(e.Id) ? Guid.NewGuid().ToString() : e.Id.Trim(),
                DisplayName = (e.DisplayName ?? string.Empty).Trim(),
                Role = (e.Role ?? string.Empty).Trim(),
                Active = e.Active
            })
            .ToList();

        await _repository.SaveAsync(payload);
        await RefreshAsync();
    }

    private void AddEmployee()
    {
        var item = new EmployeeItem
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = "Neuer Mitarbeiter",
            Role = "Driver",
            Active = true
        };

        Employees.Add(item);
        SelectedEmployee = item;
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void RemoveSelectedEmployee()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        Employees.Remove(SelectedEmployee);
        SelectedEmployee = Employees.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void UpdateStatusText()
    {
        var active = Employees.Count(e => e.Active);
        StatusText = $"Mitarbeiter: {Employees.Count} | Aktiv: {active} | Inaktiv: {Employees.Count - active}";
    }

    private void RaiseCommandStates()
    {
        if (SaveCommand is AsyncCommand save)
        {
            save.RaiseCanExecuteChanged();
        }

        if (RemoveCommand is DelegateCommand remove)
        {
            remove.RaiseCanExecuteChanged();
        }
    }
}

public sealed class EmployeeItem : ObservableObject
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;
    private string _role = string.Empty;
    private bool _active = true;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Role
    {
        get => _role;
        set => SetProperty(ref _role, value);
    }

    public bool Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }
}
