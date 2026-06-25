using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class OrdersSectionView : UserControl
{
    private OrdersSectionViewModel? _viewModel;

    public OrdersSectionView()
    {
        InitializeComponent();
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnAnyMouseWheel), true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAnyMouseWheel(object sender, MouseWheelEventArgs e)
    {
        OrderSectionViewHelpers.HandleMouseWheel(OrdersGrid, e);
    }

    private void OnOpenOrderClick(object sender, RoutedEventArgs e)
    {
        OrderSectionViewHelpers.HandleOpenOrderClick<OrdersSectionViewModel>(
            DataContext,
            sender,
            OrdersGrid,
            static (vm, item) => vm.SelectedOrder = item);

        if (DataContext is not OrdersSectionViewModel vm)
        {
            return;
        }
        if (vm.EditSelectedOrderCommand.CanExecute(null))
        {
            vm.EditSelectedOrderCommand.Execute(null);
        }
    }

    private void OrdersGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        OrderSectionViewHelpers.HandlePreviewMouseRightButtonDown<OrdersSectionViewModel>(
            DataContext,
            e.OriginalSource,
            OrdersGrid,
            static (vm, item) => vm.SelectedOrder = item);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BindViewModel(DataContext as OrdersSectionViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) { }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        BindViewModel(e.NewValue as OrdersSectionViewModel);
    }

    private void BindViewModel(OrdersSectionViewModel? vm)
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
        if (e.PropertyName is nameof(OrdersSectionViewModel.IsCustomerColumnVisible) or
            nameof(OrdersSectionViewModel.IsDeliveryAddressColumnVisible) or
            nameof(OrdersSectionViewModel.IsDeliveryPersonColumnVisible))
        {
            UpdateColumnVisibility();
        }
    }

    private void UpdateColumnVisibility()
    {
        var vm = _viewModel;
        if (vm is null)
        {
            OrderSectionViewHelpers.ApplyColumnVisibility(
                customerVisible: true,
                deliveryAddressVisible: true,
                deliveryPersonVisible: true,
                CustomerColumn,
                DeliveryAddressColumn,
                DeliveryPersonColumn);
            return;
        }

        OrderSectionViewHelpers.ApplyColumnVisibility(
            vm.IsCustomerColumnVisible,
            vm.IsDeliveryAddressColumnVisible,
            vm.IsDeliveryPersonColumnVisible,
            CustomerColumn,
            DeliveryAddressColumn,
            DeliveryPersonColumn);
    }
}
