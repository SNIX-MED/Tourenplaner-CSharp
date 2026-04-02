using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;

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

    private void OnUpcomingDayCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not UpcomingDayCardItem day)
        {
            return;
        }

        if (DataContext is KalenderSectionViewModel vm)
        {
            vm.SelectedUpcomingDay = day;
            e.Handled = true;
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
}
