using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
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
    private readonly AppDataHistoryService _historyService;
    private readonly AppDataSyncService _dataSyncService;
    private readonly string _ordersJsonPath;
    private readonly JsonAppSettingsRepository _appSettingsRepository;
    private readonly JsonEmployeesRepository _employeesRepository;
    private readonly Guid _instanceId = Guid.NewGuid();
    private NavigationItemViewModel? _selectedNavigationItem;
    private object? _currentSection;
    private string _globalSearchText = string.Empty;
    private string _currentUserName = Environment.UserName;
    private string _selectedUserName = Environment.UserName;
    private bool _suppressUserSelectionChange;
    private readonly NavigationItemViewModel _settingsNavigationItem;
    private int _toastVersion;
    private bool _isToastVisible;
    private string _toastMessage = string.Empty;
    private readonly GpsSectionViewModel _gpsSection;
    private readonly SpediteurSectionViewModel _spediteurSection;
    private readonly SettingsSectionViewModel _settingsSection;
    private readonly NavigationItemViewModel _gpsNavigationItem;
    private readonly NavigationItemViewModel _spediteurNavigationItem;
    private readonly HashSet<object> _activatedSections = new(ReferenceEqualityComparer.Instance);
    private bool _isSidebarCollapsed;

    public MainShellViewModel(
        AppDataHistoryService historyService,
        AppDataSyncService dataSyncService,
        string ordersJsonPath,
        string toursJsonPath,
        string employeesJsonPath,
        string vehiclesJsonPath,
        string settingsJsonPath,
        string dataRootPath)
    {
        _historyService = historyService;
        _dataSyncService = dataSyncService;
        _ordersJsonPath = ordersJsonPath;
        _appSettingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
        _employeesRepository = new JsonEmployeesRepository(employeesJsonPath);
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
            tourId => NavigateToTourAndEditAsync(tours, tourId),
            tourId => NavigateToMapTourAsync(map, tourId),
            date => NavigateToTourDateAsync(tours, date),
            orderId => OpenOrderEditorFromCalendarAsync(orderId),
            dataSyncService: dataSyncService);
        var orders = new OrdersSectionViewModel(
            ordersJsonPath,
            dataSyncService,
            tourId => NavigateToTourAsync(tours, tourId));
        var nonMapOrders = new NonMapOrdersSectionViewModel(
            ordersJsonPath,
            dataSyncService,
            tourId => NavigateToTourAsync(tours, tourId));
        var employees = new EmployeesSectionViewModel(employeesJsonPath, toursJsonPath, dataSyncService);
        var vehicles = new VehiclesSectionViewModel(vehiclesJsonPath, toursJsonPath, dataSyncService);
        
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
        AvailableUserNames = [];
        ApplyToolSettingsFromSettingsSection();
        RebuildSidebarNavigation();
        _settingsSection.PropertyChanged += OnSettingsSectionPropertyChanged;

        OpenSettingsCommand = new DelegateCommand(OpenSettings);
        ToggleSidebarCommand = new DelegateCommand(ToggleSidebar);
        UndoCommand = new AsyncCommand(UndoAsync, () => CanUndo);
        RedoCommand = new AsyncCommand(RedoAsync, () => _historyService.CanRedo);
        _historyService.StateChanged += OnHistoryStateChanged;
        _mapSection.PropertyChanged += OnMapSectionPropertyChanged;
        _dataSyncService.DataChanged += OnDataChanged;
        ToastNotificationService.NotificationRequested += OnToastNotificationRequested;
        SelectedNavigationItem = NavigationItems[0];

        _ = start.RefreshAsync();
        _ = InitializeUserSelectionAsync();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public ObservableCollection<NavigationItemViewModel> SidebarNavigationItems { get; }
    public ObservableCollection<string> AvailableUserNames { get; }

    public DelegateCommand OpenSettingsCommand { get; }

    public DelegateCommand ToggleSidebarCommand { get; }

    public AsyncCommand UndoCommand { get; }

    public AsyncCommand RedoCommand { get; }

    public bool CanUndo => CanUndoDraftRouteStopRemoval || _historyService.CanUndo;

    public bool CanRedo => _historyService.CanRedo;

    public string UndoToolTip => CanUndoDraftRouteStopRemoval
        ? "Rückgängig: Stopp entfernen"
        : (CanUndo
            ? $"Rückgängig: {_historyService.UndoDescription}"
            : "Keine rückgängig zu machende Änderung");

    public string RedoToolTip => CanRedo
        ? $"Wiederholen: {_historyService.RedoDescription}"
        : "Keine wiederholbare Änderung";

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

    public string SelectedUserName
    {
        get => _selectedUserName;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (!SetProperty(ref _selectedUserName, normalized))
            {
                return;
            }

            if (_suppressUserSelectionChange || string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            _ = SwitchCurrentUserAsync(normalized);
        }
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

    public bool IsSidebarCollapsed => _isSidebarCollapsed;

    public bool IsSidebarVisible => !IsSidebarCollapsed;

    public GridLength SidebarColumnWidth => IsSidebarVisible ? new GridLength(280) : new GridLength(0);

    public bool IsSidebarToggleVisible => true;

    public string SidebarToggleGlyph => IsSidebarCollapsed ? "\uE76C" : "\uE76B";

    public string SidebarToggleToolTip => IsSidebarCollapsed ? "Seitenmenü einblenden" : "Seitenmenü ausblenden";

    public bool IsMapSectionActive => CurrentSection is KarteSectionViewModel;

    public bool IsToursSectionActive => CurrentSection is ToursSectionViewModel;

    public bool IsEmployeesSectionActive => CurrentSection is EmployeesSectionViewModel;

    public bool IsVehiclesSectionActive => CurrentSection is VehiclesSectionViewModel;

    public bool IsGpsSectionActive => CurrentSection is GpsSectionViewModel;

    public bool IsSpediteurSectionActive => CurrentSection is SpediteurSectionViewModel;

    public bool IsTopBarSectionControlsVisible =>
        IsMapSectionActive ||
        IsToursSectionActive ||
        CurrentSection is KalenderSectionViewModel ||
        CurrentSection is OrdersSectionViewModel ||
        CurrentSection is NonMapOrdersSectionViewModel ||
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

    private async Task NavigateToTourAndEditAsync(ToursSectionViewModel toursSection, int tourId)
    {
        await toursSection.FocusTourAsync(tourId);
        SelectNavigationItemForSection(toursSection);
        if (toursSection.EditTourOnMapCommand.CanExecute(null))
        {
            toursSection.EditTourOnMapCommand.Execute(null);
        }
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

    private void OpenSettings()
    {
        SelectedNavigationItem = _settingsNavigationItem;
    }

    private void ToggleSidebar()
    {
        if (SetProperty(ref _isSidebarCollapsed, !_isSidebarCollapsed))
        {
            OnPropertyChanged(nameof(IsSidebarCollapsed));
            OnPropertyChanged(nameof(IsSidebarVisible));
            OnPropertyChanged(nameof(SidebarColumnWidth));
            OnPropertyChanged(nameof(IsSidebarToggleVisible));
            OnPropertyChanged(nameof(SidebarToggleGlyph));
            OnPropertyChanged(nameof(SidebarToggleToolTip));
        }
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
        OnPropertyChanged(nameof(IsSidebarCollapsed));
        OnPropertyChanged(nameof(IsSidebarVisible));
        OnPropertyChanged(nameof(SidebarColumnWidth));
        OnPropertyChanged(nameof(IsSidebarToggleVisible));
        OnPropertyChanged(nameof(SidebarToggleGlyph));
        OnPropertyChanged(nameof(SidebarToggleToolTip));
        OnPropertyChanged(nameof(IsMapSectionActive));
        OnPropertyChanged(nameof(IsToursSectionActive));
        OnPropertyChanged(nameof(IsEmployeesSectionActive));
        OnPropertyChanged(nameof(IsVehiclesSectionActive));
        OnPropertyChanged(nameof(IsGpsSectionActive));
        OnPropertyChanged(nameof(IsSpediteurSectionActive));
        OnPropertyChanged(nameof(IsTopBarSectionControlsVisible));
        RefreshUndoRedoState();
    }

    private bool IsDraftRouteUndoContext()
    {
        return CurrentSection is KarteSectionViewModel;
    }

    private bool CanUndoDraftRouteStopRemoval => IsDraftRouteUndoContext() && _mapSection.CanUndoDraftRouteStopRemoval;

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

    private async Task UndoAsync()
    {
        if (CanUndoDraftRouteStopRemoval && _mapSection.TryUndoDraftRouteStopRemoval())
        {
            RefreshUndoRedoState();
            return;
        }

        await _historyService.UndoAsync();
    }

    private async Task RedoAsync()
    {
        await _historyService.RedoAsync();
    }

    private void OnHistoryStateChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnHistoryStateChanged(sender, e));
            return;
        }

        RefreshUndoRedoState();
    }

    private void OnMapSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(KarteSectionViewModel.CanUndoDraftRouteStopRemoval), StringComparison.Ordinal))
        {
            return;
        }

        RefreshUndoRedoState();
    }

    private void RefreshUndoRedoState()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
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

    private async Task InitializeUserSelectionAsync()
    {
        try
        {
            var settingsTask = _appSettingsRepository.LoadAsync();
            var employeesTask = _employeesRepository.LoadAsync();
            await Task.WhenAll(settingsTask, employeesTask);

            var settings = await settingsTask;
            var names = (await employeesTask)
                .Where(x => x is not null && x.HasProgramProfile && !string.IsNullOrWhiteSpace(x.DisplayName))
                .Select(x => x.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var preferred = (settings.CurrentUserName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = (Environment.UserName ?? string.Empty).Trim();
            }

            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = "default";
            }

            preferred = ResolvePreferredUserName(preferred, names);

            _suppressUserSelectionChange = true;
            AvailableUserNames.Clear();
            foreach (var name in names)
            {
                AvailableUserNames.Add(name);
            }

            SelectedUserName = preferred;
            _suppressUserSelectionChange = false;
            CurrentUserName = preferred;

            if (!string.Equals(settings.CurrentUserName, preferred, StringComparison.OrdinalIgnoreCase))
            {
                settings.CurrentUserName = preferred;
                await _appSettingsRepository.SaveAsync(settings);
            }
        }
        catch
        {
            _suppressUserSelectionChange = true;
            AvailableUserNames.Clear();
            AvailableUserNames.Add(CurrentUserName);
            SelectedUserName = CurrentUserName;
            _suppressUserSelectionChange = false;
        }
    }

    private async Task SwitchCurrentUserAsync(string userName)
    {
        var normalized = (userName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(CurrentUserName, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var settings = await _appSettingsRepository.LoadAsync();
        settings.CurrentUserName = normalized;
        await _appSettingsRepository.SaveAsync(settings);

        CurrentUserName = normalized;
        if (!AvailableUserNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            if (AvailableUserNames.Count == 0)
            {
                AvailableUserNames.Add(normalized);
            }
            else
            {
                return;
            }
        }

        await _mapSection.RefreshAsync();
        await _settingsSection.RefreshAsync();
        ToastNotificationService.ShowInfo($"Aktiver Benutzer: {normalized}");
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (!args.Kinds.HasFlag(AppDataKind.Employees))
        {
            return;
        }

        _ = ReloadAvailableUsersAsync();
    }

    private async Task ReloadAvailableUsersAsync()
    {
        try
        {
            var employees = await _employeesRepository.LoadAsync();
            var names = employees
                .Where(x => x is not null && x.HasProgramProfile && !string.IsNullOrWhiteSpace(x.DisplayName))
                .Select(x => x.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var resolvedCurrent = ResolvePreferredUserName(CurrentUserName, names);
            if (!string.Equals(resolvedCurrent, CurrentUserName, StringComparison.OrdinalIgnoreCase))
            {
                CurrentUserName = resolvedCurrent;
                _suppressUserSelectionChange = true;
                SelectedUserName = resolvedCurrent;
                _suppressUserSelectionChange = false;
            }

            _suppressUserSelectionChange = true;
            AvailableUserNames.Clear();
            foreach (var name in names)
            {
                AvailableUserNames.Add(name);
            }
            _suppressUserSelectionChange = false;
        }
        catch
        {
        }
    }

    private static string ResolvePreferredUserName(string preferred, IReadOnlyList<string> employeeNames)
    {
        if (employeeNames.Count == 0)
        {
            return string.IsNullOrWhiteSpace(preferred) ? "default" : preferred;
        }

        var normalizedPreferred = (preferred ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return employeeNames[0];
        }

        var exact = employeeNames.FirstOrDefault(x => string.Equals(x, normalizedPreferred, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var firstNameMatches = employeeNames
            .Where(x =>
            {
                var firstToken = x.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                return string.Equals(firstToken, normalizedPreferred, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (firstNameMatches.Count == 1)
        {
            return firstNameMatches[0];
        }

        return employeeNames[0];
    }
}
