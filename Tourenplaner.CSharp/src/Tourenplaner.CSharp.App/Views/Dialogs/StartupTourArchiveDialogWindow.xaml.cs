using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class StartupTourArchiveDialogWindow : Window
{
    public StartupTourArchiveDialogWindow(
        TourRecord tour,
        DateTime tourDate,
        int tourIndex,
        int tourCount,
        IReadOnlyList<Order> candidateOrders)
    {
        InitializeComponent();

        var tourName = string.IsNullOrWhiteSpace(tour.Name) ? $"Tour {tour.Id}" : tour.Name.Trim();
        TourProgressTextBlock.Text = $"Tour {tourIndex}/{tourCount}";
        TourTitleTextBlock.Text = $"{tourName} ({tour.Id})";
        TourMetaTextBlock.Text = $"Datum: {tourDate:dd.MM.yyyy} | Aufträge: {candidateOrders.Count}";

        foreach (var order in candidateOrders)
        {
            Orders.Add(new StartupArchiveOrderSelectionItem(
                order.Id,
                order.CustomerName,
                order.Address,
                isSelected: true));
        }

        if (Orders.Count == 0)
        {
            SelectionHintTextBlock.Text = "Diese Tour enthält keine aktiven Aufträge mehr.";
        }
        else
        {
            SelectionHintTextBlock.Text = "Haken entfernen, um einzelne Aufträge von der Archivierung auszuschließen.";
        }

        DataContext = this;
    }

    public ObservableCollection<StartupArchiveOrderSelectionItem> Orders { get; } = [];

    public bool ShouldArchiveTour { get; private set; }

    public bool CancelAll { get; private set; }

    public IReadOnlyList<string> SelectedOrderIds => Orders
        .Where(x => x.IsSelected)
        .Select(x => x.OrderId)
        .ToList();

    private void OnArchiveClicked(object sender, RoutedEventArgs e)
    {
        ShouldArchiveTour = true;
        DialogResult = true;
        Close();
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        ShouldArchiveTour = false;
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        CancelAll = true;
        DialogResult = false;
        Close();
    }
}

public sealed class StartupArchiveOrderSelectionItem
{
    public StartupArchiveOrderSelectionItem(string orderId, string customer, string address, bool isSelected)
    {
        OrderId = orderId ?? string.Empty;
        Customer = customer ?? string.Empty;
        Address = address ?? string.Empty;
        IsSelected = isSelected;
    }

    public string OrderId { get; }

    public string Customer { get; }

    public string Address { get; }

    public bool IsSelected { get; set; }
}
