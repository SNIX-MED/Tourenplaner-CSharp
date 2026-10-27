using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class StartupTourArchiveDialogWindow : Window
{
    public StartupTourArchiveDialogWindow(
        TourRecord tour,
        DateTime tourDate,
        int tourIndex,
        int tourCount,
        IReadOnlyList<Order> orders)
    {
        InitializeComponent();

        var tourName = string.IsNullOrWhiteSpace(tour.Name) ? $"Tour {tour.Id}" : tour.Name.Trim();
        TourProgressTextBlock.Text = $"Tour {tourIndex}/{tourCount}";
        TourTitleTextBlock.Text = $"{tourName} ({tour.Id})";
        var ordersById = orders
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var targets = new TourArchiveOrderService().GetTargets(tour, orders);
        TourMetaTextBlock.Text = $"Datum: {tourDate:dd.MM.yyyy} | Lieferungen: {targets.Count}";

        foreach (var target in targets)
        {
            if (!ordersById.TryGetValue(target.OrderId, out var order))
            {
                continue;
            }

            Orders.Add(new StartupArchiveOrderSelectionItem(
                target.SelectionKey,
                order.Id,
                target.DeliverySequenceNumber.HasValue
                    ? $"Teillieferung {target.DeliverySequenceNumber.Value}"
                    : "Vollständiger Auftrag",
                order.CustomerName,
                order.Address,
                isSelected: true));
        }

        if (Orders.Count == 0)
        {
            SelectionHintTextBlock.Text = "Diese Tour enthält keine aktiven Lieferungen mehr.";
        }
        else
        {
            SelectionHintTextBlock.Text = "Haken entfernen, um Lieferungen wieder zur Planung freizugeben.";
        }

        DataContext = this;
    }

    public ObservableCollection<StartupArchiveOrderSelectionItem> Orders { get; } = [];

    public bool ShouldArchiveTour { get; private set; }

    public bool CancelAll { get; private set; }

    public IReadOnlySet<string> SelectedTargetKeys => Orders
        .Where(x => x.IsSelected)
        .Select(x => x.SelectionKey)
        .ToHashSet(StringComparer.Ordinal);

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
    public StartupArchiveOrderSelectionItem(
        string selectionKey,
        string orderId,
        string deliveryLabel,
        string customer,
        string address,
        bool isSelected)
    {
        SelectionKey = selectionKey ?? string.Empty;
        OrderId = orderId ?? string.Empty;
        DeliveryLabel = deliveryLabel ?? string.Empty;
        Customer = customer ?? string.Empty;
        Address = address ?? string.Empty;
        IsSelected = isSelected;
    }

    public string SelectionKey { get; }

    public string OrderId { get; }

    public string DeliveryLabel { get; }

    public string Customer { get; }

    public string Address { get; }

    public bool IsSelected { get; set; }
}
