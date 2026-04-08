using System.Windows;
using System.Windows.Controls;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class VehiclesSectionView : UserControl
{
    private VehiclesSectionViewModel? _currentViewModel;

    public VehiclesSectionView()
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

        _currentViewModel = DataContext as VehiclesSectionViewModel;
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
        if (DataContext is not VehiclesSectionViewModel vm)
        {
            return;
        }

        if (vm.IsCombinationMode)
        {
            var combinationDialog = new VehicleCombinationEditorDialogWindow(
                vm.CreateCombinationSeedForCreate(),
                vm.BuildCombinationOptions(),
                vm.BuildTrailerOptions())
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (combinationDialog.ShowDialog() == true && combinationDialog.Result is not null)
            {
                await vm.ApplyCombinationEditorResultAsync(combinationDialog.Result);
            }

            return;
        }

        var dialog = new VehicleEditorDialogWindow(vm.CreateSeedForCreate())
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            try
            {
                var warning = await vm.ApplyEditorResultAsync(dialog.Result);
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    MessageBox.Show(warning, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Fahrzeug speichern", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void OnEditEntryClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not VehiclesSectionViewModel vm ||
            sender is not Button { Tag: FleetEntryCardItem entry })
        {
            return;
        }

        if (entry.IsCombination)
        {
            var combinationDialog = new VehicleCombinationEditorDialogWindow(
                vm.CreateCombinationSeedForEdit(entry),
                vm.BuildCombinationOptions(),
                vm.BuildTrailerOptions())
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (combinationDialog.ShowDialog() == true && combinationDialog.Result is not null)
            {
                try
                {
                    await vm.ApplyCombinationEditorResultAsync(combinationDialog.Result);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Fahrzeugkombination speichern", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            return;
        }

        var dialog = new VehicleEditorDialogWindow(vm.CreateSeedForEdit(entry))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            try
            {
                var warning = await vm.ApplyEditorResultAsync(dialog.Result);
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    MessageBox.Show(warning, "Ausfall prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Fahrzeug speichern", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void OnDeleteEntryClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not VehiclesSectionViewModel vm ||
            sender is not Button { Tag: FleetEntryCardItem entry })
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"\"{entry.Name}\" wirklich löschen?",
            entry.IsCombination ? "Fahrzeugkombination löschen" : "Fahrzeug löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await vm.DeleteEntryAsync(entry);
    }
}
