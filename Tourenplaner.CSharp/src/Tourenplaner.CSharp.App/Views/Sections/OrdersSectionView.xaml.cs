using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class OrdersSectionView : UserControl
{
    public OrdersSectionView()
    {
        InitializeComponent();
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnAnyMouseWheel), true);
    }

    private void OnAnyMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = FindDescendantScrollViewer(OrdersGrid);
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

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject? parent)
    {
        if (parent is null)
        {
            return null;
        }

        if (parent is ScrollViewer viewer)
        {
            return viewer;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var nested = FindDescendantScrollViewer(VisualTreeHelper.GetChild(parent, i));
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void OnOpenOrderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OrdersSectionViewModel vm)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: OrderItem item })
        {
            OrdersGrid.SelectedItem = item;
            vm.SelectedOrder = item;
        }

        if (vm.EditSelectedOrderCommand.CanExecute(null))
        {
            vm.EditSelectedOrderCommand.Execute(null);
        }
    }
}
