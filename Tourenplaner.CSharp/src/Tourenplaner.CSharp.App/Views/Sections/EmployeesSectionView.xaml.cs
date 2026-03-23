using System.Windows;
using System.Windows.Controls;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class EmployeesSectionView : UserControl
{
    public EmployeesSectionView()
    {
        InitializeComponent();
    }

    private async void OnAddEntryClicked(object sender, RoutedEventArgs e)
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
            await vm.ApplyEditorResultAsync(dialog.Result);
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
            await vm.ApplyEditorResultAsync(dialog.Result);
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
