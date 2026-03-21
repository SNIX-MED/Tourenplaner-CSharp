using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class MainShellViewModel : ObservableObject
{
    private NavigationItemViewModel? _selectedNavigationItem;
    private object? _currentSection;

    public MainShellViewModel(
        AppSnapshotService snapshotService,
        string ordersJsonPath,
        string toursJsonPath,
        string employeesJsonPath,
        string vehiclesJsonPath,
        string settingsJsonPath,
        string dataRootPath)
    {
        var start = new StartSectionViewModel(snapshotService);
        var calendar = new KalenderSectionViewModel(toursJsonPath);
        var orders = new OrdersSectionViewModel(ordersJsonPath);
        var nonMapOrders = new NonMapOrdersSectionViewModel(ordersJsonPath);
        var tours = new ToursSectionViewModel(toursJsonPath, employeesJsonPath, vehiclesJsonPath);
        var employees = new EmployeesSectionViewModel(employeesJsonPath);
        var vehicles = new VehiclesSectionViewModel(vehiclesJsonPath);
        var settings = new SettingsSectionViewModel(settingsJsonPath, dataRootPath);
        var gps = new GpsSectionViewModel();
        var updates = new UpdatesSectionViewModel(settingsJsonPath);

        NavigationItems =
        [
            new NavigationItemViewModel("Start", start),
            new NavigationItemViewModel("Kalender", calendar),
            new NavigationItemViewModel("Karte", new KarteSectionViewModel()),
            new NavigationItemViewModel("GPS", gps),
            new NavigationItemViewModel("Orders", orders),
            new NavigationItemViewModel("Non-Map Orders", nonMapOrders),
            new NavigationItemViewModel("Tours", tours),
            new NavigationItemViewModel("Employees", employees),
            new NavigationItemViewModel("Vehicles", vehicles),
            new NavigationItemViewModel("Settings", settings),
            new NavigationItemViewModel("Updates", updates)
        ];

        SelectedNavigationItem = NavigationItems[0];

        _ = start.RefreshAsync();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public NavigationItemViewModel? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetProperty(ref _selectedNavigationItem, value))
            {
                CurrentSection = value?.Section;
            }
        }
    }

    public object? CurrentSection
    {
        get => _currentSection;
        private set => SetProperty(ref _currentSection, value);
    }
}
