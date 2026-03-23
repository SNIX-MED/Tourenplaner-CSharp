using System.Windows;
using System.Windows.Controls;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class VehiclesSectionView : UserControl
{
    public VehiclesSectionView()
    {
        InitializeComponent();
    }

    private async void OnAddEntryClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not VehiclesSectionViewModel vm)
        {
            return;
        }

        var dialog = new VehicleEditorDialogWindow(vm.CreateSeedForCreate())
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
        if (DataContext is not VehiclesSectionViewModel vm ||
            sender is not Button { Tag: FleetEntryCardItem entry })
        {
            return;
        }

        var dialog = new VehicleEditorDialogWindow(vm.CreateSeedForEdit(entry))
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
        if (DataContext is not VehiclesSectionViewModel vm ||
            sender is not Button { Tag: FleetEntryCardItem entry })
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"\"{entry.Name}\" wirklich löschen?",
            "Fahrzeug löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await vm.DeleteEntryAsync(entry);
    }
}
