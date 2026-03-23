using Tourenplaner.CSharp.App.Themes;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class MainShellViewModel : ObservableObject
{
    private NavigationItemViewModel? _selectedNavigationItem;
    private object? _currentSection;
    private bool _isDarkTheme = AppThemeManager.IsDarkTheme;
    private string _globalSearchText = string.Empty;
    private string _currentUserName = "Mike Weber";

    public MainShellViewModel(
        AppSnapshotService snapshotService,
        string ordersJsonPath,
        string toursJsonPath,
        string employeesJsonPath,
        string vehiclesJsonPath,
        string settingsJsonPath,
        string dataRootPath)
    {
        var start = new StartSectionViewModel(snapshotService, vehiclesJsonPath);
        var map = new KarteSectionViewModel(ordersJsonPath, toursJsonPath, settingsJsonPath);
        var tours = new ToursSectionViewModel(
            toursJsonPath,
            employeesJsonPath,
            vehiclesJsonPath,
            settingsJsonPath,
            tourId => NavigateToMapTourAsync(map, tourId));
        var calendar = new KalenderSectionViewModel(
            toursJsonPath,
            tourId => NavigateToTourAsync(tours, tourId),
            date => NavigateToTourDateAsync(tours, date));
        var orders = new OrdersSectionViewModel(ordersJsonPath);
        var nonMapOrders = new NonMapOrdersSectionViewModel(ordersJsonPath);
        var employees = new EmployeesSectionViewModel(employeesJsonPath);
        var vehicles = new VehiclesSectionViewModel(vehiclesJsonPath);
        var settings = new SettingsSectionViewModel(settingsJsonPath, dataRootPath);
        var gps = new GpsSectionViewModel();
        var updates = new UpdatesSectionViewModel(settingsJsonPath);

        NavigationItems =
        [
            new NavigationItemViewModel("Start", start),
            new NavigationItemViewModel("Kalender", calendar),
            new NavigationItemViewModel("Karte", map),
            new NavigationItemViewModel("GPS", gps),
            new NavigationItemViewModel("Auftragsliste", orders),
            new NavigationItemViewModel("Nicht-Karten", nonMapOrders),
            new NavigationItemViewModel("Liefertouren", tours),
            new NavigationItemViewModel("Mitarbeiter", employees),
            new NavigationItemViewModel("Fahrzeuge", vehicles),
            new NavigationItemViewModel("Einstellungen", settings),
            new NavigationItemViewModel("Updates", updates)
        ];

        ToggleThemeCommand = new DelegateCommand(ToggleTheme);
        SelectedNavigationItem = NavigationItems[0];

        _ = start.RefreshAsync();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public DelegateCommand ToggleThemeCommand { get; }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set
        {
            if (SetProperty(ref _isDarkTheme, value))
            {
                OnPropertyChanged(nameof(ThemeToggleText));
            }
        }
    }

    public string ThemeToggleText => IsDarkTheme ? "Light Mode" : "Dark Mode";

    public string GlobalSearchText
    {
        get => _globalSearchText;
        set => SetProperty(ref _globalSearchText, value);
    }

    public string CurrentUserName
    {
        get => _currentUserName;
        private set => SetProperty(ref _currentUserName, value);
    }

    public NavigationItemViewModel? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetProperty(ref _selectedNavigationItem, value))
            {
                CurrentSection = value?.Section;
                TriggerSectionRefresh(value?.Section);
            }
        }
    }

    public object? CurrentSection
    {
        get => _currentSection;
        private set => SetProperty(ref _currentSection, value);
    }

    private async Task NavigateToTourAsync(ToursSectionViewModel toursSection, int tourId)
    {
        await toursSection.FocusTourAsync(tourId);
        SelectedNavigationItem = NavigationItems.FirstOrDefault(x => ReferenceEquals(x.Section, toursSection)) ?? SelectedNavigationItem;
    }

    private async Task NavigateToTourDateAsync(ToursSectionViewModel toursSection, DateTime date)
    {
        SelectedNavigationItem = NavigationItems.FirstOrDefault(x => ReferenceEquals(x.Section, toursSection)) ?? SelectedNavigationItem;
        await toursSection.FocusDateAsync(date);
    }

    private async Task NavigateToMapTourAsync(KarteSectionViewModel mapSection, int tourId)
    {
        SelectedNavigationItem = NavigationItems.FirstOrDefault(x => ReferenceEquals(x.Section, mapSection)) ?? SelectedNavigationItem;
        await mapSection.FocusTourAsync(tourId);
    }

    private void ToggleTheme()
    {
        AppThemeManager.ToggleTheme();
        IsDarkTheme = AppThemeManager.IsDarkTheme;
    }

    private static void TriggerSectionRefresh(object? section)
    {
        switch (section)
        {
            case StartSectionViewModel start:
                _ = start.RefreshAsync();
                break;
            case KarteSectionViewModel map:
                _ = map.RefreshAsync();
                break;
            case ToursSectionViewModel tours:
                _ = tours.RefreshAsync();
                break;
            case KalenderSectionViewModel calendar:
                _ = calendar.RefreshAsync();
                break;
            case VehiclesSectionViewModel vehicles:
                _ = vehicles.RefreshAsync();
                break;
            case OrdersSectionViewModel orders:
                _ = orders.RefreshAsync();
                break;
            case NonMapOrdersSectionViewModel nonMap:
                _ = nonMap.RefreshAsync();
                break;
        }
    }
}
