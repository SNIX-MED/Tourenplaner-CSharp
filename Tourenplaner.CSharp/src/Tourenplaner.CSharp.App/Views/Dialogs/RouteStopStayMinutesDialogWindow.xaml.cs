using System.ComponentModel;
using System.Windows;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class RouteStopStayMinutesDialogWindow : Window
{
    public RouteStopStayMinutesDialogWindow(int currentMinutes)
    {
        InitializeComponent();
        ViewModel = new RouteStopStayMinutesDialogViewModel(currentMinutes);
        DataContext = ViewModel;
    }

    public RouteStopStayMinutesDialogViewModel ViewModel { get; }

    public int? StayMinutes { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        StayMinutes = ViewModel.SelectedMinutes;
        DialogResult = true;
        Close();
    }
}

public sealed class RouteStopStayMinutesDialogViewModel : INotifyPropertyChanged
{
    private int _selectedMinutes;

    public RouteStopStayMinutesDialogViewModel(int currentMinutes)
    {
        MinuteOptions = Enumerable.Range(0, 289).Select(i => i * 5).ToList();
        var normalized = Math.Max(0, currentMinutes);
        var roundedToFive = (int)Math.Round(normalized / 5.0) * 5;
        SelectedMinutes = MinuteOptions.Contains(roundedToFive) ? roundedToFive : 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<int> MinuteOptions { get; }

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
}
