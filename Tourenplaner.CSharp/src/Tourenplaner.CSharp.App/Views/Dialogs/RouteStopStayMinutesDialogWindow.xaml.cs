using System.ComponentModel;
using System.Windows;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class RouteStopStayMinutesDialogWindow : Window
{
    public RouteStopStayMinutesDialogWindow(int currentMinutes)
        : this(currentMinutes, Array.Empty<string>(), string.Empty, string.Empty)
    {
    }

    public RouteStopStayMinutesDialogWindow(
        int currentMinutes,
        IReadOnlyList<string> avisoStatusOptions,
        string currentAvisoStatus,
        string currentEmployeeInfoText)
    {
        InitializeComponent();
        ViewModel = new RouteStopStayMinutesDialogViewModel(
            currentMinutes,
            avisoStatusOptions,
            currentAvisoStatus,
            currentEmployeeInfoText);
        DataContext = ViewModel;
    }

    public RouteStopStayMinutesDialogViewModel ViewModel { get; }

    public int? StayMinutes { get; private set; }
    public string? SelectedAvisoStatus { get; private set; }
    public string EmployeeInfoText { get; private set; } = string.Empty;

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        StayMinutes = ViewModel.SelectedMinutes;
        SelectedAvisoStatus = ViewModel.SelectedAvisoStatus;
        EmployeeInfoText = (ViewModel.EmployeeInfoText ?? string.Empty).Trim();
        DialogResult = true;
        Close();
    }
}

public sealed class RouteStopStayMinutesDialogViewModel : INotifyPropertyChanged
{
    private int _selectedMinutes;
    private string _selectedAvisoStatus = string.Empty;
    private string _employeeInfoText = string.Empty;

    public RouteStopStayMinutesDialogViewModel(
        int currentMinutes,
        IReadOnlyList<string> avisoStatusOptions,
        string currentAvisoStatus,
        string currentEmployeeInfoText)
    {
        MinuteOptions = Enumerable.Range(0, 289).Select(i => i * 5).ToList();
        AvisoStatusOptions = (avisoStatusOptions ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalized = Math.Max(0, currentMinutes);
        var roundedToFive = (int)Math.Round(normalized / 5.0) * 5;
        SelectedMinutes = MinuteOptions.Contains(roundedToFive) ? roundedToFive : 0;
        SelectedAvisoStatus = AvisoStatusOptions.FirstOrDefault(x =>
                                 string.Equals(x, (currentAvisoStatus ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) ??
                             AvisoStatusOptions.FirstOrDefault() ??
                             string.Empty;
        EmployeeInfoText = (currentEmployeeInfoText ?? string.Empty).Trim();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<int> MinuteOptions { get; }
    public IReadOnlyList<string> AvisoStatusOptions { get; }

    public int SelectedMinutes
    {
        get => _selectedMinutes;
        set
        {
            if (_selectedMinutes == value)
            {
                return;
            }

            _selectedMinutes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMinutes)));
        }
    }

    public string SelectedAvisoStatus
    {
        get => _selectedAvisoStatus;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.Equals(_selectedAvisoStatus, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _selectedAvisoStatus = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedAvisoStatus)));
        }
    }

    public string EmployeeInfoText
    {
        get => _employeeInfoText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_employeeInfoText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _employeeInfoText = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmployeeInfoText)));
        }
    }
}
