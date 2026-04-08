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

        var dialog = new EmployeeEditorDialogWindow(vm.CreateSeedForCreate())
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            var warning = await vm.ApplyEditorResultAsync(dialog.Result);
            if (!string.IsNullOrWhiteSpace(warning))
            {
                MessageBox.Show(warning, "Abwesenheit prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var dialog = new EmployeeEditorDialogWindow(vm.CreateSeedForEdit(entry))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            var warning = await vm.ApplyEditorResultAsync(dialog.Result);
            if (!string.IsNullOrWhiteSpace(warning))
            {
                MessageBox.Show(warning, "Abwesenheit prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void OnDeleteEntryClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesSectionViewModel vm ||
            sender is not Button { Tag: EmployeeCardItem entry })
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"\"{entry.Name}\" wirklich löschen?",
            "Mitarbeiter löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await vm.DeleteEntryAsync(entry);
    }
}
