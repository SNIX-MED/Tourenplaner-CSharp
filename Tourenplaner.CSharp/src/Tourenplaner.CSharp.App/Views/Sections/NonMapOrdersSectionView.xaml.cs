using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class NonMapOrdersSectionView : UserControl
{
    private NonMapOrdersSectionViewModel? _viewModel;

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
        OrderSectionViewHelpers.HandleMouseWheel(NonMapOrdersGrid, e);
    }

    private void OnOpenOrderClick(object sender, RoutedEventArgs e)
    {
        OrderSectionViewHelpers.HandleOpenOrderClick<NonMapOrdersSectionViewModel>(
            DataContext,
            sender,
            NonMapOrdersGrid,
            static (vm, item) => vm.SelectedOrder = item);

        if (DataContext is not NonMapOrdersSectionViewModel vm)
        {
            return;
        }
        if (vm.EditSelectedOrderCommand.CanExecute(null))
        {
            vm.EditSelectedOrderCommand.Execute(null);
        }
    }

    private void NonMapOrdersGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        OrderSectionViewHelpers.HandlePreviewMouseRightButtonDown<NonMapOrdersSectionViewModel>(
            DataContext,
            e.OriginalSource,
            NonMapOrdersGrid,
            static (vm, item) => vm.SelectedOrder = item);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BindViewModel(DataContext as NonMapOrdersSectionViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) { }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        BindViewModel(e.NewValue as NonMapOrdersSectionViewModel);
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
