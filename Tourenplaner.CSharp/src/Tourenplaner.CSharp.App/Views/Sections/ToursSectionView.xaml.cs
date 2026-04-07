using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class ToursSectionView : UserControl
{
    private Point? _stopsGridDragStart;
    private TourStopOverviewItem? _stopsGridDragItem;
    private ListBoxItem? _activeStopsDropRow;
    private DataGridRow? _activeToursDropRow;
    private Brush? _dropHighlightBrush;

    public ToursSectionView()
    {
        InitializeComponent();
    }

    private void OnToursGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ToursSectionViewModel vm)
        {
            return;
        }

        if (vm.EditTourOnMapCommand.CanExecute(null))
        {
            vm.EditTourOnMapCommand.Execute(null);
        }
    }

    private void SelectedTourStopsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _stopsGridDragStart = null;
        _stopsGridDragItem = null;

        var itemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (itemContainer?.DataContext is not TourStopOverviewItem item || item.IsCompanyStop)
        {
            return;
        }

        _stopsGridDragStart = e.GetPosition(SelectedTourStopsGrid);
        _stopsGridDragItem = item;
    }

    private void SelectedTourStopsGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_stopsGridDragStart is null || _stopsGridDragItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(SelectedTourStopsGrid);
        var delta = current - _stopsGridDragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var draggedItem = _stopsGridDragItem;
        _stopsGridDragStart = null;
        _stopsGridDragItem = null;

        DragDrop.DoDragDrop(
            SelectedTourStopsGrid,
            new DataObject(typeof(TourStopOverviewItem), draggedItem),
            DragDropEffects.Move);
    }

    private void SelectedTourStopsGrid_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not ToursSectionViewModel vm ||
            !e.Data.GetDataPresent(typeof(TourStopOverviewItem)))
        {
            ClearDropMarker(ref _activeStopsDropRow);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var source = e.Data.GetData(typeof(TourStopOverviewItem)) as TourStopOverviewItem;
        if (source is null || source.IsCompanyStop || vm.SelectedTour is null || source.SourceTourId != vm.SelectedTour.TourId)
        {
            ClearDropMarker(ref _activeStopsDropRow);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var targetRow = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = targetRow?.DataContext as TourStopOverviewItem;
        if (target is not null &&
            (target.IsCompanyStop || ReferenceEquals(target, source)))
        {
            ClearDropMarker(ref _activeStopsDropRow);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        SetDropMarker(ref _activeStopsDropRow, targetRow);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void SelectedTourStopsGrid_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropMarker(ref _activeStopsDropRow);
    }

    private async void SelectedTourStopsGrid_Drop(object sender, DragEventArgs e)
    {
        ClearDropMarker(ref _activeStopsDropRow);
        if (DataContext is not ToursSectionViewModel vm ||
            !e.Data.GetDataPresent(typeof(TourStopOverviewItem)))
        {
            return;
        }

        var source = e.Data.GetData(typeof(TourStopOverviewItem)) as TourStopOverviewItem;
        if (source is null)
        {
            return;
        }

        var targetRow = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = targetRow?.DataContext as TourStopOverviewItem;
        var moved = await vm.MoveStopWithinSelectedTourAsync(source, target);
        if (moved)
        {
            SelectedTourStopsGrid.Items.Refresh();
        }
    }

    private void ToursGrid_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not ToursSectionViewModel ||
            !e.Data.GetDataPresent(typeof(TourStopOverviewItem)))
        {
            ClearDropMarker(ref _activeToursDropRow);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var source = e.Data.GetData(typeof(TourStopOverviewItem)) as TourStopOverviewItem;
        var targetRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        var targetTour = targetRow?.Item as TourOverviewItem;
        if (source is null || source.IsCompanyStop || targetTour is null || source.SourceTourId == targetTour.TourId)
        {
            ClearDropMarker(ref _activeToursDropRow);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        SetDropMarker(ref _activeToursDropRow, targetRow);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ToursGrid_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropMarker(ref _activeToursDropRow);
    }

    private async void ToursGrid_Drop(object sender, DragEventArgs e)
    {
        ClearDropMarker(ref _activeToursDropRow);
        if (DataContext is not ToursSectionViewModel vm ||
            !e.Data.GetDataPresent(typeof(TourStopOverviewItem)))
        {
            return;
        }

        var source = e.Data.GetData(typeof(TourStopOverviewItem)) as TourStopOverviewItem;
        var targetRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        var targetTour = targetRow?.Item as TourOverviewItem;
        if (source is null || targetTour is null)
        {
            return;
        }

        var moved = await vm.MoveStopToTourAsync(source, targetTour);
        if (moved)
        {
            ToursGrid.SelectedItem = vm.SelectedTour;
            ToursGrid.ScrollIntoView(vm.SelectedTour);
        }
    }

    private void SetDropMarker(ref DataGridRow? currentRow, DataGridRow? nextRow)
    {
        if (ReferenceEquals(currentRow, nextRow))
        {
            return;
        }

        ClearDropMarker(ref currentRow);
        if (nextRow is null)
        {
            return;
        }

        currentRow = nextRow;
        currentRow.Background = ResolveDropHighlightBrush();
    }

    private void SetDropMarker(ref ListBoxItem? currentRow, ListBoxItem? nextRow)
    {
        if (ReferenceEquals(currentRow, nextRow))
        {
            return;
        }

        ClearDropMarker(ref currentRow);
        if (nextRow is null)
        {
            return;
        }

        currentRow = nextRow;
        currentRow.Background = ResolveDropHighlightBrush();
    }

    private void ClearDropMarker(ref DataGridRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.ClearValue(DataGridRow.BackgroundProperty);
        row = null;
    }

    private void ClearDropMarker(ref ListBoxItem? row)
    {
        if (row is null)
        {
            return;
        }

        row.ClearValue(ListBoxItem.BackgroundProperty);
        row = null;
    }

    private Brush ResolveDropHighlightBrush()
    {
        if (_dropHighlightBrush is null)
        {
            _dropHighlightBrush = (Brush)(TryFindResource("Brush.AccentSoft") ??
                                          TryFindResource("Brush.RowHover") ??
                                          Brushes.LightBlue);
        }

        return _dropHighlightBrush;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
