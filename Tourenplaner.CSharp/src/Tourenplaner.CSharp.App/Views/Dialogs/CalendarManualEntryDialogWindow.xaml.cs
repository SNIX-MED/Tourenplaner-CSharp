using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class CalendarManualEntryDialogWindow : Window
{
    private const string DefaultManualColorHex = "#7DD3FC";
    private const string EmptyTimePart = "--";

    public CalendarManualEntryDialogWindow(
        DateTime? initialDate,
        IEnumerable<string> colorOptions,
        string defaultColor,
        CalendarManualEntryEditItem? existingEntry = null)
    {
        InitializeComponent();

        var selectedDate = existingEntry?.Date ?? initialDate ?? DateTime.Today;
        EntryDatePicker.SelectedDateText = selectedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        EntryTitleTextBox.Text = existingEntry?.Title ?? string.Empty;
        EntryDescriptionTextBox.Text = existingEntry?.Description ?? string.Empty;

        _hourOptions = [EmptyTimePart, .. Enumerable.Range(0, 24).Select(x => x.ToString("00", CultureInfo.InvariantCulture))];
        _minuteOptions = [EmptyTimePart, .. Enumerable.Range(0, 60).Select(x => x.ToString("00", CultureInfo.InvariantCulture))];
        EntryHourComboBox.ItemsSource = _hourOptions;
        EntryMinuteComboBox.ItemsSource = _minuteOptions;

        if (TryParseTimeParts(existingEntry?.Time, out var existingHour, out var existingMinute))
        {
            EntryHourComboBox.SelectedItem = existingHour;
            EntryMinuteComboBox.SelectedItem = existingMinute;
        }
        else
        {
            EntryHourComboBox.SelectedItem = EmptyTimePart;
            EntryMinuteComboBox.SelectedItem = EmptyTimePart;
        }

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
            options.Add(DefaultManualColorHex);
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
    }

    private readonly List<CalendarColorOptionItem> _colorOptions = [];
    private readonly IReadOnlyList<string> _hourOptions;
    private readonly IReadOnlyList<string> _minuteOptions;

    public bool IsEditMode { get; }

    public string ExistingEntryId { get; }

    public bool DeleteRequested { get; private set; }

    public DateTime? EntryDate
    {
        get
        {
            var raw = (EntryDatePicker.SelectedDateText ?? string.Empty).Trim();
            if (DateTime.TryParseExact(raw, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.Date;
            }

            return null;
        }
    }

    public string EntryTime
    {
        get
        {
            var hour = (EntryHourComboBox.SelectedItem as string ?? string.Empty).Trim();
            var minute = (EntryMinuteComboBox.SelectedItem as string ?? string.Empty).Trim();
            if (string.Equals(hour, EmptyTimePart, StringComparison.Ordinal) ||
                string.Equals(minute, EmptyTimePart, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return _hourOptions.Contains(hour) && _minuteOptions.Contains(minute)
                ? $"{hour}:{minute}"
                : string.Empty;
        }
    }

    public string EntryTitle => (EntryTitleTextBox.Text ?? string.Empty).Trim();

    public string EntryDescription => (EntryDescriptionTextBox.Text ?? string.Empty).Trim();

    public string EntryColor => NormalizeHexColor((EntryColorComboBox.SelectedItem as CalendarColorOptionItem)?.Hex, DefaultManualColorHex);

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

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 7 && normalized.StartsWith('#') ? normalized : fallback;
    }

    private static bool TryParseTimeParts(string? value, out string hour, out string minute)
    {
        hour = string.Empty;
        minute = string.Empty;

        var raw = (value ?? string.Empty).Trim();
        if (!TimeSpan.TryParseExact(raw, "hh\\:mm", CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        hour = parsed.Hours.ToString("00", CultureInfo.InvariantCulture);
        minute = parsed.Minutes.ToString("00", CultureInfo.InvariantCulture);
        return true;
    }

}

public sealed class CalendarColorOptionItem
{
    public CalendarColorOptionItem(string hex, bool isDefault)
    {
        Hex = hex;
        IsDefault = isDefault;
        var baseLabel = ResolveColorName(hex);
        Label = isDefault ? $"{baseLabel} (Standard)" : baseLabel;
    }

    public string Hex { get; }

    public bool IsDefault { get; }

    public string Label { get; }

    private static string ResolveColorName(string? hex)
    {
        return (hex ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "#7DD3FC" => "Hellblau",
            "#1D4ED8" => "Dunkelblau",
            "#86EFAC" => "Hellgrün",
            "#15803D" => "Dunkelgrün",
            "#DC2626" => "Rot",
            "#F97316" => "Orange",
            "#FACC15" => "Gelb",
            "#6B7280" => "Grau",
            _ => "Farbe"
        };
    }
}
