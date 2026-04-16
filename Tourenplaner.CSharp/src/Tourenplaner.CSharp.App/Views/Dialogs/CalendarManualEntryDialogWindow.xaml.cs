using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class CalendarManualEntryDialogWindow : Window
{
    public CalendarManualEntryDialogWindow(
        DateTime? initialDate,
        IEnumerable<string> colorOptions,
        string defaultColor,
        CalendarManualEntryEditItem? existingEntry = null)
    {
        InitializeComponent();

        EntryDatePicker.SelectedDate = existingEntry?.Date ?? initialDate ?? DateTime.Today;
        EntryTimeTextBox.Text = existingEntry?.Time ?? string.Empty;
        EntryTitleTextBox.Text = existingEntry?.Title ?? string.Empty;
        EntryDescriptionTextBox.Text = existingEntry?.Description ?? string.Empty;

        IsEditMode = existingEntry is not null;
        ExistingEntryId = (existingEntry?.Id ?? string.Empty).Trim();
        DialogHeadlineText.Text = IsEditMode ? "Manuellen Eintrag bearbeiten" : "Manueller Kalendereintrag";
        Title = IsEditMode ? "Manuellen Eintrag bearbeiten" : "Manuellen Eintrag hinzufügen";
        SaveButton.Content = IsEditMode ? "Änderungen speichern" : "Eintrag hinzufügen";
        DeleteButton.Visibility = IsEditMode ? Visibility.Visible : Visibility.Collapsed;

        var options = (colorOptions ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (options.Count == 0)
        {
            options.Add("#0EA5E9");
        }

        var selectedHex = NormalizeHexColor(existingEntry?.ColorHex, NormalizeHexColor(defaultColor, options[0]));
        _colorOptions = options
            .Select(hex => new CalendarColorOptionItem(hex, string.Equals(hex, selectedHex, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        EntryColorComboBox.ItemsSource = _colorOptions;
        EntryColorComboBox.SelectedItem = _colorOptions.FirstOrDefault(x => x.IsDefault) ?? _colorOptions[0];
        if (EntryColorComboBox.SelectedItem is null && _colorOptions.Count > 0)
        {
            EntryColorComboBox.SelectedIndex = 0;
        }

        UpdateColorPreview((EntryColorComboBox.SelectedItem as CalendarColorOptionItem)?.Hex);
    }

    private readonly List<CalendarColorOptionItem> _colorOptions = [];

    public bool IsEditMode { get; }

    public string ExistingEntryId { get; }

    public bool DeleteRequested { get; private set; }

    public DateTime? EntryDate => EntryDatePicker.SelectedDate?.Date;

    public string EntryTime => (EntryTimeTextBox.Text ?? string.Empty).Trim();

    public string EntryTitle => (EntryTitleTextBox.Text ?? string.Empty).Trim();

    public string EntryDescription => (EntryDescriptionTextBox.Text ?? string.Empty).Trim();

    public string EntryColor => NormalizeHexColor((EntryColorComboBox.SelectedItem as CalendarColorOptionItem)?.Hex, "#0EA5E9");

    private void OnColorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateColorPreview((EntryColorComboBox.SelectedItem as CalendarColorOptionItem)?.Hex);
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (EntryDate is null)
        {
            MessageBox.Show(this, "Bitte ein Datum auswählen.", "Kalender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(EntryTitle))
        {
            MessageBox.Show(this, "Bitte einen Titel eingeben.", "Kalender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!string.IsNullOrWhiteSpace(EntryTime) &&
            !TimeSpan.TryParse(EntryTime, CultureInfo.InvariantCulture, out _))
        {
            MessageBox.Show(this, "Zeitformat ungültig. Bitte HH:mm verwenden (z. B. 08:30).", "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DeleteRequested = false;
        DialogResult = true;
        Close();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "Diesen manuellen Eintrag wirklich löschen?",
            "Kalender",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteRequested = true;
        DialogResult = true;
        Close();
    }

    private void UpdateColorPreview(string? candidate)
    {
        var normalized = NormalizeHexColor(candidate, "#0EA5E9");
        var converted = new BrushConverter().ConvertFromString(normalized) as Brush;
        ColorPreviewBorder.Background = converted ?? new SolidColorBrush(Colors.DeepSkyBlue);
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 7 && normalized.StartsWith('#') ? normalized : fallback;
    }

}

public sealed class CalendarColorOptionItem
{
    public CalendarColorOptionItem(string hex, bool isDefault)
    {
        Hex = hex;
        IsDefault = isDefault;
        Label = isDefault ? $"{hex} (Standard)" : hex;
    }

    public string Hex { get; }

    public bool IsDefault { get; }

    public string Label { get; }
}
