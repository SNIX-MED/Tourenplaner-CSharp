using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class SelectEmployeesDialogWindow : Window
{
    public SelectEmployeesDialogWindow(IReadOnlyList<SelectableEmployee> employees)
    {
        InitializeComponent();
        ViewModel = new SelectEmployeesDialogViewModel(employees);
        DataContext = ViewModel;
    }

    public SelectEmployeesDialogViewModel ViewModel { get; }

    public IReadOnlyList<string>? SelectedEmployeeIds { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.Employees.Where(x => x.IsSelected).Select(x => x.Id).Take(2).ToList();
        if (selected.Count is < 1 or > 2)
        {
            MessageBox.Show(this, "Bitte 1 bis 2 Mitarbeiter auswählen.", "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedEmployeeIds = selected;
        DialogResult = true;
        Close();
    }
}

public sealed class SelectEmployeesDialogViewModel : ObservableObject
{
    public SelectEmployeesDialogViewModel(IReadOnlyList<SelectableEmployee> employees)
    {
        Employees = new ObservableCollection<SelectableEmployee>(
            (employees ?? [])
            .Select(x => new SelectableEmployee(x.Id, x.Label) { IsSelected = x.IsSelected }));

        foreach (var employee in Employees)
        {
            employee.PropertyChanged += OnEmployeePropertyChanged;
        }

        RefreshSummary();
    }

    public ObservableCollection<SelectableEmployee> Employees { get; }

    private string _summaryText = "0 von 2 Mitarbeitern ausgewählt.";
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    private void OnEmployeePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectableEmployee.IsSelected) || sender is not SelectableEmployee changed || !changed.IsSelected)
        {
            RefreshSummary();
            return;
        }

        var selected = Employees.Where(x => x.IsSelected).ToList();
        if (selected.Count > 2)
        {
            var toDisable = selected.FirstOrDefault(x => x != changed);
            if (toDisable is not null)
            {
                toDisable.IsSelected = false;
            }
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        SummaryText = $"{Employees.Count(x => x.IsSelected)} von 2 Mitarbeitern ausgewählt.";
    }
}
