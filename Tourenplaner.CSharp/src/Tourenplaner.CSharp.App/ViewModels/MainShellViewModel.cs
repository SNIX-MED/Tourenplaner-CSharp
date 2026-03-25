using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class MainShellViewModel : ObservableObject
{
    private readonly KarteSectionViewModel _mapSection;
    private NavigationItemViewModel? _selectedNavigationItem;
    private object? _currentSection;
    private string _globalSearchText = string.Empty;
    private string _currentUserName = "Mike Weber";
    private readonly NavigationItemViewModel _settingsNavigationItem;

    public MainShellViewModel(
        AppSnapshotService snapshotService,
        string ordersJsonPath,
        string toursJsonPath,
        string employeesJsonPath,
        string vehiclesJsonPath,
        string settingsJsonPath,
        string dataRootPath)
    {
        var dataSyncService = new AppDataSyncService();
        var map = new KarteSectionViewModel(ordersJsonPath, toursJsonPath, settingsJsonPath, dataSyncService);
        _mapSection = map;
        var start = new StartSectionViewModel(
            toursJsonPath,
            settingsJsonPath,
            "pack://application:,,,/Tourenplaner.CSharp.App;component/Assets/Banner.png",
            () => NavigateToMapAsync(map),
            dataSyncService);
        var tours = new ToursSectionViewModel(
            toursJsonPath,
            ordersJsonPath,
            employeesJsonPath,
            vehiclesJsonPath,
            settingsJsonPath,
            tourId => NavigateToMapTourAsync(map, tourId),
            dataSyncService);
        var calendar = new KalenderSectionViewModel(
            toursJsonPath,
            ordersJsonPath,
            settingsJsonPath,
            tourId => NavigateToTourAsync(tours, tourId),
            date => NavigateToTourDateAsync(tours, date),
            dataSyncService);
        var orders = new OrdersSectionViewModel(ordersJsonPath, dataSyncService);
        var nonMapOrders = new NonMapOrdersSectionViewModel(ordersJsonPath, dataSyncService);
        var employees = new EmployeesSectionViewModel(employeesJsonPath, dataSyncService);
        var vehicles = new VehiclesSectionViewModel(vehiclesJsonPath, dataSyncService);
        var settings = new SettingsSectionViewModel(settingsJsonPath, dataRootPath);
        var gps = new GpsSectionViewModel();

        NavigationItems =
        [
            new NavigationItemViewModel("Start", start, "Planung"),
            new NavigationItemViewModel("Kalender", calendar, "Planung"),
            new NavigationItemViewModel("Karte", map, "Planung"),
            new NavigationItemViewModel("Liefertouren", tours, "Planung"),
            new NavigationItemViewModel("Auftragsliste", orders, "Stammdaten"),
            new NavigationItemViewModel("Nicht-Karten", nonMapOrders, "Stammdaten"),
            new NavigationItemViewModel("Mitarbeiter", employees, "Stammdaten"),
            new NavigationItemViewModel("Fahrzeuge", vehicles, "Stammdaten"),
            new NavigationItemViewModel("GPS", gps, "Tools"),
            new NavigationItemViewModel("Einstellungen", settings, "Tools")
        ];

        _settingsNavigationItem = NavigationItems.First(item => item.DisplayName == "Einstellungen");
        SidebarNavigationItems = NavigationItems.Where(item => !ReferenceEquals(item, _settingsNavigationItem)).ToList();

        OpenSettingsCommand = new DelegateCommand(OpenSettings);
        ExportCurrentRouteCommand = new DelegateCommand(ExportCurrentRoute, CanExportCurrentRoute);
        _mapSection.ExportRouteCommand.CanExecuteChanged += (_, _) => ExportCurrentRouteCommand.RaiseCanExecuteChanged();
        SelectedNavigationItem = NavigationItems[0];

        _ = start.RefreshAsync();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public IReadOnlyList<NavigationItemViewModel> SidebarNavigationItems { get; }

    public DelegateCommand OpenSettingsCommand { get; }

    public DelegateCommand ExportCurrentRouteCommand { get; }

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
                ExportCurrentRouteCommand.RaiseCanExecuteChanged();
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

    private async Task NavigateToMapAsync(KarteSectionViewModel mapSection)
    {
        SelectedNavigationItem = NavigationItems.FirstOrDefault(x => ReferenceEquals(x.Section, mapSection)) ?? SelectedNavigationItem;
        await mapSection.RefreshAsync();
    }

    private void OpenSettings()
    {
        SelectedNavigationItem = _settingsNavigationItem;
    }

    private bool CanExportCurrentRoute()
    {
        return ReferenceEquals(CurrentSection, _mapSection) && _mapSection.ExportRouteCommand.CanExecute(null);
    }

    private void ExportCurrentRoute()
    {
        if (!CanExportCurrentRoute())
        {
            return;
        }

        _mapSection.ExportRouteCommand.Execute(null);
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
