using System.Collections.ObjectModel;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class WebfleetDriverSelectionDialogWindow : Window
{
    public WebfleetDriverSelectionDialogWindow(IReadOnlyList<Employee> drivers)
    {
        InitializeComponent();
        DataContext = ViewModel = new WebfleetDriverSelectionViewModel(drivers);
    }

    public WebfleetDriverSelectionViewModel ViewModel { get; }
    public Employee? SelectedDriver => ViewModel.SelectedDriver?.Employee;
    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedDriver is null) return;
        DialogResult = true;
    }
}

public sealed class WebfleetDriverSelectionViewModel : ObservableObject
{
    public WebfleetDriverSelectionViewModel(IReadOnlyList<Employee> drivers)
    {
        Drivers = new ObservableCollection<WebfleetDriverOption>((drivers ?? []).Select(x => new WebfleetDriverOption(x)));
        SelectedDriver = Drivers.FirstOrDefault();
    }
    public ObservableCollection<WebfleetDriverOption> Drivers { get; }
    private WebfleetDriverOption? _selectedDriver;
    public WebfleetDriverOption? SelectedDriver { get => _selectedDriver; set => SetProperty(ref _selectedDriver, value); }
}

public sealed record WebfleetDriverOption(Employee Employee)
{
    public string Label => string.IsNullOrWhiteSpace(Employee.WebfleetObjectNumber) ? Employee.DisplayName : $"{Employee.DisplayName} ({Employee.WebfleetObjectNumber})";
}
