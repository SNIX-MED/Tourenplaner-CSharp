using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Components;

public partial class DashboardTopBar : UserControl
{
    private DateTime _suppressUserPopupReopenUntilUtc = DateTime.MinValue;

    public DashboardTopBar()
    {
        InitializeComponent();
    }

    private async void MapSearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not KarteSectionViewModel vm) return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSearchSuggestionSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSearchSuggestionSelection(-1);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CloseSearchSuggestions();
                e.Handled = true;
                break;
            case Key.Enter:
                if (vm.IsSearchSuggestionsOpen && vm.SearchSuggestions.Count > 0)
                    await vm.ApplySearchSuggestionAsync(vm.SelectedSearchSuggestion);
                else if (vm.SearchCommand.CanExecute(null))
                    vm.SearchCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private async void MapSearchSuggestionsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is KarteSectionViewModel vm &&
            (e.OriginalSource as FrameworkElement)?.DataContext is MapSearchSuggestionItem suggestion)
        {
            await vm.ApplySearchSuggestionAsync(suggestion);
            e.Handled = true;
        }
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

    private void UserSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc < _suppressUserPopupReopenUntilUtc)
        {
            UserSelectionButton.IsChecked = false;
            UserSelectionPopup.IsOpen = false;
            return;
        }

        if (UserSelectionPopup.IsOpen)
        {
            UserSelectionPopup.IsOpen = false;
            UserSelectionButton.IsChecked = false;
            _suppressUserPopupReopenUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
            return;
        }

        UserSelectionPopup.IsOpen = true;
        UserSelectionButton.IsChecked = true;
    }

    private void UserSelectionPopup_Closed(object? sender, EventArgs e)
    {
        UserSelectionButton.IsChecked = false;
        _suppressUserPopupReopenUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
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
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(owner, result.Message, "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(result.Message, "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

