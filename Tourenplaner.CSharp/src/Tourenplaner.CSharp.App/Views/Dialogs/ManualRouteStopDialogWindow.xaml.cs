using System.Windows;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class ManualRouteStopDialogWindow : Window
{
    public sealed record MinuteOption(int Value, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    public ManualRouteStopDialogWindow(
        string? currentName = null,
        int currentStayMinutes = 10,
        string? currentNotes = null, string? avisoStatus = null, string? windowStart = null, string? windowEnd = null)
    {
        InitializeComponent();
        AvisoComboBox.ItemsSource = new[] { "Keine Avisierung nötig", "nicht avisiert", "Informiert", "Bestätigt" };
        AvisoComboBox.SelectedItem = string.IsNullOrWhiteSpace(avisoStatus) ? "Keine Avisierung nötig" : avisoStatus;
        WindowStartTextBox.Text = windowStart ?? string.Empty;
        WindowEndTextBox.Text = windowEnd ?? string.Empty;
        StayMinutesComboBox.ItemsSource = Enumerable.Range(0, 289)
            .Select(index => index * 5)
            .Select(minutes => new MinuteOption(minutes, FormatMinutes(minutes)))
            .ToList();
        StopNameTextBox.Text = (currentName ?? string.Empty).Trim();
        NotesTextBox.Text = currentNotes ?? string.Empty;
        var normalizedMinutes = Math.Max(0, currentStayMinutes);
        var roundedMinutes = (int)Math.Round(normalizedMinutes / 5d) * 5;
        StayMinutesComboBox.SelectedValue = Math.Clamp(roundedMinutes, 0, 1440);
        if (!string.IsNullOrWhiteSpace(currentName))
        {
            Title = "Stopp bearbeiten";
            DialogHeading.Text = "Stoppdaten bearbeiten";
            SaveButton.Content = "Speichern";
        }
        Loaded += (_, _) => StopNameTextBox.Focus();
    }

    public string AvisoStatus => AvisoComboBox.SelectedItem as string ?? "Keine Avisierung nötig";
    public string WindowStart => WindowStartTextBox.Text.Trim();
    public string WindowEnd => WindowEndTextBox.Text.Trim();
    public string StopName { get; private set; } = string.Empty;
    public int StayMinutes { get; private set; }
    public string Notes { get; private set; } = string.Empty;

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var stopName = (StopNameTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(stopName))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Bitte eine Stoppbezeichnung eingeben.",
                "Stopp hinzufügen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StopNameTextBox.Focus();
            return;
        }

        if (AvisoStatus != "Keine Avisierung nötig" &&
            (WindowStart.Length > 0 || WindowEnd.Length > 0 || AvisoStatus == "Bestätigt") &&
            (!TimeOnly.TryParseExact(WindowStart, "HH:mm", out var start) ||
             !TimeOnly.TryParseExact(WindowEnd, "HH:mm", out var end) || start > end))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show("Bitte ein gültiges Zeitfenster im Format HH:mm eingeben (Start vor Ende).", "Zeitfenster prüfen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        StopName = stopName;
        StayMinutes = StayMinutesComboBox.SelectedValue is int minutes ? minutes : 10;
        Notes = (NotesTextBox.Text ?? string.Empty).Trim();
        DialogResult = true;
    }

    private static string FormatMinutes(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} min";
        }

        var hours = minutes / 60;
        var remainder = minutes % 60;
        return remainder == 0 ? $"{hours} h" : $"{hours} h {remainder} min";
    }
}
