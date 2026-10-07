using System.Globalization;
using System.Windows;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class RouteStartTimeDialogWindow : Window
{
    public RouteStartTimeDialogWindow(string? currentTime)
    {
        InitializeComponent();

        HourComboBox.ItemsSource = Enumerable.Range(0, 24)
            .Select(value => value.ToString("00", CultureInfo.InvariantCulture))
            .ToList();
        MinuteComboBox.ItemsSource = Enumerable.Range(0, 60)
            .Select(value => value.ToString("00", CultureInfo.InvariantCulture))
            .ToList();

        var parts = (currentTime ?? string.Empty).Split(':');
        HourComboBox.SelectedItem = parts.Length > 0 && HourComboBox.Items.Contains(parts[0]) ? parts[0] : "08";
        MinuteComboBox.SelectedItem = parts.Length > 1 && MinuteComboBox.Items.Contains(parts[1]) ? parts[1] : "00";
    }

    public string SelectedTime { get; private set; } = "08:00";

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        SelectedTime = $"{HourComboBox.SelectedItem ?? "08"}:{MinuteComboBox.SelectedItem ?? "00"}";
        DialogResult = true;
    }
}
