using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Tourenplaner.CSharp.App.Views.Controls;

public partial class ModernDatePickerField : UserControl, INotifyPropertyChanged
{
    private static readonly CultureInfo UiCulture = CultureInfo.GetCultureInfo("de-CH");
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public ModernDatePickerField()
    {
        InitializeComponent();
        CalendarDays = new ObservableCollection<ModernDatePickerDayItem>();
        RebuildCalendarDays();
    }

    public static readonly DependencyProperty SelectedDateTextProperty =
        DependencyProperty.Register(
            nameof(SelectedDateText),
            typeof(string),
            typeof(ModernDatePickerField),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedDateTextChanged));

    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(
            nameof(PlaceholderText),
            typeof(string),
            typeof(ModernDatePickerField),
            new PropertyMetadata("Datum auswählen", OnPlaceholderChanged));

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ModernDatePickerDayItem> CalendarDays { get; }

    public string SelectedDateText
    {
        get => (string)GetValue(SelectedDateTextProperty);
        set => SetValue(SelectedDateTextProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public string DisplayText
    {
        get
        {
            var text = (SelectedDateText ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(text) ? PlaceholderText : text;
        }
    }

    public string MonthDisplayText => _visibleMonth.ToString("MMMM yyyy", UiCulture);

    private static void OnSelectedDateTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ModernDatePickerField control)
        {
            return;
        }

        if (TryParseDate((string?)e.NewValue, out var parsed))
        {
            control._visibleMonth = new DateTime(parsed.Year, parsed.Month, 1);
        }

        control.OnPropertyChanged(nameof(DisplayText));
        control.RebuildCalendarDays();
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ModernDatePickerField control)
        {
            return;
        }

        control.OnPropertyChanged(nameof(DisplayText));
    }

    private static bool TryParseDate(string? value, out DateTime parsed)
    {
        return DateTime.TryParseExact(
            (value ?? string.Empty).Trim(),
            "dd.MM.yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed);
    }

    private void RebuildCalendarDays()
    {
        CalendarDays.Clear();
        OnPropertyChanged(nameof(MonthDisplayText));

        var selected = TryParseDate(SelectedDateText, out var selectedDate)
            ? selectedDate.Date
            : (DateTime?)null;

        var firstOfMonth = new DateTime(_visibleMonth.Year, _visibleMonth.Month, 1);
        var offset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var start = firstOfMonth.AddDays(-offset);

        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i).Date;
            CalendarDays.Add(new ModernDatePickerDayItem
            {
                Date = date,
                DayText = date.Day.ToString(CultureInfo.InvariantCulture),
                IsCurrentMonth = date.Month == _visibleMonth.Month && date.Year == _visibleMonth.Year,
                IsToday = date == DateTime.Today,
                IsSelected = selected.HasValue && date == selected.Value
            });
        }
    }

    private void OnToggleButtonClick(object sender, RoutedEventArgs e)
    {
        PickerPopup.IsOpen = !PickerPopup.IsOpen;
    }

    private void OnPopupOpened(object sender, System.EventArgs e)
    {
        if (TryParseDate(SelectedDateText, out var parsed))
        {
            _visibleMonth = new DateTime(parsed.Year, parsed.Month, 1);
        }

        RebuildCalendarDays();
    }

    private void OnPopupClosed(object sender, System.EventArgs e)
    {
        // no-op; explicit hook keeps state predictable if we later expand behavior.
    }

    private void OnPreviousMonthClicked(object sender, RoutedEventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(-1);
        RebuildCalendarDays();
    }

    private void OnNextMonthClicked(object sender, RoutedEventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(1);
        RebuildCalendarDays();
    }

    private void OnDayClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModernDatePickerDayItem day })
        {
            return;
        }

        SelectedDateText = day.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        PickerPopup.IsOpen = false;
        RebuildCalendarDays();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ModernDatePickerDayItem
{
    public DateTime Date { get; set; }

    public string DayText { get; set; } = string.Empty;

    public bool IsCurrentMonth { get; set; }

    public bool IsToday { get; set; }

    public bool IsSelected { get; set; }
}
