using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class SplitScreenSectionViewModel : SectionViewModelBase
{
    private readonly Func<Task>? _leaveViewAsync;

    public SplitScreenSectionViewModel(
        KarteSectionViewModel mapSection,
        KalenderSectionViewModel calendarSection,
        Func<Task>? leaveViewAsync = null)
        : base("Split-Ansicht", "Karte und Tourenübersicht in einer kombinierten Vollansicht.")
    {
        MapSection = mapSection;
        CalendarSection = calendarSection;
        _leaveViewAsync = leaveViewAsync;

        LeaveViewCommand = new AsyncCommand(LeaveViewAsync, () => _leaveViewAsync is not null);
        OpenSelectedTourOnMapCommand = new AsyncCommand(OpenSelectedTourOnMapAsync, CanOpenSelectedTourOnMap);
        PreviousDayCommand = new DelegateCommand(GoToPreviousDay, CanGoToPreviousDay);
        NextDayCommand = new DelegateCommand(GoToNextDay, CanGoToNextDay);
        CalendarSection.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(KalenderSectionViewModel.SelectedDayTour) ||
                args.PropertyName == nameof(KalenderSectionViewModel.SelectedDay))
            {
                if (OpenSelectedTourOnMapCommand is AsyncCommand openSelected)
                {
                    openSelected.RaiseCanExecuteChanged();
                }

                if (PreviousDayCommand is DelegateCommand previousDay)
                {
                    previousDay.RaiseCanExecuteChanged();
                }

                if (NextDayCommand is DelegateCommand nextDay)
                {
                    nextDay.RaiseCanExecuteChanged();
                }

                OnPropertyChanged(nameof(SelectedDayCompactText));
            }
        };
    }

    public KarteSectionViewModel MapSection { get; }

    public KalenderSectionViewModel CalendarSection { get; }

    public ICommand LeaveViewCommand { get; }

    public ICommand OpenSelectedTourOnMapCommand { get; }

    public ICommand PreviousDayCommand { get; }

    public ICommand NextDayCommand { get; }

    public string SelectedDayCompactText
    {
        get
        {
            if (CalendarSection.SelectedDay?.Date is not DateTime date)
            {
                return "Kein Tag ausgewählt";
            }

            return $"{ToShortWeekday(date.DayOfWeek)}, {date:dd.MM.yyyy}";
        }
    }

    private async Task LeaveViewAsync()
    {
        if (_leaveViewAsync is null)
        {
            return;
        }

        await _leaveViewAsync();
    }

    private bool CanOpenSelectedTourOnMap()
    {
        return CalendarSection.SelectedDayTour is not null;
    }

    private async Task OpenSelectedTourOnMapAsync()
    {
        var selected = CalendarSection.SelectedDayTour;
        if (selected is null)
        {
            return;
        }

        await MapSection.FocusTourAsync(selected.TourId);
    }

    private bool CanGoToPreviousDay()
    {
        return CalendarSection.SelectedDay is not null;
    }

    private bool CanGoToNextDay()
    {
        return CalendarSection.SelectedDay is not null;
    }

    private void GoToPreviousDay()
    {
        if (CalendarSection.NavigateSelectedDay(-1))
        {
            OnPropertyChanged(nameof(SelectedDayCompactText));
        }
    }

    private void GoToNextDay()
    {
        if (CalendarSection.NavigateSelectedDay(1))
        {
            OnPropertyChanged(nameof(SelectedDayCompactText));
        }
    }

    private static string ToShortWeekday(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "Mo",
            DayOfWeek.Tuesday => "Di",
            DayOfWeek.Wednesday => "Mi",
            DayOfWeek.Thursday => "Do",
            DayOfWeek.Friday => "Fr",
            DayOfWeek.Saturday => "Sa",
            DayOfWeek.Sunday => "So",
            _ => string.Empty
        };
    }
}
