using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class OrdersSectionViewModel : SectionViewModelBase
{
    private const string AllDeliveryTypesLabel = "Alle Lieferarten";
    private const string AllStatusesLabel = "Alle Status";
    private const string DefaultOrderStatus = Order.DefaultOrderStatus;
    private const string UnspecifiedSupplierFilterOption = "nicht festgelegt";
    private static readonly IReadOnlyList<string> KnownStatusOptions =
    [
        DefaultOrderStatus,
        Order.OrderedStatus,
        Order.InTransitStatus,
        Order.PartiallyInTransitStatus,
        Order.PendingPreparationStatus,
        Order.PartiallyPendingPreparationStatus,
        Order.PartiallyReadyStatus,
        Order.ReadyToDeliverStatus
    ];

    private readonly OrderSectionWorkflowService _workflowService;
    private readonly IAppSettingsStore _settingsRepository;
    private readonly string _geocodeCachePath;
    private readonly List<Order> _allOrders = new();
    private readonly OrderDeliveryService _deliveryService = new();
    private Order? _lastDeletedOrder;
    private int _lastDeletedIndex = -1;

    private string _searchText = string.Empty;
    private string _statusText = "Lade Aufträge...";
    private OrderItem? _selectedOrder;
    private string _selectedDeliveryTypeFilter = AllDeliveryTypesLabel;
    private string _selectedStatusFilter = AllStatusesLabel;
    private bool _includeOpenOrders = true;
    private bool _includePlannedOrders = true;
    private bool _isUpdatingFilterOptions;
    private bool _suppressFilterRefresh;
    private bool _isCustomerColumnVisible = true;
    private bool _isDeliveryAddressColumnVisible = true;
    private bool _isNotesColumnVisible = true;
    private bool _isOrdersFilterPanelVisible;
    private bool _showArchivedOrders;

    public OrdersSectionViewModel(
        OrderSectionWorkflowService workflowService,
        IAppSettingsStore settingsRepository,
        string dataRoot)
        : base("Aufträge", "Aufträge mit Adresse, Zuordnung und Filterung.")
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _settingsRepository = settingsRepository;
        _geocodeCachePath = Path.Combine(dataRoot, "geocode-cache.json");

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => MapOrders.Count > 0);
        AddCommand = new DelegateCommand(AddOrder);
        AddManualOrderCommand = new AsyncCommand(AddManualOrderAsync);
        EditSelectedOrderCommand = new AsyncCommand(EditSelectedOrderAsync, () => SelectedOrder is not null);
        CreatePartialDeliveryCommand = new AsyncCommand(CreatePartialDeliveryAsync, CanCreatePartialDelivery);
        MergePartialDeliveriesCommand = new AsyncCommand(MergePartialDeliveriesAsync, CanMergePartialDeliveries);
        ShowAssignedTourCommand = new AsyncCommand(ShowAssignedTourAsync, CanShowAssignedTour);
        CopySelectedOrderNumberCommand = new DelegateCommand(CopySelectedOrderNumber, () => SelectedOrder is not null);
        SetSelectedOrderStatusDefaultCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.DefaultOrderStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusOrderedCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.OrderedStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusInTransitCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.InTransitStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusPartiallyInTransitCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.PartiallyInTransitStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusPartiallyReadyCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.PartiallyReadyStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusReadyCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.ReadyToDeliverStatus), () => SelectedOrder is not null);
        ToggleArchiveSelectedOrderCommand = new AsyncCommand(ToggleArchiveSelectedOrderAsync, () => SelectedOrders.Count > 0);
        ArchiveDelivery1Command = new AsyncCommand(() => ArchiveSelectedDeliveryAsync(1), () => CanArchiveSelectedDelivery(1));
        ArchiveDelivery2Command = new AsyncCommand(() => ArchiveSelectedDeliveryAsync(2), () => CanArchiveSelectedDelivery(2));
        UndoDeleteCommand = new AsyncCommand(UndoDeleteAsync, () => _lastDeletedOrder is not null);
        RemoveCommand = new AsyncCommand(RemoveSelectedOrderAsync, () => SelectedOrders.Count > 0);
        ShowActiveOrdersCommand = new DelegateCommand(() => ShowArchivedOrders = false);
        ShowArchivedOrdersCommand = new DelegateCommand(() => ShowArchivedOrders = true);
        ToggleArchiveModeCommand = new DelegateCommand(() => ShowArchivedOrders = !ShowArchivedOrders);
        ResetOrderFiltersCommand = new DelegateCommand(ResetOrderFilters);
        ToggleAllOrderFiltersCommand = new DelegateCommand(ToggleAllOrderFilters);
        _workflowService.OrdersChanged += OnOrdersChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<OrderItem> MapOrders { get; } = new();
    public ObservableCollection<OrderItem> SelectedOrders { get; } = new();
    public ObservableCollection<string> DeliveryTypeFilterOptions { get; } = [];
    public ObservableCollection<string> StatusFilterOptions { get; } = [];
    public ObservableCollection<MapOrderFilterOption> OrderStatusFilters { get; } = new();
    public ObservableCollection<MapOrderFilterOption> DeliveryTypeFilters { get; } = new();
    public ObservableCollection<MapOrderFilterOption> AvisoStatusFilters { get; } = new();
    public ObservableCollection<MapOrderFilterOption> SupplierFilters { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand AddManualOrderCommand { get; }

    public ICommand EditSelectedOrderCommand { get; }

    public ICommand CreatePartialDeliveryCommand { get; }

    public ICommand MergePartialDeliveriesCommand { get; }

    public ICommand ShowAssignedTourCommand { get; }

    public ICommand CopySelectedOrderNumberCommand { get; }

    public ICommand SetSelectedOrderStatusDefaultCommand { get; }

    public ICommand SetSelectedOrderStatusOrderedCommand { get; }

    public ICommand SetSelectedOrderStatusInTransitCommand { get; }

    public ICommand SetSelectedOrderStatusPartiallyInTransitCommand { get; }

    public ICommand SetSelectedOrderStatusPartiallyReadyCommand { get; }

    public ICommand SetSelectedOrderStatusReadyCommand { get; }

    public ICommand ToggleArchiveSelectedOrderCommand { get; }

    public ICommand ArchiveDelivery1Command { get; }

    public ICommand ArchiveDelivery2Command { get; }

    public ICommand UndoDeleteCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand ShowActiveOrdersCommand { get; }

    public ICommand ShowArchivedOrdersCommand { get; }

    public ICommand ToggleArchiveModeCommand { get; }

    public ICommand ResetOrderFiltersCommand { get; }

    public ICommand ToggleAllOrderFiltersCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildGrid();
            }
        }
    }

    public string SelectedDeliveryTypeFilter
    {
        get => _selectedDeliveryTypeFilter;
        set
        {
            if (SetProperty(ref _selectedDeliveryTypeFilter, value))
            {
                RebuildGrid();
            }
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                RebuildGrid();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsCustomerColumnVisible
    {
        get => _isCustomerColumnVisible;
        set => SetProperty(ref _isCustomerColumnVisible, value);
    }

    public bool IsDeliveryAddressColumnVisible
    {
        get => _isDeliveryAddressColumnVisible;
        set => SetProperty(ref _isDeliveryAddressColumnVisible, value);
    }

    public bool IsNotesColumnVisible
    {
        get => _isNotesColumnVisible;
        set => SetProperty(ref _isNotesColumnVisible, value);
    }

    public bool IsOrdersFilterPanelVisible
    {
        get => _isOrdersFilterPanelVisible;
        set => SetProperty(ref _isOrdersFilterPanelVisible, value);
    }

    public bool ShowArchivedOrders
    {
        get => _showArchivedOrders;
        set
        {
            if (!SetProperty(ref _showArchivedOrders, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowActiveOrders));
            OnPropertyChanged(nameof(ArchiveToggleButtonText));
            UpdateFilterOptions();
            RebuildGrid();
        }
    }

    public bool ShowActiveOrders => !ShowArchivedOrders;

    public string ArchiveToggleButtonText => ShowArchivedOrders ? "Aktiv anzeigen" : "Archiviert anzeigen";

    public string ToggleArchiveSelectedOrderMenuText => SelectedOrder?.IsArchived == true
        ? "Auftrag reaktivieren"
        : "Auftrag archivieren";

    public bool SelectedOrderHasPartialDeliveries
    {
        get
        {
            if (SelectedOrder is null)
            {
                return false;
            }

            var order = _allOrders.FirstOrDefault(x =>
                string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
            return (order?.Deliveries ?? []).Count >= 2;
        }
    }

    public bool SelectedOrderCanCreatePartialDelivery => CanCreatePartialDelivery();

    public bool SelectedOrderCanMergePartialDeliveries => CanMergePartialDeliveries();

    public OrderItem? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
            {
                OnPropertyChanged(nameof(ToggleArchiveSelectedOrderMenuText));
                OnPropertyChanged(nameof(SelectedOrderHasPartialDeliveries));
                OnPropertyChanged(nameof(SelectedOrderCanCreatePartialDelivery));
                OnPropertyChanged(nameof(SelectedOrderCanMergePartialDeliveries));
                RaiseCommandStates();
            }
        }
    }

    public void UpdateSelectedOrders(IEnumerable<OrderItem> orders)
    {
        var snapshot = orders.ToList();
        OrderSectionWorkflowService.SynchronizeSelection(SelectedOrders, snapshot);

        if (SelectedOrders.Count > 0 && (SelectedOrder is null || !SelectedOrders.Contains(SelectedOrder)))
        {
            SelectedOrder = SelectedOrders.Last();
        }

        RaiseCommandStates();
    }

    public async Task FocusOrderAsync(string orderId, bool isArchived)
    {
        await RefreshAsync();
        ShowArchivedOrders = isArchived;
        ResetOrderFilters();
        SearchText = orderId;
        RebuildGrid(orderId);
    }

    public async Task RefreshAsync()
    {
        await RefreshFromRepositoryAsync();
    }

    public async Task SaveAsync()
    {
        OrderSectionSharedHelpers.SyncDerivedOrderStatuses(_allOrders);
        await _workflowService.SaveAllAsync(_allOrders);
        PublishOrderChange(SelectedOrder?.Id, SelectedOrder?.Id);
        StatusText = $"Aufträge gespeichert: {_allOrders.Count(DeliveryMethodExtensions.CanUseLiefertour)}";
    }

    private bool CanCreatePartialDelivery()
    {
        if (SelectedOrder is null)
        {
            return false;
        }
        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        return order is not null &&
               !order.IsArchived &&
               (order.Deliveries ?? []).Count < OrderDeliveryService.MaximumDeliveryCount &&
               (_deliveryService.CanSplitSingleDelivery(order) ||
                _deliveryService.GetRemainingQuantities(order).Values.Any(x => x > 0));
    }

    private async Task CreatePartialDeliveryAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }
        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            return;
        }

        OrderDelivery? previousSingleDelivery = null;
        if (_deliveryService.CanSplitSingleDelivery(order))
        {
            previousSingleDelivery = order.Deliveries[0];
            order.Deliveries = [];
        }
        var dialog = new OrderDeliveryDialogWindow(order) { Owner = System.Windows.Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.ResultLines is null)
        {
            if (previousSingleDelivery is not null)
            {
                order.Deliveries = [previousSingleDelivery];
            }
            return;
        }
        var delivery = _deliveryService.Create(order, dialog.ResultLines, dialog.Delivery1PlannedDate, dialog.Delivery1Notes);
        if (previousSingleDelivery is not null)
        {
            delivery.Id = previousSingleDelivery.Id;
            delivery.AssignedTourId = previousSingleDelivery.AssignedTourId;
            delivery.Status = string.IsNullOrWhiteSpace(delivery.AssignedTourId)
                ? OrderDeliveryStatus.ReadyForPlanning
                : OrderDeliveryStatus.Planned;
            order.AssignedTourId = delivery.AssignedTourId;
        }
        var remainderDelivery = _deliveryService.EnsureRemainderDelivery(order);
        if (remainderDelivery is not null)
        {
            remainderDelivery.PlannedDeliveryDate = dialog.Delivery2PlannedDate;
            remainderDelivery.IsPlannedDeliveryDateExplicit = dialog.Delivery2PlannedDate.HasValue;
            remainderDelivery.Notes = dialog.Delivery2Notes;
        }
        _deliveryService.DistributeOrderNotesToDeliveries(order);
        if (!await SaveOrderAsync(order))
        {
            return;
        }
        await RefreshFromRepositoryAsync(order.Id);
        PublishOrderChange(order.Id, order.Id);
        StatusText = $"Teillieferung {order.Id}-{delivery.SequenceNumber} wurde erstellt.";
    }

    private bool CanMergePartialDeliveries()
    {
        if (SelectedOrder is null)
        {
            return false;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        return order is not null && _deliveryService.CanMergePartialDeliveries(order);
    }

    private async Task MergePartialDeliveriesAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        if (order is null || !CanMergePartialDeliveries())
        {
            return;
        }

        var confirmation = AppMessageBox.Show(
            System.Windows.Application.Current?.MainWindow,
            "Soll die Teillieferung wirklich aufgehoben werden? Anschliessend wird der Auftrag wieder als eine Lieferung geplant.",
            "Teillieferung aufheben",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _deliveryService.MergePartialDeliveries(order);
        if (!await SaveOrderAsync(order))
        {
            return;
        }

        await ReconcileToursWithOrdersAsync(_allOrders);
        await RefreshFromRepositoryAsync(order.Id);
        PublishOrderChange(order.Id, order.Id);
        StatusText = $"Teillieferung für Auftrag {order.Id} wurde aufgehoben.";
        ToastNotificationService.ShowInfo(StatusText);
    }

    private void AddOrder()
    {
        var item = new OrderItem
        {
            Id = Guid.NewGuid().ToString(),
            CustomerName = "New map order",
            Address = string.Empty,
            ScheduledDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
            Latitude = "47.3769",
            Longitude = "8.5417"
        };

        MapOrders.Insert(0, item);
        SelectedOrder = item;
        UpdateStatusText();
        RaiseCommandStates();
    }

    private async Task AddManualOrderAsync()
    {
        var dialog = new ManualOrderDialogWindow(
            deliveryTypes: DeliveryMethodExtensions.AllDeliveryTypeOptions,
            defaultOrderType: OrderType.Map)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var createdOrder = dialog.CreatedOrder;
        var geocodingResult = await ApplyDeliveryMethodRoutingAsync(createdOrder);

        _allOrders.RemoveAll(x => string.Equals(x.Id, createdOrder.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(createdOrder);

        if (!await SaveOrderAsync(createdOrder))
        {
            return;
        }
        await RefreshFromRepositoryAsync(createdOrder.Id);
        SelectOrderById(createdOrder.Id);
        PublishOrderChange(null, createdOrder.Id);
        OrderPinAssignmentWarningService.ShowIfNeeded(createdOrder, geocodingResult);
        StatusText = DeliveryMethodExtensions.CanUseLiefertour(createdOrder) && createdOrder.Location is null
            ? $"Auftrag {createdOrder.Id} gespeichert, aber Adresse konnte nicht automatisch geokodiert werden."
            : $"Auftrag {createdOrder.Id} wurde gespeichert.";
        ToastNotificationService.ShowInfo($"Auftrag {createdOrder.Id} wurde erstellt.");
    }

    private async Task EditSelectedOrderAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var existing = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        var originalId = existing.Id;
        AddressGeocodingResult? updatedGeocodingResult = null;
        var dialog = new ManualOrderDialogWindow(
            existing,
            deliveryTypes: DeliveryMethodExtensions.AllDeliveryTypeOptions,
            defaultOrderType: OrderType.Map)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            BeforeSaveAsync = async (candidate, owner) =>
            {
                if (OrderPinSaveValidationService.HasDeliveryAddressChanged(existing, candidate) &&
                    DeliveryMethodExtensions.CanUseLiefertour(candidate))
                {
                    var settings = await _settingsRepository.LoadAsync();
                    var validation = await OrderPinSaveValidationService.ValidateAsync(
                        existing, candidate, settings.TomTomApiKey, _geocodeCachePath, owner);
                    updatedGeocodingResult = validation.GeocodingResult;
                    return validation.Confirmed;
                }

                updatedGeocodingResult = await ApplyDeliveryMethodRoutingAsync(candidate, existing.Location);
                return true;
            }
        };

        var dialogResult = dialog.ShowDialog();
        if (dialog.DeleteRequested)
        {
            await RemoveSelectedOrderAsync();
            return;
        }

        if (dialogResult != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var updated = dialog.CreatedOrder;
        updated.ConcurrencyToken = existing.ConcurrencyToken;
        if (!_deliveryService.TryValidateUpdate(existing, updated, out var deliveryValidationError))
        {
            AppMessageBox.Show(System.Windows.Application.Current?.MainWindow, deliveryValidationError, "Teillieferungen prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!await ConfirmManualArchiveForAssignedActiveTourAsync(existing, updated.IsArchived))
        {
            return;
        }

        _allOrders.RemoveAll(x => string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase));
        _allOrders.RemoveAll(x => !string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(updated);

        if (!string.Equals(originalId, updated.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (!await DeleteOrderAsync(originalId, existing.ConcurrencyToken))
            {
                return;
            }

            updated.ConcurrencyToken = null;
        }

        if (!await SaveOrderAsync(updated))
        {
            return;
        }
        await ReconcileToursWithOrdersAsync(_allOrders);
        await RefreshFromRepositoryAsync(updated.Id);
        SelectOrderById(updated.Id);
        PublishOrderChange(originalId, updated.Id);
        OrderPinAssignmentWarningService.ShowIfNeeded(updated, updatedGeocodingResult);
        StatusText = $"Auftrag {updated.Id} wurde aktualisiert.";
    }

    private async Task<AddressGeocodingResult?> ApplyDeliveryMethodRoutingAsync(Order order, GeoPoint? fallbackLocation = null)
    {
        return await _workflowService.ApplyDeliveryMethodRoutingAsync(order, fallbackLocation);
    }

    private async Task<bool> ConfirmManualArchiveForAssignedActiveTourAsync(Order existing, bool nextIsArchived)
    {
        var assignment = await _workflowService.ResolveArchiveAssignmentAsync(existing, nextIsArchived);
        if (!assignment.RequiresConfirmation)
        {
            return true;
        }
        var confirmation = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            $"Der Auftrag ist in {assignment.TourLabel} eingeplant, und diese Tour ist nicht archiviert.{Environment.NewLine}{Environment.NewLine}" +
            "Auftrag trotzdem archivieren?",
            "Auftrag in aktiver Tour",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return confirmation == MessageBoxResult.Yes;
    }

    private async Task ToggleArchiveSelectedOrderAsync()
    {
        var selectedOrderIds = SelectedOrders.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selectedOrderIds.Count == 0 && SelectedOrder is not null)
        {
            selectedOrderIds.Add(SelectedOrder.Id);
        }

        var targets = _allOrders.Where(x => selectedOrderIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var nextIsArchived = !targets[0].IsArchived;
        foreach (var target in targets)
        {
            if (!await ConfirmManualArchiveForAssignedActiveTourAsync(target, nextIsArchived))
            {
                return;
            }
        }

        var changed = 0;
        foreach (var target in targets)
        {
            target.IsArchived = nextIsArchived;
            if (await SaveOrderAsync(target))
            {
                changed++;
                PublishOrderChange(target.Id, target.Id);
            }
        }

        await ReconcileToursWithOrdersAsync(_allOrders);
        await RefreshFromRepositoryAsync();
        StatusText = $"{changed} Auftrag/Aufträge wurden {(nextIsArchived ? "archiviert" : "reaktiviert")}.";
        ToastNotificationService.ShowInfo(StatusText);
    }

    private bool CanArchiveSelectedDelivery(int sequenceNumber)
    {
        if (SelectedOrder is null)
        {
            return false;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        var delivery = (order?.Deliveries ?? []).FirstOrDefault(x => x.SequenceNumber == sequenceNumber);
        return order is not null && !order.IsArchived && delivery is not null &&
               delivery.Status is not OrderDeliveryStatus.Delivered and not OrderDeliveryStatus.Cancelled;
    }

    private async Task ArchiveSelectedDeliveryAsync(int sequenceNumber)
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var order = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        var delivery = (order?.Deliveries ?? []).FirstOrDefault(x => x.SequenceNumber == sequenceNumber);
        if (order is null || delivery is null || !CanArchiveSelectedDelivery(sequenceNumber))
        {
            return;
        }

        var confirmation = AppMessageBox.Show(
            System.Windows.Application.Current?.MainWindow,
            $"Lieferung {sequenceNumber} als geliefert archivieren?",
            "Teillieferung archivieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _deliveryService.MarkDelivered(order, delivery.Id, DateTimeOffset.Now);
        if (!await SaveOrderAsync(order))
        {
            return;
        }

        await ReconcileToursWithOrdersAsync(_allOrders);
        PublishOrderChange(order.Id, order.Id);
        var archivedWholeOrder = order.IsArchived;
        await RefreshFromRepositoryAsync();
        StatusText = archivedWholeOrder
            ? $"Lieferung {sequenceNumber} und Auftrag {order.Id} wurden archiviert."
            : $"Lieferung {sequenceNumber} wurde archiviert.";
        ToastNotificationService.ShowInfo(StatusText);
    }

    private async Task ShowAssignedTourAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var navigation = await _workflowService.NavigateToAssignedTourAsync(SelectedOrder.AssignedTourId);
        if (navigation.Status == OrderTourNavigationStatus.InvalidTourId)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Für diesen Auftrag ist keine gültige Tour zugeordnet.",
                "Tour anzeigen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (navigation.Status == OrderTourNavigationStatus.NavigationUnavailable)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"Der Auftrag ist Tour {navigation.TourId} zugeordnet.",
                "Tour anzeigen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

    }

    private bool CanShowAssignedTour()
    {
        return SelectedOrder is not null &&
               OrderSectionWorkflowService.CanNavigateToAssignedTour(SelectedOrder.AssignedTourId);
    }

    private void CopySelectedOrderNumber()
    {
        if (SelectedOrder is null || string.IsNullOrWhiteSpace(SelectedOrder.Id))
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedOrder.Id);
            StatusText = $"Auftragsnummer {SelectedOrder.Id} wurde in die Zwischenablage kopiert.";
        }
        catch
        {
            StatusText = "Auftragsnummer konnte nicht in die Zwischenablage kopiert werden.";
        }
    }

    private async Task SetSelectedOrderStatusAsync(string status)
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var target = _allOrders.FirstOrDefault(x => string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        var normalizedStatus = NormalizeOrderStatus(status);
        if (string.Equals(NormalizeOrderStatus(target.OrderStatus), normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        target.OrderStatus = normalizedStatus;
        if (!await SaveOrderAsync(target))
        {
            return;
        }
        RebuildGrid(target.Id);
        SelectOrderById(target.Id);
        PublishOrderChange(target.Id, target.Id);
        StatusText = $"Status für Auftrag {target.Id} wurde auf \"{normalizedStatus}\" gesetzt.";
    }

    private async Task RemoveSelectedOrderAsync()
    {
        var selectedOrderIds = SelectedOrders
            .Select(x => x.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedOrderIds.Count == 0 && SelectedOrder is not null)
        {
            selectedOrderIds.Add(SelectedOrder.Id);
        }

        var targets = _allOrders
            .Where(x => DeliveryMethodExtensions.CanUseLiefertour(x) && selectedOrderIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var confirmation = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            targets.Count == 1
                ? $"Soll der Auftrag {targets[0].Id} wirklich gelöscht werden?"
                : $"Sollen die {targets.Count} markierten Aufträge wirklich gelöscht werden?",
            "Auftrag löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var deleted = 0;
        var deletedIds = new List<string>();
        var deletedSnapshots = new List<(Order Order, int Index)>();
        if (!_workflowService.HasOrderMutationRepository)
        {
            foreach (var target in targets)
            {
                var index = _allOrders.IndexOf(target);
                if (index < 0)
                {
                    continue;
                }

                deletedSnapshots.Add((CloneOrder(target), index));
                deletedIds.Add(target.Id);
            }

            foreach (var deletedId in deletedIds)
            {
                _allOrders.RemoveAll(x => string.Equals(x.Id, deletedId, StringComparison.OrdinalIgnoreCase));
            }

            await _workflowService.SaveAllAsync(_allOrders);
            deleted = deletedIds.Count;
        }
        else
        {
            foreach (var target in targets)
            {
                var index = _allOrders.IndexOf(target);
                if (index < 0 || !await DeleteOrderAsync(target.Id, target.ConcurrencyToken))
                {
                    continue;
                }

                deletedSnapshots.Add((CloneOrder(target), index));
                deletedIds.Add(target.Id);
                deleted++;
            }

            foreach (var deletedId in deletedIds)
            {
                _allOrders.RemoveAll(x => string.Equals(x.Id, deletedId, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (deleted == 0)
        {
            await RefreshFromRepositoryAsync();
            StatusText = "Kein Auftrag wurde gelöscht.";
            RaiseCommandStates();
            return;
        }

        if (deletedSnapshots.Count > 0)
        {
            var lastDeleted = deletedSnapshots[^1];
            _lastDeletedOrder = lastDeleted.Order;
            _lastDeletedIndex = lastDeleted.Index;
        }

        await ReconcileToursWithOrdersAsync(_allOrders);
        await RefreshFromRepositoryAsync();
        PublishOrderChange(deleted == 1 ? deletedIds[0] : null, null);
        StatusText = deleted == 1
            ? "1 Auftrag wurde gelöscht. Mit 'Zurück' wiederherstellen."
            : $"{deleted} Aufträge wurden gelöscht. Mit 'Zurück' den zuletzt gelöschten Auftrag wiederherstellen.";
        ToastNotificationService.ShowInfo(StatusText);
        RaiseCommandStates();
    }

    private void RebuildGrid(string? preferredSelectedId = null)
    {
        SelectedOrders.Clear();
        var selectedOrderId = string.IsNullOrWhiteSpace(preferredSelectedId)
            ? SelectedOrder?.Id
            : preferredSelectedId;
        var query = (_searchText ?? string.Empty).Trim();
        var map = _allOrders
            .Where(DeliveryMethodExtensions.CanUseLiefertour)
            .Where(o => o.IsArchived == ShowArchivedOrders)
            .Where(MatchesTourAssignmentFilter)
            .Where(MatchesSelectedFilters)
            .Where(o => OrderSectionSharedHelpers.MatchesSearchQuery(o, query));

        MapOrders.Clear();
        foreach (var order in map.OrderBy(o => o.ScheduledDate).ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            MapOrders.Add(ToOrderItem(order));
        }

        SelectedOrder = OrderSectionWorkflowService.SelectOrderById(MapOrders, selectedOrderId);
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void UpdateStatusText()
    {
        StatusText = OrderSectionWorkflowService.BuildStatusText("Aufträge", MapOrders, ShowArchivedOrders);
    }

    private void RaiseCommandStates()
    {
        RaiseCanExecuteChangedIfSupported(SaveCommand);
        RaiseCanExecuteChangedIfSupported(RemoveCommand);
        RaiseCanExecuteChangedIfSupported(EditSelectedOrderCommand);
        RaiseCanExecuteChangedIfSupported(CreatePartialDeliveryCommand);
        RaiseCanExecuteChangedIfSupported(MergePartialDeliveriesCommand);
        RaiseCanExecuteChangedIfSupported(ShowAssignedTourCommand);
        RaiseCanExecuteChangedIfSupported(CopySelectedOrderNumberCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusDefaultCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusOrderedCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusInTransitCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusPartiallyInTransitCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusPartiallyReadyCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusReadyCommand);
        RaiseCanExecuteChangedIfSupported(ToggleArchiveSelectedOrderCommand);
        RaiseCanExecuteChangedIfSupported(ArchiveDelivery1Command);
        RaiseCanExecuteChangedIfSupported(ArchiveDelivery2Command);
        RaiseCanExecuteChangedIfSupported(UndoDeleteCommand);
    }

    private async Task UndoDeleteAsync()
    {
        if (_lastDeletedOrder is null)
        {
            return;
        }

        var restoreOrder = _lastDeletedOrder;
        var insertIndex = _lastDeletedIndex;

        _allOrders.RemoveAll(x => string.Equals(x.Id, restoreOrder.Id, StringComparison.OrdinalIgnoreCase));
        if (insertIndex >= 0 && insertIndex <= _allOrders.Count)
        {
            _allOrders.Insert(insertIndex, restoreOrder);
        }
        else
        {
            _allOrders.Add(restoreOrder);
        }

        _lastDeletedOrder = null;
        _lastDeletedIndex = -1;
        restoreOrder.ConcurrencyToken = null;
        if (!await SaveOrderAsync(restoreOrder))
        {
            return;
        }
        await RefreshFromRepositoryAsync(restoreOrder.Id);
        SelectOrderById(restoreOrder.Id);
        PublishOrderChange(null, restoreOrder.Id);
        StatusText = $"Auftrag {restoreOrder.Id} wurde wiederhergestellt.";
        RaiseCommandStates();
    }

    private async Task RefreshFromRepositoryAsync(string? preferredSelectedId = null)
    {
        var loadResult = await _workflowService.LoadAndNormalizeAsync();
        _allOrders.Clear();
        _allOrders.AddRange(loadResult.Orders);

        UpdateFilterOptions();
        RebuildGrid(preferredSelectedId);
    }

    private async Task<bool> SaveOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        var result = await _workflowService.SaveOrderAsync(order, _allOrders, cancellationToken);
        if (result.Succeeded)
        {
            return true;
        }

        await HandleConcurrencyConflictAsync(order.Id);
        return false;
    }

    public bool IncludeOpenOrders
    {
        get => _includeOpenOrders;
        set
        {
            if (SetProperty(ref _includeOpenOrders, value))
            {
                TriggerOrderFilterRefresh();
            }
        }
    }

    public bool IncludePlannedOrders
    {
        get => _includePlannedOrders;
        set
        {
            if (SetProperty(ref _includePlannedOrders, value))
            {
                TriggerOrderFilterRefresh();
            }
        }
    }

    public string ToggleAllFiltersButtonText => AreAllFiltersSelected() ? "Alle abwählen" : "Alle auswählen";

    private async Task<bool> DeleteOrderAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
    {
        var result = await _workflowService.DeleteOrderAsync(
            orderId,
            concurrencyToken,
            _allOrders,
            cancellationToken);
        if (result.Succeeded)
        {
            return true;
        }

        await HandleConcurrencyConflictAsync(orderId);
        return false;
    }

    private async Task HandleConcurrencyConflictAsync(string? preferredSelectedId)
    {
        await RefreshFromRepositoryAsync(preferredSelectedId);
        Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            "Der Auftrag wurde zwischenzeitlich von einem anderen Benutzer geändert oder gelöscht. Die Liste wurde neu geladen.",
            "Mehrbenutzerkonflikt",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        StatusText = "Auftragsdaten wurden nach einem Mehrbenutzerkonflikt neu geladen.";
    }

    private void UpdateFilterOptions()
    {
        var selectedDelivery = _selectedDeliveryTypeFilter;
        var selectedStatus = _selectedStatusFilter;

        DeliveryTypeFilterOptions.Clear();
        DeliveryTypeFilterOptions.Add(AllDeliveryTypesLabel);
        foreach (var item in DeliveryMethodExtensions.MapDeliveryTypeOptions)
        {
            DeliveryTypeFilterOptions.Add(item);
        }

        var statusOptions = _allOrders
            .Where(DeliveryMethodExtensions.CanUseLiefertour)
            .Where(o => o.IsArchived == ShowArchivedOrders)
            .Select(o => NormalizeOrderStatus(o.OrderStatus))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        StatusFilterOptions.Clear();
        StatusFilterOptions.Add(AllStatusesLabel);
        foreach (var known in KnownStatusOptions)
        {
            StatusFilterOptions.Add(known);
            statusOptions.RemoveAll(x => string.Equals(x, known, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var status in statusOptions)
        {
            StatusFilterOptions.Add(status);
        }

        _selectedDeliveryTypeFilter = DeliveryTypeFilterOptions.Any(x => string.Equals(x, selectedDelivery, StringComparison.OrdinalIgnoreCase))
            ? selectedDelivery
            : AllDeliveryTypesLabel;
        OnPropertyChanged(nameof(SelectedDeliveryTypeFilter));

        _selectedStatusFilter = StatusFilterOptions.Any(x => string.Equals(x, selectedStatus, StringComparison.OrdinalIgnoreCase))
            ? selectedStatus
            : AllStatusesLabel;
        OnPropertyChanged(nameof(SelectedStatusFilter));

        if (_isUpdatingFilterOptions)
        {
            return;
        }

        _isUpdatingFilterOptions = true;
        try
        {
            var orders = _allOrders
                .Where(DeliveryMethodExtensions.CanUseLiefertour)
                .Where(o => o.IsArchived == ShowArchivedOrders)
                .ToList();

            var statuses = orders
                .Select(o => NormalizeOrderStatus(o.OrderStatus))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetOrderStatusSortIndex)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var deliveryTypes = orders
                .Select(o => DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(o.DeliveryType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var avisoStatuses = orders
                .Select(o => string.IsNullOrWhiteSpace(o.AvisoStatus) ? "nicht avisiert" : o.AvisoStatus.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var suppliers = orders
                .SelectMany(o => o.Products ?? [])
                .Select(p => NormalizeSupplier(p.Supplier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            UpdateFilterOptions(OrderStatusFilters, statuses);
            UpdateFilterOptions(DeliveryTypeFilters, deliveryTypes);
            UpdateFilterOptions(AvisoStatusFilters, avisoStatuses);
            UpdateFilterOptions(SupplierFilters, suppliers);
        }
        finally
        {
            _isUpdatingFilterOptions = false;
        }

        OnPropertyChanged(nameof(ToggleAllFiltersButtonText));
    }

    private void UpdateFilterOptions(ObservableCollection<MapOrderFilterOption> target, IReadOnlyList<string> values)
    {
        var previouslyAvailable = target
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previouslySelected = target
            .Where(x => x.IsSelected)
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepCurrentSelection = target.Count > 0;

        foreach (var option in target)
        {
            option.PropertyChanged -= OnOrderFilterOptionChanged;
        }

        target.Clear();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var isNewValue = !previouslyAvailable.Contains(value);
            var isSelected = !keepCurrentSelection || previouslySelected.Contains(value) || isNewValue;
            var option = new MapOrderFilterOption(value, isSelected);
            option.PropertyChanged += OnOrderFilterOptionChanged;
            target.Add(option);
        }
    }

    private static string NormalizeOrderStatus(string? status)
    {
        var normalized = Order.NormalizeOrderStatus(status);
        if (string.Equals(normalized, "Bereits eingeplant", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultOrderStatus;
        }

        return normalized;
    }

    private static int GetOrderStatusSortIndex(string? status)
    {
        var normalized = NormalizeOrderStatus(status);
        for (var i = 0; i < KnownStatusOptions.Count; i++)
        {
            if (string.Equals(KnownStatusOptions[i], normalized, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return KnownStatusOptions.Count;
    }

    private void TriggerOrderFilterRefresh()
    {
        OnPropertyChanged(nameof(ToggleAllFiltersButtonText));
        if (_suppressFilterRefresh)
        {
            return;
        }

        RebuildGrid();
    }

    private void OnOrderFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingFilterOptions || e.PropertyName != nameof(MapOrderFilterOption.IsSelected))
        {
            return;
        }

        TriggerOrderFilterRefresh();
    }

    private void ResetOrderFilters()
    {
        _suppressFilterRefresh = true;
        try
        {
            IncludeOpenOrders = true;
            IncludePlannedOrders = true;
            OrderSectionSharedHelpers.SetAllFilterOptions(OrderStatusFilters, true);
            OrderSectionSharedHelpers.SetAllFilterOptions(DeliveryTypeFilters, true);
            OrderSectionSharedHelpers.SetAllFilterOptions(AvisoStatusFilters, true);
            OrderSectionSharedHelpers.SetAllFilterOptions(SupplierFilters, true);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        TriggerOrderFilterRefresh();
    }

    private void ToggleAllOrderFilters()
    {
        var targetState = !AreAllFiltersSelected();
        _suppressFilterRefresh = true;
        try
        {
            IncludeOpenOrders = targetState;
            IncludePlannedOrders = targetState;
            OrderSectionSharedHelpers.SetAllFilterOptions(OrderStatusFilters, targetState);
            OrderSectionSharedHelpers.SetAllFilterOptions(DeliveryTypeFilters, targetState);
            OrderSectionSharedHelpers.SetAllFilterOptions(AvisoStatusFilters, targetState);
            OrderSectionSharedHelpers.SetAllFilterOptions(SupplierFilters, targetState);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        TriggerOrderFilterRefresh();
    }

    private bool AreAllFiltersSelected()
    {
        return IncludeOpenOrders &&
               IncludePlannedOrders &&
               OrderStatusFilters.All(x => x.IsSelected) &&
               DeliveryTypeFilters.All(x => x.IsSelected) &&
               AvisoStatusFilters.All(x => x.IsSelected) &&
               SupplierFilters.All(x => x.IsSelected);
    }

    private void OnOrdersChanged(object? sender, OrderChangedEventArgs args)
    {
        if (_workflowService.IsOwnChange(args))
        {
            return;
        }

        _ = RefreshFromRepositoryAsync(ResolvePreferredSelectedId(args));
    }

    private string? ResolvePreferredSelectedId(OrderChangedEventArgs args)
    {
        var selectedOrderId = SelectedOrder?.Id;
        if (!string.IsNullOrWhiteSpace(selectedOrderId) &&
            string.Equals(selectedOrderId, args.PreviousOrderId, StringComparison.OrdinalIgnoreCase))
        {
            return args.CurrentOrderId;
        }

        return OrderSectionWorkflowService.ResolvePreferredSelectedId(args, selectedOrderId);
    }

    private void PublishOrderChange(string? previousOrderId, string? currentOrderId)
    {
        _workflowService.PublishOrderChange(previousOrderId, currentOrderId);
    }

    private async Task ReconcileToursWithOrdersAsync(IEnumerable<Order> orders)
    {
        await _workflowService.ReconcileToursWithOrdersAsync(orders);
    }

    private bool MatchesTourAssignmentFilter(Order order)
    {
        var isAssigned = !string.IsNullOrWhiteSpace(order.AssignedTourId);
        return isAssigned ? IncludePlannedOrders : IncludeOpenOrders;
    }

    private bool MatchesSelectedFilters(Order order)
    {
        var selectedOrderStatuses = OrderSectionSharedHelpers.GetSelectedFilterLabels(OrderStatusFilters);
        if (selectedOrderStatuses.Count > 0 &&
            selectedOrderStatuses.Count != OrderStatusFilters.Count &&
            !selectedOrderStatuses.Contains(NormalizeOrderStatus(order.OrderStatus)))
        {
            return false;
        }

        var selectedDeliveryTypes = OrderSectionSharedHelpers.GetSelectedFilterLabels(DeliveryTypeFilters);
        if (selectedDeliveryTypes.Count > 0 &&
            selectedDeliveryTypes.Count != DeliveryTypeFilters.Count &&
            !selectedDeliveryTypes.Contains(DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(order.DeliveryType)))
        {
            return false;
        }

        var selectedAvisoStatuses = OrderSectionSharedHelpers.GetSelectedFilterLabels(AvisoStatusFilters);
        var avisoStatus = string.IsNullOrWhiteSpace(order.AvisoStatus) ? "nicht avisiert" : order.AvisoStatus.Trim();
        if (selectedAvisoStatuses.Count > 0 &&
            selectedAvisoStatuses.Count != AvisoStatusFilters.Count &&
            !selectedAvisoStatuses.Contains(avisoStatus))
        {
            return false;
        }

        var selectedSuppliers = OrderSectionSharedHelpers.GetSelectedFilterLabels(SupplierFilters)
            .Select(NormalizeSupplier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return selectedSuppliers.Count == 0 ||
               selectedSuppliers.Count == SupplierFilters.Count ||
               OrderSectionSharedHelpers.OrderContainsAnySupplier(order, selectedSuppliers, NormalizeSupplier);
    }

    private static string NormalizeSupplier(string? supplier)
    {
        var normalized = (supplier ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? UnspecifiedSupplierFilterOption
            : normalized;
    }

    private static OrderItem ToOrderItem(Order order)
    {
        var palette = OrderStatusDisplayPalette.Resolve(order);
        return new OrderItem
        {
            Id = order.Id,
            CustomerName = order.CustomerName,
            Address = order.Address,
            ScheduledDate = order.ScheduledDate.ToString("dd.MM.yyyy"),
            DeliveryDate = order.DeliveryDate?.ToString("dd.MM.yyyy") ?? string.Empty,
            DeliveryPeriod = order.DeliveryPeriod ?? string.Empty,
            AssignedTourId = order.AssignedTourId ?? string.Empty,
            Latitude = order.Location?.Latitude.ToString("0.######") ?? string.Empty,
            Longitude = order.Location?.Longitude.ToString("0.######") ?? string.Empty,
            OrderAddressName = order.OrderAddress?.Name ?? string.Empty,
            OrderAddressStreet = order.OrderAddress?.Street ?? string.Empty,
            OrderAddressHouseNumber = order.OrderAddress?.HouseNumber ?? string.Empty,
            OrderAddressPostalCode = order.OrderAddress?.PostalCode ?? string.Empty,
            OrderAddressCity = order.OrderAddress?.City ?? string.Empty,
            DeliveryName = OrderSectionSharedHelpers.ResolveDeliveryName(order),
            DeliveryContactPerson = order.DeliveryAddress?.ContactPerson ?? string.Empty,
            DeliveryStreet = order.DeliveryAddress?.Street ?? string.Empty,
            DeliveryHouseNumber = order.DeliveryAddress?.HouseNumber ?? string.Empty,
            DeliveryPostalCode = order.DeliveryAddress?.PostalCode ?? string.Empty,
            DeliveryCity = order.DeliveryAddress?.City ?? string.Empty,
            Email = order.Email ?? string.Empty,
            Phone = order.Phone ?? string.Empty,
            DeliveryType = DeliveryMethodExtensions.GetPlanningDeliveryDisplayLabel(order),
            OrderStatus = NormalizeOrderStatus(order.OrderStatus),
            DeliveryProgress = BuildDeliveryProgress(order),
            OrderStatusBadgeBackground = palette.BackgroundHex,
            OrderStatusBadgeBorderBrush = palette.BorderHex,
            OrderStatusBadgeForeground = palette.ForegroundHex,
            ProductsSummary = OrderSectionSharedHelpers.BuildProductsSummary(order),
            Notes = BuildDeliveryNotesSummary(order),
            IstVorauszahlung = order.IstVorauszahlung,
            IsArchived = order.IsArchived,
            IsXmlImported = order.IsXmlImported,
            IsLocationManuallySet = order.IsLocationManuallySet,
            ManualLocationRequiresReview = order.ManualLocationRequiresReview
        };
    }

    private void SelectOrderById(string? orderId)
    {
        SelectedOrder = OrderSectionWorkflowService.SelectOrderById(MapOrders, orderId);
    }

    private static string BuildDeliveryProgress(Order order)
    {
        var deliveries = order.Deliveries ?? [];
        if (deliveries.Count < 2)
        {
            return string.Empty;
        }
        var total = (order.Products ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Name)).Sum(x => Math.Max(0, OrderProductInfo.NormalizeQuantity(x.Quantity)));
        var delivered = deliveries.Where(x => x.Status == OrderDeliveryStatus.Delivered).SelectMany(x => x.Lines ?? []).Sum(x => Math.Max(0, x.Quantity));
        var active = deliveries.Count(x => x.Status != OrderDeliveryStatus.Cancelled);
        return $"{delivered} von {total} geliefert · {active} Lieferung(en)";
    }

    private static string BuildDeliveryNotesSummary(Order order)
    {
        var deliveries = (order.Deliveries ?? []).OrderBy(x => x.SequenceNumber).ToList();
        return deliveries.Count < 2
            ? order.Notes ?? string.Empty
            : string.Join(Environment.NewLine, deliveries.Select(x => x.Notes).Where(x => !string.IsNullOrWhiteSpace(x)));
    }


    private static void RaiseCanExecuteChangedIfSupported(ICommand command)
    {
        if (command is DelegateCommand delegateCommand)
        {
            delegateCommand.RaiseCanExecuteChanged();
            return;
        }

        if (command is AsyncCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
    }

    private static Order CloneOrder(Order source)
    {
        return new Order
        {
            Id = source.Id,
            CustomerName = source.CustomerName,
            Address = source.Address,
            ScheduledDate = source.ScheduledDate,
            Type = source.Type,
            Location = source.Location is null ? null : new GeoPoint(source.Location.Latitude, source.Location.Longitude),
            IsLocationManuallySet = source.IsLocationManuallySet,
            ManualLocationAddress = source.ManualLocationAddress,
            ManualLocationRequiresReview = source.ManualLocationRequiresReview,
            AssignedTourId = source.AssignedTourId,
            OrderAddress = new OrderAddressInfo
            {
                Name = source.OrderAddress?.Name ?? string.Empty,
                ContactPerson = source.OrderAddress?.ContactPerson ?? string.Empty,
                Street = source.OrderAddress?.Street ?? string.Empty,
                HouseNumber = source.OrderAddress?.HouseNumber ?? string.Empty,
                PostalCode = source.OrderAddress?.PostalCode ?? string.Empty,
                City = source.OrderAddress?.City ?? string.Empty
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = source.DeliveryAddress?.Name ?? string.Empty,
                ContactPerson = source.DeliveryAddress?.ContactPerson ?? string.Empty,
                Street = source.DeliveryAddress?.Street ?? string.Empty,
                HouseNumber = source.DeliveryAddress?.HouseNumber ?? string.Empty,
                PostalCode = source.DeliveryAddress?.PostalCode ?? string.Empty,
                City = source.DeliveryAddress?.City ?? string.Empty
            },
            Email = source.Email,
            Phone = source.Phone,
            Products = (source.Products ?? []).Select(p => new OrderProductInfo
            {
                PositionId = p.PositionId,
                ExternalPositionId = p.ExternalPositionId,
                Name = p.Name,
                Supplier = p.Supplier,
                ExpectedDelivery = p.ExpectedDelivery,
                Quantity = p.Quantity,
                UnitWeightKg = p.UnitWeightKg,
                WeightKg = p.WeightKg,
                Dimensions = p.Dimensions,
                DeliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(p.DeliveryStatus)
            }).ToList(),
            Deliveries = (source.Deliveries ?? []).Select(d => new OrderDelivery
            {
                Id = d.Id,
                SequenceNumber = d.SequenceNumber,
                Status = d.Status,
                PlannedDeliveryDate = d.PlannedDeliveryDate,
                IsPlannedDeliveryDateExplicit = d.IsPlannedDeliveryDateExplicit,
                DeliveredAt = d.DeliveredAt,
                AssignedTourId = d.AssignedTourId,
                Notes = d.Notes,
                Lines = (d.Lines ?? []).Select(line => new OrderDeliveryLine
                {
                    OrderProductPositionId = line.OrderProductPositionId,
                    Quantity = line.Quantity
                }).ToList()
            }).ToList(),
            ManualTotalWeightKg = source.ManualTotalWeightKg,
            ManualTotalWeightInfo = source.ManualTotalWeightInfo,
            DeliveryType = source.DeliveryType,
            IsAlternativeDeliveryEnabled = source.IsAlternativeDeliveryEnabled,
            IsSelfPickupAlternativeEnabled = source.IsSelfPickupAlternativeEnabled,
            OrderStatus = source.OrderStatus,
            Notes = source.Notes,
            IstVorauszahlung = source.IstVorauszahlung,
            IstVorauszahlungBezahlt = source.IstVorauszahlungBezahlt,
            IsArchived = source.IsArchived,
            IsXmlImported = source.IsXmlImported,
            ConcurrencyToken = source.ConcurrencyToken
        };
    }
}



