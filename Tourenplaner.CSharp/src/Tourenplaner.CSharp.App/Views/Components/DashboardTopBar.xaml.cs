using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Components;

public partial class DashboardTopBar : UserControl
{
    public DashboardTopBar()
    {
        InitializeComponent();
    }

    private CustomPopupPlacement[] CenterPopupUnderButton(Size popupSize, Size targetSize, Point offset)
    {
        var x = (targetSize.Width - popupSize.Width) / 2d;
        var y = targetSize.Height + 6d;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
    }

    private void TogglePopupButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ToggleButton button || button.IsChecked != true)
        {
            return;
        }

        button.IsChecked = false;
        e.Handled = true;
    }

    private void PinInfoCardScaleSlider_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KarteSectionViewModel vm)
        {
            return;
        }

        vm.PinInfoCardScale = 1.0d;
        e.Handled = true;
    }

    private async void OnAddCalendarManualEntryClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KalenderSectionViewModel vm)
        {
            return;
        }

        var dialog = new CalendarManualEntryDialogWindow(
            vm.SelectedDay?.Date ?? DateTime.Today,
            vm.ManualEntryColorOptions,
            vm.DefaultManualEntryColor)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.EntryDate is not DateTime date)
        {
            return;
        }

        var result = await vm.SaveManualEntryAsync(
            date,
            dialog.EntryTime,
            dialog.EntryTitle,
            dialog.EntryDescription,
            dialog.EntryColor);
        if (!result.Success)
        {
            var owner = Window.GetWindow(this);
            if (owner is not null)
            {
                MessageBox.Show(owner, result.Message, "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(result.Message, "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
