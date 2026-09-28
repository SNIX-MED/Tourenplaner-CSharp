using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class PinAddressInfoDialogWindow : Window
{
    private readonly string _originalAddress;
    private readonly string _foundAddress;
    public bool UseFoundAddress { get; private set; }

    public PinAddressInfoDialogWindow(Order order, string originalAddress,
        AddressGeocodingResult? result, string failureSummary)
    {
        InitializeComponent();
        _originalAddress = originalAddress;
        _foundAddress = result?.ResultFreeformAddress ?? string.Empty;
        OrderText.Text = $"Auftrag {order.Id} · {order.CustomerName}";
        OriginalText.Text = originalAddress;
        FoundText.Text = string.IsNullOrWhiteSpace(_foundAddress) ? "Keine vollständige Trefferadresse verfügbar." : _foundAddress;
        FoundChoice.IsEnabled = PinAddressComparison.CanUseFoundAddress(result);
        FoundLinks.IsEnabled = !string.IsNullOrWhiteSpace(_foundAddress);
        ConfirmChoice.IsEnabled = FoundChoice.IsEnabled;
        ProblemText.Text = result is null ? failureSummary : result.IsPrecise
            ? "Die Adresse wurde inzwischen genau zugeordnet."
            : $"TomTom meldet „{result.MatchType}“. Der Treffer stimmt nicht vollständig mit der Lieferadresse überein oder ist kein genauer Hausadresspunkt.";
        if (result is not null)
        {
            Differences.ItemsSource = PinAddressComparison.Compare(order.DeliveryAddress, PinAddressComparison.GetFoundAddress(result));
            PositionText.Text = string.Format(CultureInfo.InvariantCulture,
                "Kartenposition: {0:F6}, {1:F6}. Bei Übernahme werden Lieferadresse und dieser Kartenpunkt gespeichert.",
                result.Location.Latitude, result.Location.Longitude);
            if (!FoundChoice.IsEnabled)
                PositionText.Text += " Der Treffer ist unvollständig; bitte die Lieferadresse über „Bearbeiten“ korrigieren.";
            if (result.MatchType is not "Point Address")
                PositionText.Text += " Achtung: Dieser Treffer bezeichnet kein einzelnes Haus; die Position kann nur ungefähr sein.";
        }
    }

    private string AddressFor(object sender) => (sender as Button)?.Tag as string == "found" ? _foundAddress : _originalAddress;

    private void Google_Click(object sender, RoutedEventArgs e) => OpenMap(PinAddressComparison.GoogleMapsUrl(AddressFor(sender)));

    private void TomTom_Click(object sender, RoutedEventArgs e) => OpenMap(PinAddressComparison.TomTomUrl(AddressFor(sender)));

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AddressFor(sender) + ", Schweiz");
            StatusText.Text = "Adresse kopiert. Mit Strg+V in die Kartensuche einfügen.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Adresse konnte nicht kopiert werden: {ex.Message}";
        }
    }

    private bool OpenMap(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Karte konnte nicht geöffnet werden: {ex.Message}";
            return false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (FoundChoice.IsChecked == true && ConfirmChoice.IsChecked != true)
        {
            StatusText.Text = "Bitte zuerst bestätigen, dass die gefundene Adresse und Position geprüft wurden.";
            return;
        }
        UseFoundAddress = FoundChoice.IsChecked == true;
        DialogResult = true;
    }
}
