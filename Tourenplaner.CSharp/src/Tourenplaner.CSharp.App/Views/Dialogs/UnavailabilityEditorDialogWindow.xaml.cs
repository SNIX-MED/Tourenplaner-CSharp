using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class UnavailabilityEditorDialogWindow : Window
{
    public UnavailabilityEditorDialogWindow(IEnumerable<ResourceUnavailabilityPeriod>? periods)
    {
        InitializeComponent();
        ViewModel = new UnavailabilityEditorDialogViewModel(periods);
        DataContext = ViewModel;
    }

    public UnavailabilityEditorDialogViewModel ViewModel { get; }
    public IReadOnlyList<ResourceUnavailabilityPeriod> Result { get; private set; } = [];

    private void OnAddClicked(object sender, RoutedEventArgs e) => ViewModel.AddPeriod();
    private void OnRemoveClicked(object sender, RoutedEventArgs e) => ViewModel.RemoveSelectedPeriod();
    private void OnRemoveRowClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UnavailabilityPeriodEditItem item }) ViewModel.Periods.Remove(item);
    }
    private void OnCancelClicked(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void OnTimePreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var value = BuildTextAfterInsertion(textBox, e.Text);
        if (value.Length == 2 && value.All(char.IsDigit) && IsValidTimeInput(value))
        {
            SetTimeText(textBox, value + ":");
            e.Handled = true;
            return;
        }

        e.Handled = !IsValidTimeInput(value);
    }

    private void OnTimePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox || !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = (e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty).Trim();
        if (pastedText.Length == 4 && pastedText.All(char.IsDigit))
        {
            pastedText = pastedText.Insert(2, ":");
        }

        var value = BuildTextAfterInsertion(textBox, pastedText);
        if (!IsValidTimeInput(value))
        {
            e.CancelCommand();
            return;
        }

        SetTimeText(textBox, value);
        e.CancelCommand();
    }

    private static void SetTimeText(TextBox textBox, string value)
    {
        textBox.Text = value;
        textBox.CaretIndex = value.Length;
    }

    private static string BuildTextAfterInsertion(TextBox textBox, string insertedText)
    {
        var current = textBox.Text ?? string.Empty;
        return current.Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, insertedText);
    }

    private static bool IsValidTimeInput(string value)
    {
        if (value.Length > 5) return false;
        for (var index = 0; index < value.Length; index++)
        {
            if (index == 2)
            {
                if (value[index] != ':') return false;
            }
            else if (!char.IsDigit(value[index]))
            {
                return false;
            }
        }

        if (value.Length >= 1 && value[0] > '2') return false;
        if (value.Length >= 2 && value[0] == '2' && value[1] > '3') return false;
        if (value.Length >= 4 && value[3] > '5') return false;
        return true;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out var result, out var error))
        {
            AppMessageBox.Show(this, error, "Abwesenheiten prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = result;
        DialogResult = true;
        Close();
    }
}

public sealed class UnavailabilityEditorDialogViewModel
{
    public UnavailabilityEditorDialogViewModel(IEnumerable<ResourceUnavailabilityPeriod>? periods)
    {
        foreach (var period in periods ?? [])
        {
            Periods.Add(UnavailabilityPeriodEditItem.FromModel(period));
        }
    }

    public ObservableCollection<UnavailabilityPeriodEditItem> Periods { get; } = [];
    public UnavailabilityPeriodEditItem? SelectedPeriod { get; set; }

    public void AddPeriod()
    {
        var today = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var item = new UnavailabilityPeriodEditItem { StartDate = today, EndDate = today };
        Periods.Add(item);
        SelectedPeriod = item;
    }

    public void RemoveSelectedPeriod()
    {
        if (SelectedPeriod is not null) Periods.Remove(SelectedPeriod);
    }

    public bool TryBuildResult(out IReadOnlyList<ResourceUnavailabilityPeriod> result, out string error)
        => TryBuildPeriods(Periods, out result, out error);

    public static bool TryBuildPeriods(
        IEnumerable<UnavailabilityPeriodEditItem> source,
        out IReadOnlyList<ResourceUnavailabilityPeriod> result,
        out string error)
    {
        var rows = source.ToList();
        var normalized = new List<ResourceUnavailabilityPeriod>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var start = ResourceAvailabilityService.ParseDate(row.StartDate);
            var end = ResourceAvailabilityService.ParseDate(row.EndDate);
            if (!start.HasValue || !end.HasValue)
            {
                result = [];
                error = $"Eintrag {index + 1}: Bitte gültige Datumswerte im Format DD.MM.YYYY eingeben.";
                return false;
            }
            if (!TryNormalizeTime(row.StartTime, out var startTime) || !TryNormalizeTime(row.EndTime, out var endTime))
            {
                result = [];
                error = $"Eintrag {index + 1}: Uhrzeiten müssen leer sein oder das Format HH:mm verwenden.";
                return false;
            }
            var from = start.Value;
            var to = end.Value;
            if (to < from || (to == from && startTime.Length > 0 && endTime.Length > 0 && string.CompareOrdinal(endTime, startTime) < 0))
            {
                result = [];
                error = $"Eintrag {index + 1}: Das Ende darf nicht vor dem Beginn liegen.";
                return false;
            }
            normalized.Add(new ResourceUnavailabilityPeriod
            {
                StartDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StartTime = startTime,
                EndTime = endTime,
                Note = (row.Note ?? string.Empty).Trim()
            });
        }
        result = normalized;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeTime(string? raw, out string normalized)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) { normalized = string.Empty; return true; }
        if (TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { normalized = time.ToString("HH:mm", CultureInfo.InvariantCulture); return true; }
        normalized = string.Empty;
        return false;
    }
}

public sealed class UnavailabilityPeriodEditItem
{
    public string StartDate { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string DateRangeText => string.Equals(StartDate, EndDate, StringComparison.OrdinalIgnoreCase)
        ? StartDate
        : $"Von {StartDate}{FormatTimeSuffix(StartTime)}";
    public string TimeRangeText
    {
        get
        {
            if (!string.Equals(StartDate, EndDate, StringComparison.OrdinalIgnoreCase))
                return $"Bis {EndDate}{FormatTimeSuffix(EndTime)}";
            if (string.IsNullOrWhiteSpace(StartTime) && string.IsNullOrWhiteSpace(EndTime)) return "Ganztägig";
            if (string.IsNullOrWhiteSpace(EndTime)) return $"ab {StartTime}";
            if (string.IsNullOrWhiteSpace(StartTime)) return $"bis {EndTime}";
            return $"{StartTime} – {EndTime}";
        }
    }
    public string NoteText => string.IsNullOrWhiteSpace(Note) ? "Keine Notiz" : Note;

    private static string FormatTimeSuffix(string? time)
        => string.IsNullOrWhiteSpace(time) ? string.Empty : $" um {time.Trim()}";

    public static UnavailabilityPeriodEditItem FromModel(ResourceUnavailabilityPeriod value) => new()
    {
        StartDate = ResourceAvailabilityService.ParseDate(value.StartDate)?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
        EndDate = ResourceAvailabilityService.ParseDate(value.EndDate)?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
        StartTime = value.StartTime ?? string.Empty,
        EndTime = value.EndTime ?? string.Empty,
        Note = value.Note ?? string.Empty
    };
}
