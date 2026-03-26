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

    private void OnRootScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (RootScrollViewer is null)
        {
            return;
        }

        RootScrollViewer.ScrollToVerticalOffset(RootScrollViewer.VerticalOffset - (e.Delta / 3d));
        e.Handled = true;
    }

    private void OnUpcomingDayItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item || item.DataContext is not UpcomingDayCardItem day)
        {
            return;
        }

        if (DataContext is KalenderSectionViewModel vm)
        {
            vm.SelectedUpcomingDay = day;
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
