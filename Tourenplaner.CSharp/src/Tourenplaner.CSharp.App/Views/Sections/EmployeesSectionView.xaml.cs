using System.Windows;
using System.Windows.Controls;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class EmployeesSectionView : UserControl
{
    private EmployeesSectionViewModel? _currentViewModel;

    public EmployeesSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_currentViewModel is not null)
        {
            _currentViewModel.AddEntryRequested -= OnAddEntryRequested;
        }

        _currentViewModel = DataContext as EmployeesSectionViewModel;
        if (_currentViewModel is not null)
        {
            _currentViewModel.AddEntryRequested += OnAddEntryRequested;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_currentViewModel is not null)
        {
            _currentViewModel.AddEntryRequested -= OnAddEntryRequested;
            _currentViewModel = null;
        }
    }

    private async void OnAddEntryRequested(object? sender, EventArgs e)
    {
        await OpenAddEntryDialogAsync();
    }

    private async Task OpenAddEntryDialogAsync()
    {
        if (DataContext is not EmployeesSectionViewModel vm)
        {
            return;
        }

        var seed = vm.CreateSeedForCreate();
        var dialog = new EmployeeEditorDialogWindow(
            seed,
            await GetWebfleetVehiclesSafeAsync(vm, seed.Id),
            vm.GetWebfleetAssignmentHint(seed.Id),
            vm.HasDuplicateWebfleetAssignment(seed.Id))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            string? warning;
            try
            {
                warning = await vm.ApplyEditorResultAsync(dialog.Result);
            }
            catch (InvalidOperationException ex)
            {
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(ex.Message, "WEBFLEET-Gerät", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(warning))
            {
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(warning, "Abwesenheit prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void OnEditEntryClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesSectionViewModel vm ||
            sender is not Button { Tag: EmployeeCardItem entry })
        {
            return;
        }

        var seed = vm.CreateSeedForEdit(entry);
        var dialog = new EmployeeEditorDialogWindow(
            seed,
            await GetWebfleetVehiclesSafeAsync(vm, seed.Id),
            vm.GetWebfleetAssignmentHint(seed.Id),
            vm.HasDuplicateWebfleetAssignment(seed.Id))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        var dialogResult = dialog.ShowDialog();
        if (dialog.DeleteRequested)
        {
            var confirm = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"\"{entry.Name}\" wirklich löschen?",
                "Mitarbeiter löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                await vm.DeleteEntryAsync(entry);
            }

            return;
        }

        if (dialogResult == true && dialog.Result is not null)
        {
            string? warning;
            try
            {
                warning = await vm.ApplyEditorResultAsync(dialog.Result);
            }
            catch (InvalidOperationException ex)
            {
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(ex.Message, "WEBFLEET-Gerät", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(warning))
            {
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(warning, "Abwesenheit prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private static async Task<IReadOnlyList<Tourenplaner.CSharp.App.Services.WebfleetVehicleSnapshot>> GetWebfleetVehiclesSafeAsync(EmployeesSectionViewModel viewModel, string? employeeId)
    {
        try
        {
            return await viewModel.GetWebfleetVehiclesAsync(employeeId);
        }
        catch (Exception ex)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"WEBFLEET-Objekte konnten nicht geladen werden. Eine bestehende Zuordnung bleibt unverändert.{Environment.NewLine}{ex.Message}",
                "WEBFLEET",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return [];
        }
    }
}
