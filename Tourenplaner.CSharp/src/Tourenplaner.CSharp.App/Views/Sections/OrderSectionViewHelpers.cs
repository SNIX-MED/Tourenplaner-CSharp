using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

internal static class OrderSectionViewHelpers
{
    public static void HandleMouseWheel(DataGrid grid, MouseWheelEventArgs e)
    {
        var viewer = VisualTreeUtilities.FindDescendant<ScrollViewer>(grid);
        if (viewer is null || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        var target = viewer.VerticalOffset - (e.Delta / 3d);
        if (target < 0)
        {
            target = 0;
        }
        else if (target > viewer.ScrollableHeight)
        {
            target = viewer.ScrollableHeight;
        }

        viewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    public static void HandleOpenOrderClick<TViewModel>(
        object? dataContext,
        object sender,
        DataGrid grid,
        Action<TViewModel, OrderItem> setSelection)
        where TViewModel : class
    {
        if (dataContext is not TViewModel vm)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: OrderItem item })
        {
            grid.SelectedItem = item;
            setSelection(vm, item);
        }
    }

    public static void HandlePreviewMouseRightButtonDown<TViewModel>(
        object? dataContext,
        object? originalSource,
        DataGrid grid,
        Action<TViewModel, OrderItem> setSelection)
        where TViewModel : class
    {
        var row = VisualTreeUtilities.FindAncestor<DataGridRow>(originalSource as DependencyObject);
        if (row?.Item is not OrderItem item || dataContext is not TViewModel vm)
        {
            return;
        }

        grid.SelectedItem = item;
        setSelection(vm, item);
    }

    public static void ApplyColumnVisibility(
        bool customerVisible,
        bool deliveryAddressVisible,
        bool deliveryPersonVisible,
        DataGridColumn customerColumn,
        DataGridColumn deliveryAddressColumn,
        DataGridColumn deliveryPersonColumn)
    {
        customerColumn.Visibility = customerVisible ? Visibility.Visible : Visibility.Collapsed;
        deliveryAddressColumn.Visibility = deliveryAddressVisible ? Visibility.Visible : Visibility.Collapsed;
        deliveryPersonColumn.Visibility = deliveryPersonVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
