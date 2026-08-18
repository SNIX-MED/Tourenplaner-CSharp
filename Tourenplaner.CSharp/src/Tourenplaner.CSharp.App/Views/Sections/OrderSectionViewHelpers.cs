using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

internal static class OrderSectionViewHelpers
{
    public static void HandleMouseWheel(DataGrid grid, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            !ReferenceEquals(VisualTreeUtilities.FindAncestor<DataGrid>(source), grid))
        {
            return;
        }

        var viewer = VisualTreeUtilities.FindDescendant<ScrollViewer>(grid);
        if (viewer is null || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        var target = viewer.CanContentScroll
            ? viewer.VerticalOffset - GetLogicalMouseWheelDelta(e.Delta)
            : viewer.VerticalOffset - (e.Delta / 3d);
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

    private static double GetLogicalMouseWheelDelta(int delta)
    {
        var notches = Math.Max(1d, Math.Abs(delta) / (double)Mouse.MouseWheelDeltaForOneLine);
        var linesPerNotch = SystemParameters.WheelScrollLines > 0
            ? SystemParameters.WheelScrollLines
            : 3;

        return Math.Sign(delta) * linesPerNotch * notches;
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

        if (grid.SelectionMode == DataGridSelectionMode.Single)
        {
            grid.SelectedItem = item;
            grid.CurrentItem = item;
            setSelection(vm, item);
            return;
        }

        if (!grid.SelectedItems.Contains(item))
        {
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(item);
        }
        else
        {
            // DataGrid clears an extended selection during its own right-click handling.
            // Restore it after that internal handling, before the context-menu command runs.
            var preservedSelection = grid.SelectedItems.Cast<object>().ToList();
            _ = grid.Dispatcher.BeginInvoke(() =>
            {
                grid.SelectedItems.Clear();
                foreach (var selectedItem in preservedSelection)
                {
                    grid.SelectedItems.Add(selectedItem);
                }

                grid.CurrentItem = item;
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        grid.CurrentItem = item;
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
