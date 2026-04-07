using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using System.Threading;
using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class MainShellViewModel : ObservableObject
{
    private static readonly HashSet<string> ToolSettingsPropertyNames =
    [
        nameof(SettingsSectionViewModel.ShowGpsTool),
        nameof(SettingsSectionViewModel.ShowSpediteurTool),
        nameof(SettingsSectionViewModel.GpsToolUrl),
        nameof(SettingsSectionViewModel.SpediteurToolUrl)
    ];

    private readonly KarteSectionViewModel _mapSection;
    private readonly KalenderSectionViewModel _calendarSection;
    private readonly SplitScreenSectionViewModel _splitScreenSection;
    private readonly AppDataSyncService _dataSyncService;
    private readonly string _ordersJsonPath;
    private readonly Guid _instanceId = Guid.NewGuid();
    private NavigationItemViewModel? _selectedNavigationItem;
    private object? _currentSection;
    private string _globalSearchText = string.Empty;
    private string _currentUserName = "Mike Weber";
    private readonly NavigationItemViewModel _settingsNavigationItem;
    private object? _previousSectionBeforeSplit;
    private int _toastVersion;
    private bool _isToastVisible;
    private string _toastMessage = string.Empty;
    private readonly GpsSectionViewModel _gpsSection;
    private readonly SpediteurSectionViewModel _spediteurSection;
    private readonly SettingsSectionViewModel _settingsSection;
    private readonly NavigationItemViewModel _gpsNavigationItem;
    private readonly NavigationItemViewModel _spediteurNavigationItem;
    private readonly HashSet<object> _activatedSections = new(ReferenceEqualityComparer.Instance);

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
        _dataSyncService = dataSyncService;
        _ordersJsonPath = ordersJsonPath;
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
            tourId => NavigateToMapTourAsync(map, tourId),
            date => NavigateToTourDateAsync(tours, date),
            orderId => OpenOrderEditorFromCalendarAsync(orderId),
            dataSyncService: dataSyncService);
        _calendarSection = calendar;
        _splitScreenSection = new SplitScreenSectionViewModel(map, calendar, LeaveSplitScreenAsync);
        map.SetOpenSplitScreenAction(OpenSplitScreenAsync);
        calendar.SetOpenSplitScreenAction(OpenSplitScreenAsync);
        var orders = new OrdersSectionViewModel(ordersJsonPath, dataSyncService);
        var nonMapOrders = new NonMapOrdersSectionViewModel(ordersJsonPath, dataSyncService);
        var employees = new EmployeesSectionViewModel(employeesJsonPath, dataSyncService);
        var vehicles = new VehiclesSectionViewModel(vehiclesJsonPath, dataSyncService);
        
        // Repositories für SQL Import
        var orderRepository = new JsonOrderRepository(ordersJsonPath);
        var settingsRepository = new JsonSettingsRepository(settingsJsonPath);
        var settings = new SettingsSectionViewModel(
            settingsJsonPath,
            dataRootPath,
            orderRepository,
            settingsRepository,
            dataSyncService);
        _settingsSection = settings;
        var gps = new GpsSectionViewModel();
        _gpsSection = gps;
        var spediteur = new SpediteurSectionViewModel();
        _spediteurSection = spediteur;

        NavigationItems =
        [
            new NavigationItemViewModel("Start", start, "Planung"),
            new NavigationItemViewModel("Kalender", calendar, "Planung"),
            new NavigationItemViewModel("Karte", map, "Planung"),
            new NavigationItemViewModel("Liefertouren", tours, "Planung"),
            new NavigationItemViewModel("Auftragsliste", orders, "Stammdaten"),
            new NavigationItemViewModel("Post/Spedition/Abholung", nonMapOrders, "Stammdaten"),
            new NavigationItemViewModel("Mitarbeiter", employees, "Stammdaten"),
            new NavigationItemViewModel("Fahrzeuge", vehicles, "Stammdaten"),
            new NavigationItemViewModel("GPS", gps, "Tools"),
            new NavigationItemViewModel("Spediteur", spediteur, "Tools"),
            new NavigationItemViewModel("Einstellungen", settings, "Tools")
        ];

        _settingsNavigationItem = NavigationItems.First(item => item.DisplayName == "Einstellungen");
        _gpsNavigationItem = NavigationItems.First(item => item.DisplayName == "GPS");
        _spediteurNavigationItem = NavigationItems.First(item => item.DisplayName == "Spediteur");
        SidebarNavigationItems = [];
        ApplyToolSettingsFromSettingsSection();
        RebuildSidebarNavigation();
        _settingsSection.PropertyChanged += OnSettingsSectionPropertyChanged;

        OpenSettingsCommand = new DelegateCommand(OpenSettings);
        ToastNotificationService.NotificationRequested += OnToastNotificationRequested;
        SelectedNavigationItem = NavigationItems[0];

        _ = start.RefreshAsync();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public ObservableCollection<NavigationItemViewModel> SidebarNavigationItems { get; }

    public DelegateCommand OpenSettingsCommand { get; }

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
            if (value is null)
            {
                value = ReferenceEquals(_selectedNavigationItem, _settingsNavigationItem)
                    ? _settingsNavigationItem
                    : _selectedNavigationItem ?? SidebarNavigationItems.FirstOrDefault() ?? _settingsNavigationItem;
            }

            if (SetProperty(ref _selectedNavigationItem, value))
            {
                CurrentSection = value?.Section;
                TriggerSectionRefreshOnce(value?.Section);
            }
        }
    }

    public object? CurrentSection
    {
        get => _currentSection;
        private set
        {
            if (!SetProperty(ref _currentSection, value))
            {
                return;
            }

            OnCurrentSectionChanged();
        }
    }

    public bool IsSplitScreenActive => CurrentSection is SplitScreenSectionViewModel;

    public bool IsSidebarVisible => !IsSplitScreenActive;

    public GridLength SidebarColumnWidth => IsSplitScreenActive ? new GridLength(0) : new GridLength(280);

    public bool IsMapSectionActive => CurrentSection is KarteSectionViewModel;

    public bool IsToursSectionActive => CurrentSection is ToursSectionViewModel;

    public bool IsEmployeesSectionActive => CurrentSection is EmployeesSectionViewModel;

    public bool IsVehiclesSectionActive => CurrentSection is VehiclesSectionViewModel;

    public bool IsGpsSectionActive => CurrentSection is GpsSectionViewModel;

    public bool IsSpediteurSectionActive => CurrentSection is SpediteurSectionViewModel;

    public bool IsTopBarSectionControlsVisible =>
        IsMapSectionActive ||
        IsToursSectionActive ||
        IsEmployeesSectionActive ||
        IsVehiclesSectionActive ||
        IsGpsSectionActive ||
        IsSpediteurSectionActive;

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    private async Task NavigateToTourAsync(ToursSectionViewModel toursSection, int tourId)
    {
        await toursSection.FocusTourAsync(tourId);
        SelectNavigationItemForSection(toursSection);
    }

    private async Task NavigateToTourDateAsync(ToursSectionViewModel toursSection, DateTime date)
    {
        SelectNavigationItemForSection(toursSection);
        await toursSection.FocusDateAsync(date);
    }

    private async Task NavigateToMapTourAsync(KarteSectionViewModel mapSection, int tourId)
    {
        SelectNavigationItemForSection(mapSection);
        await mapSection.FocusTourAsync(tourId);
    }

    private async Task NavigateToMapAsync(KarteSectionViewModel mapSection)
    {
        SelectNavigationItemForSection(mapSection);
        await mapSection.RefreshAsync();
    }

    private async Task OpenSplitScreenAsync()
    {
        var openedFromCalendar = ReferenceEquals(CurrentSection, _calendarSection);
        var calendarTourId = _calendarSection.SelectedDayTour?.TourId;

        _previousSectionBeforeSplit = CurrentSection;
        CurrentSection = _splitScreenSection;
        await Task.WhenAll(_mapSection.RefreshAsync(), _calendarSection.RefreshAsync());

        if (openedFromCalendar && calendarTourId is int tourId && tourId > 0)
        {
            await _mapSection.FocusTourAsync(tourId);
        }
    }

    private async Task LeaveSplitScreenAsync()
    {
        CurrentSection = _previousSectionBeforeSplit ?? _mapSection;
        _previousSectionBeforeSplit = null;
        await Task.CompletedTask;
    }

    private void OpenSettings()
    {
        SelectedNavigationItem = _settingsNavigationItem;
    }

    private void TriggerSectionRefreshOnce(object? section)
    {
        if (section is null || !_activatedSections.Add(section))
        {
            return;
        }

        _ = TryRefreshSectionAsync(section);
    }

    private void OnSettingsSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ToolSettingsPropertyNames.Contains(e.PropertyName ?? string.Empty))
        {
            return;
        }

        ApplyToolSettingsFromSettingsSection();
        RebuildSidebarNavigation();
    }

    private void ApplyToolSettingsFromSettingsSection()
    {
        _gpsSection.SetConfiguredUrl(_settingsSection.GpsToolUrl);
        _spediteurSection.SetConfiguredUrl(_settingsSection.SpediteurToolUrl);
    }

    private void RebuildSidebarNavigation()
    {
        SidebarNavigationItems.Clear();
        foreach (var item in NavigationItems)
        {
            if (!IsNavigationItemVisibleInSidebar(item))
            {
                continue;
            }

            SidebarNavigationItems.Add(item);
        }

        EnsureSelectedNavigationItemIsVisible();
    }

    private void OnCurrentSectionChanged()
    {
        OnPropertyChanged(nameof(IsSplitScreenActive));
        OnPropertyChanged(nameof(IsSidebarVisible));
        OnPropertyChanged(nameof(SidebarColumnWidth));
        OnPropertyChanged(nameof(IsMapSectionActive));
        OnPropertyChanged(nameof(IsToursSectionActive));
        OnPropertyChanged(nameof(IsEmployeesSectionActive));
        OnPropertyChanged(nameof(IsVehiclesSectionActive));
        OnPropertyChanged(nameof(IsGpsSectionActive));
        OnPropertyChanged(nameof(IsSpediteurSectionActive));
        OnPropertyChanged(nameof(IsTopBarSectionControlsVisible));
    }

    private void SelectNavigationItemForSection(object section)
    {
        SelectedNavigationItem = NavigationItems.FirstOrDefault(x => ReferenceEquals(x.Section, section)) ?? SelectedNavigationItem;
    }

    private static Task TryRefreshSectionAsync(object section)
    {
        return section switch
        {
            StartSectionViewModel start => start.RefreshAsync(),
            KarteSectionViewModel map => map.RefreshAsync(),
            ToursSectionViewModel tours => tours.RefreshAsync(),
            KalenderSectionViewModel calendar => calendar.RefreshAsync(),
            VehiclesSectionViewModel vehicles => vehicles.RefreshAsync(),
            OrdersSectionViewModel orders => orders.RefreshAsync(),
            NonMapOrdersSectionViewModel nonMap => nonMap.RefreshAsync(),
            SettingsSectionViewModel settings => settings.RefreshAsync(),
            _ => Task.CompletedTask
        };
    }

    private bool IsNavigationItemVisibleInSidebar(NavigationItemViewModel item)
    {
        if (ReferenceEquals(item, _settingsNavigationItem))
        {
            return false;
        }

        if (ReferenceEquals(item, _gpsNavigationItem) && !_settingsSection.ShowGpsTool)
        {
            return false;
        }

        if (ReferenceEquals(item, _spediteurNavigationItem) && !_settingsSection.ShowSpediteurTool)
        {
            return false;
        }

        return true;
    }

    private void EnsureSelectedNavigationItemIsVisible()
    {
        if (SelectedNavigationItem is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedNavigationItem, _gpsNavigationItem) && !_settingsSection.ShowGpsTool)
        {
            SelectedNavigationItem = SidebarNavigationItems.FirstOrDefault() ?? _settingsNavigationItem;
            return;
        }

        if (ReferenceEquals(SelectedNavigationItem, _spediteurNavigationItem) && !_settingsSection.ShowSpediteurTool)
        {
            SelectedNavigationItem = SidebarNavigationItems.FirstOrDefault() ?? _settingsNavigationItem;
        }
    }

    private async Task OpenOrderEditorFromCalendarAsync(string orderId)
    {
        var normalizedId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        var repository = new JsonOrderRepository(_ordersJsonPath);
        var orders = (await repository.GetAllAsync()).ToList();
        var existing = orders.FirstOrDefault(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        var dialog = new ManualOrderDialogWindow(
            existing,
            deliveryTypes: existing.Type == OrderType.NonMap
                ? DeliveryMethodExtensions.NonMapDeliveryTypeOptions
                : DeliveryMethodExtensions.MapDeliveryTypeOptions,
            defaultOrderType: existing.Type)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var updated = dialog.CreatedOrder;
        updated.Type = existing.Type;
        updated.AssignedTourId = existing.AssignedTourId;
        updated.Location = await AddressGeocodingService.TryGeocodeOrderAsync(updated) ?? existing.Location;

        var originalId = existing.Id;
        orders.RemoveAll(x => string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase));
        orders.RemoveAll(x => !string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        orders.Add(updated);

        await repository.SaveAllAsync(orders);
        _dataSyncService.PublishOrders(_instanceId, originalId, updated.Id);
    }

    private async void OnToastNotificationRequested(object? sender, ToastNotification notification)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnToastNotificationRequested(sender, notification));
            return;
        }

        ToastMessage = notification.Message;
        IsToastVisible = true;
        var version = Interlocked.Increment(ref _toastVersion);
        await Task.Delay(notification.DurationMs);
        if (version == _toastVersion)
        {
            IsToastVisible = false;
        }
    }
}
