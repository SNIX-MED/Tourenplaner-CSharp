using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class NonMapOrdersSectionView : UserControl
{
    private NonMapOrdersSectionViewModel? _viewModel;
    private PopupToggleController? _filterPopupController;
    private PopupToggleController? _columnsPopupController;

    public NonMapOrdersSectionView()
    {
        InitializeComponent();
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnAnyMouseWheel), true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAnyMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = VisualTreeUtilities.FindDescendant<ScrollViewer>(NonMapOrdersGrid);
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

    private void OnOpenOrderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NonMapOrdersSectionViewModel vm)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: OrderItem item })
        {
            NonMapOrdersGrid.SelectedItem = item;
            vm.SelectedOrder = item;
        }

        if (vm.EditSelectedOrderCommand.CanExecute(null))
        {
            vm.EditSelectedOrderCommand.Execute(null);
        }
    }

    private void NonMapOrdersGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = VisualTreeUtilities.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not OrderItem item || DataContext is not NonMapOrdersSectionViewModel vm)
        {
            return;
        }

        NonMapOrdersGrid.SelectedItem = item;
        vm.SelectedOrder = item;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsurePopupControllers();
        BindViewModel(DataContext as NonMapOrdersSectionViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DisposePopupControllers();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        BindViewModel(e.NewValue as NonMapOrdersSectionViewModel);
    }

    private void EnsurePopupControllers()
    {
        _filterPopupController ??= new PopupToggleController(
            FilterPopupButton,
            FilterPopup,
            () => _columnsPopupController?.Close());

        _columnsPopupController ??= new PopupToggleController(
            ColumnsPopupButton,
            ColumnsPopup,
            () => _filterPopupController?.Close());
    }

    private void DisposePopupControllers()
    {
        _filterPopupController?.Dispose();
        _columnsPopupController?.Dispose();
        _filterPopupController = null;
        _columnsPopupController = null;
    }

    private void BindViewModel(NonMapOrdersSectionViewModel? vm)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = vm;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateColumnVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NonMapOrdersSectionViewModel.IsCustomerColumnVisible) or
            nameof(NonMapOrdersSectionViewModel.IsDeliveryAddressColumnVisible) or
            nameof(NonMapOrdersSectionViewModel.IsDeliveryPersonColumnVisible))
        {
            UpdateColumnVisibility();
        }
    }

    private void UpdateColumnVisibility()
    {
        var vm = _viewModel;
        if (vm is null)
        {
            CustomerColumn.Visibility = Visibility.Visible;
            DeliveryAddressColumn.Visibility = Visibility.Visible;
            DeliveryPersonColumn.Visibility = Visibility.Visible;
            return;
        }

        CustomerColumn.Visibility = vm.IsCustomerColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        DeliveryAddressColumn.Visibility = vm.IsDeliveryAddressColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        DeliveryPersonColumn.Visibility = vm.IsDeliveryPersonColumnVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
