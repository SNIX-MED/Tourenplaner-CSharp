using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class CalendarManualEntriesDayDialogWindow : Window
{
    public CalendarManualEntriesDayDialogWindow(
        DateTime date,
        IReadOnlyList<CalendarManualEntryEditItem> manualEntries,
        IReadOnlyList<CalendarTourDayListItem> tours)
    {
        InitializeComponent();
        HeadlineText.Text = $"Einträge am {date:dd.MM.yyyy}";

        var items = new List<CalendarDayCombinedListItem>();

        foreach (var manual in manualEntries ?? [])
        {
            items.Add(new CalendarDayCombinedListItem
            {
                IsManual = true,
                ManualEntry = manual,
                SortMinutes = ParseMinutes(manual.Time),
                TimeDisplay = string.IsNullOrWhiteSpace(manual.Time) ? string.Empty : manual.Time,
                Title = manual.Title,
                Headline = string.IsNullOrWhiteSpace(manual.Time)
                    ? manual.Title
                    : $"{manual.Time} {manual.Title}".Trim(),
                Description = manual.Description,
                KindLabel = "Manueller Eintrag",
                ColorHex = manual.ColorHex
            });
        }

        foreach (var tour in tours ?? [])
        {
            items.Add(new CalendarDayCombinedListItem
            {
                IsManual = false,
                TourEntry = tour,
                SortMinutes = ParseMinutes(tour.Time),
                TimeDisplay = string.IsNullOrWhiteSpace(tour.Time) ? string.Empty : tour.Time,
                Title = tour.Name,
                Headline = string.IsNullOrWhiteSpace(tour.Time)
                    ? tour.Name
                    : $"{tour.Time} {tour.Name}".Trim(),
                Description = tour.Summary,
                KindLabel = "Liefertour",
                ColorHex = "#475569"
            });
        }

        Items = items
            .OrderBy(x => x.SortMinutes)
            .ThenByDescending(x => x.IsManual)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        EntriesListBox.ItemsSource = Items;
        EntriesListBox.SelectedItem = Items.FirstOrDefault();
    }

    public IReadOnlyList<CalendarDayCombinedListItem> Items { get; }

    public CalendarDayCombinedListItem? SelectedItem => EntriesListBox.SelectedItem as CalendarDayCombinedListItem;

    public CalendarManualEntryEditItem? SelectedManualEntry => SelectedItem?.ManualEntry;

    public CalendarManualEntryDialogAction Action { get; private set; } = CalendarManualEntryDialogAction.None;

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        Action = CalendarManualEntryDialogAction.Add;
        DialogResult = true;
        Close();
    }

    private void OnEditClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedManualEntry is null)
        {
            MessageBox.Show(this, "Bitte zuerst einen manuellen Eintrag auswählen.", "Kalender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Action = CalendarManualEntryDialogAction.Edit;
        DialogResult = true;
        Close();
    }

    private void OnEditClickedFromDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OnEditClicked(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedManualEntry is null)
        {
            MessageBox.Show(this, "Bitte zuerst einen manuellen Eintrag auswählen.", "Kalender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Action = CalendarManualEntryDialogAction.Delete;
        DialogResult = true;
        Close();
    }

    private static int ParseMinutes(string? time)
    {
        var value = (time ?? string.Empty).Trim();
        if (TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out var exact))
        {
            return (int)exact.TotalMinutes;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return (int)parsed.TotalMinutes;
        }

        return int.MaxValue;
    }
}

public enum CalendarManualEntryDialogAction
{
    None = 0,
    Add = 1,
    Edit = 2,
    Delete = 3
}

public sealed class CalendarDayCombinedListItem
{
    public bool IsManual { get; set; }

    public CalendarManualEntryEditItem? ManualEntry { get; set; }

    public CalendarTourDayListItem? TourEntry { get; set; }

    public int SortMinutes { get; set; } = int.MaxValue;

    public string TimeDisplay { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Headline { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string KindLabel { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#475569";
}
