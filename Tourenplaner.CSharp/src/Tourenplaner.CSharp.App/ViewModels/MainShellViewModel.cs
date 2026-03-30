using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using System.Threading;
using System.Windows;

namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class MainShellViewModel : ObservableObject
{
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
        ToastNotificationService.NotificationRequested += OnToastNotificationRequested;
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
        private set
        {
            if (!SetProperty(ref _currentSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSplitScreenActive));
            OnPropertyChanged(nameof(IsSidebarVisible));
            OnPropertyChanged(nameof(SidebarColumnWidth));
            OnPropertyChanged(nameof(IsMapSectionActive));
            OnPropertyChanged(nameof(IsToursSectionActive));
            OnPropertyChanged(nameof(IsTopBarSectionControlsVisible));
        }
    }

    public bool IsSplitScreenActive => CurrentSection is SplitScreenSectionViewModel;

    public bool IsSidebarVisible => !IsSplitScreenActive;

    public GridLength SidebarColumnWidth => IsSplitScreenActive ? new GridLength(0) : new GridLength(280);

    public bool IsMapSectionActive => CurrentSection is KarteSectionViewModel;

    public bool IsToursSectionActive => CurrentSection is ToursSectionViewModel;

    public bool IsTopBarSectionControlsVisible => IsMapSectionActive || IsToursSectionActive;

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
