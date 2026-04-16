using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class KalenderSectionView : UserControl
{
    public KalenderSectionView()
    {
        InitializeComponent();
    }

    private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PageScrollViewer is null || PageScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var target = PageScrollViewer.VerticalOffset - (e.Delta / 3d);
        if (target < 0)
        {
            target = 0;
        }
        else if (target > PageScrollViewer.ScrollableHeight)
        {
            target = PageScrollViewer.ScrollableHeight;
        }

        PageScrollViewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private async void OnUpcomingDayCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not UpcomingDayCardItem day)
        {
            return;
        }

        if (DataContext is KalenderSectionViewModel vm)
        {
            vm.SelectedUpcomingDay = day;
            e.Handled = true;

            if (e.ClickCount >= 2)
            {
                await OpenManualEntriesForDateAsync(vm, day.Date);
            }
        }
    }

    private async void OnTourStopCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        if (sender is not Border card || card.DataContext is not CalendarTourStopCardItem stop)
        {
            return;
        }

        if (DataContext is not KalenderSectionViewModel vm)
        {
            return;
        }

        await vm.OpenOrderEditorAsync(stop.OrderId);
        e.Handled = true;
    }

    private async void OnOpenTourOnMapClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CalendarTourItem tour)
        {
            return;
        }

        if (DataContext is not KalenderSectionViewModel vm)
        {
            return;
        }

        await vm.OpenTourOnMapAsync(tour.TourId);
        e.Handled = true;
    }

    private async Task OpenManualEntriesForDateAsync(KalenderSectionViewModel vm, DateTime date)
    {
        try
        {
            var entries = vm.GetManualEntriesForDate(date);
            var tours = vm.GetToursForDate(date);
            var manager = new CalendarManualEntriesDayDialogWindow(date, entries, tours)
            {
                Owner = Window.GetWindow(this)
            };
            if (manager.ShowDialog() != true)
            {
                return;
            }

            if (manager.Action == CalendarManualEntryDialogAction.Add)
            {
                await OpenManualEntryEditorAsync(vm, date, null);
                return;
            }

            if (manager.SelectedManualEntry is null)
            {
                return;
            }

            if (manager.Action == CalendarManualEntryDialogAction.Delete)
            {
                var deleteResult = await vm.DeleteManualEntryAsync(manager.SelectedManualEntry.Id);
                if (!deleteResult.Success)
                {
                    ShowWarning(deleteResult.Message);
                }

                return;
            }

            if (manager.Action == CalendarManualEntryDialogAction.Edit)
            {
                var selected = entries.FirstOrDefault(x => string.Equals(x.Id, manager.SelectedManualEntry.Id, StringComparison.OrdinalIgnoreCase));
                if (selected is null)
                {
                    ShowWarning("Der gewählte Eintrag wurde nicht gefunden.");
                    return;
                }

                await OpenManualEntryEditorAsync(vm, date, selected);
            }
        }
        catch (Exception ex)
        {
            ShowWarning($"Bearbeiten fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task OpenManualEntryEditorAsync(KalenderSectionViewModel vm, DateTime date, CalendarManualEntryEditItem? existingEntry)
    {
        try
        {
            var dialog = new CalendarManualEntryDialogWindow(
                date,
                vm.ManualEntryColorOptions,
                vm.DefaultManualEntryColor,
                existingEntry)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true || dialog.EntryDate is not DateTime selectedDate)
            {
                return;
            }

            ManualEntrySaveResult result;
            if (dialog.DeleteRequested)
            {
                result = await vm.DeleteManualEntryAsync(dialog.ExistingEntryId);
            }
            else if (dialog.IsEditMode)
            {
                result = await vm.UpdateManualEntryAsync(
                    dialog.ExistingEntryId,
                    selectedDate,
                    dialog.EntryTime,
                    dialog.EntryTitle,
                    dialog.EntryDescription,
                    dialog.EntryColor);
            }
            else
            {
                result = await vm.SaveManualEntryAsync(
                    selectedDate,
                    dialog.EntryTime,
                    dialog.EntryTitle,
                    dialog.EntryDescription,
                    dialog.EntryColor);
            }

            if (!result.Success)
            {
                ShowWarning(result.Message);
            }
        }
        catch (Exception ex)
        {
            ShowWarning($"Dialog konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    private void ShowWarning(string message)
    {
        var owner = Window.GetWindow(this);
        if (owner is not null)
        {
            MessageBox.Show(owner, message, "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(message, "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
