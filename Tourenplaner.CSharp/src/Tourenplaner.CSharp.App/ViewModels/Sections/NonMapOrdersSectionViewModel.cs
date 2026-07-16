using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class NonMapOrdersSectionViewModel : SectionViewModelBase
{
    private const string AllDeliveryTypesLabel = "Alle Lieferarten";
    private const string AllStatusesLabel = "Alle Status";
    private const string DefaultOrderStatus = Order.DefaultOrderStatus;
    private const string UnspecifiedSupplierFilterOption = "nicht Festgelegt";
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

    private readonly IOrderRepository _repository;
    private readonly ITourRecordStore _tourRepository;
    private readonly IOrderMutationRepository? _mutationRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly Func<int, Task>? _openTourAsync;
    private readonly List<Order> _allOrders = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private Order? _lastDeletedOrder;
    private int _lastDeletedIndex = -1;

    private string _searchText = string.Empty;
    private string _statusText = "Lade Post/Spedition/Abholung...";
    private OrderItem? _selectedOrder;
    private string _selectedDeliveryTypeFilter = AllDeliveryTypesLabel;
    private string _selectedStatusFilter = AllStatusesLabel;
    private bool _includeOpenOrders = true;
    private bool _includePlannedOrders = true;
    private bool _isUpdatingFilterOptions;
    private bool _suppressFilterRefresh;
    private bool _isCustomerColumnVisible = true;
    private bool _isDeliveryAddressColumnVisible = true;
    private bool _isDeliveryPersonColumnVisible = true;
    private bool _isNonMapOrdersFilterPanelVisible;
    private bool _showArchivedOrders;

    public NonMapOrdersSectionViewModel(IOrderRepository repository, ITourRecordStore tourRepository, AppDataSyncService dataSyncService, Func<int, Task>? openTourAsync = null)
        : base("Post/Spedition/Abholung", "Aufträge für Post, Spedition oder Selbstabholung.")
    {
        _repository = repository;
        _tourRepository = tourRepository;
        _mutationRepository = repository as IOrderMutationRepository;
        _dataSyncService = dataSyncService;
        _openTourAsync = openTourAsync;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => NonMapOrders.Count > 0);
        AddCommand = new DelegateCommand(AddOrder);
        AddManualOrderCommand = new AsyncCommand(AddManualOrderAsync);
        EditSelectedOrderCommand = new AsyncCommand(EditSelectedOrderAsync, () => SelectedOrder is not null);
        ShowAssignedTourCommand = new AsyncCommand(ShowAssignedTourAsync, CanShowAssignedTour);
        CopySelectedOrderNumberCommand = new DelegateCommand(CopySelectedOrderNumber, () => SelectedOrder is not null);
        SetSelectedOrderStatusDefaultCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.DefaultOrderStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusOrderedCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.OrderedStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusInTransitCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.InTransitStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusPartiallyInTransitCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.PartiallyInTransitStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusPartiallyReadyCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.PartiallyReadyStatus), () => SelectedOrder is not null);
        SetSelectedOrderStatusReadyCommand = new AsyncCommand(() => SetSelectedOrderStatusAsync(Order.ReadyToDeliverStatus), () => SelectedOrder is not null);
        ToggleArchiveSelectedOrderCommand = new AsyncCommand(ToggleArchiveSelectedOrderAsync, () => SelectedOrder is not null);
        UndoDeleteCommand = new AsyncCommand(UndoDeleteAsync, () => _lastDeletedOrder is not null);
        RemoveCommand = new AsyncCommand(RemoveSelectedOrderAsync, () => SelectedOrder is not null);
        ShowActiveOrdersCommand = new DelegateCommand(() => ShowArchivedOrders = false);
        ShowArchivedOrdersCommand = new DelegateCommand(() => ShowArchivedOrders = true);
        ToggleArchiveModeCommand = new DelegateCommand(() => ShowArchivedOrders = !ShowArchivedOrders);
        ResetOrderFiltersCommand = new DelegateCommand(ResetOrderFilters);
        ToggleAllOrderFiltersCommand = new DelegateCommand(ToggleAllOrderFilters);
        _dataSyncService.OrdersChanged += OnOrdersChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<OrderItem> NonMapOrders { get; } = new();
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

    public ICommand ShowAssignedTourCommand { get; }

    public ICommand CopySelectedOrderNumberCommand { get; }

    public ICommand SetSelectedOrderStatusDefaultCommand { get; }

    public ICommand SetSelectedOrderStatusOrderedCommand { get; }

    public ICommand SetSelectedOrderStatusInTransitCommand { get; }

    public ICommand SetSelectedOrderStatusPartiallyInTransitCommand { get; }

    public ICommand SetSelectedOrderStatusPartiallyReadyCommand { get; }

    public ICommand SetSelectedOrderStatusReadyCommand { get; }

    public ICommand ToggleArchiveSelectedOrderCommand { get; }

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

    public bool IsDeliveryPersonColumnVisible
    {
        get => _isDeliveryPersonColumnVisible;
        set => SetProperty(ref _isDeliveryPersonColumnVisible, value);
    }

    public bool IsNonMapOrdersFilterPanelVisible
    {
        get => _isNonMapOrdersFilterPanelVisible;
        set => SetProperty(ref _isNonMapOrdersFilterPanelVisible, value);
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

    public OrderItem? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
            {
                OnPropertyChanged(nameof(ToggleArchiveSelectedOrderMenuText));
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        await RefreshFromRepositoryAsync();
    }

    public async Task SaveAsync()
    {
        OrderSectionSharedHelpers.SyncDerivedOrderStatuses(_allOrders);
        await _repository.SaveAllAsync(_allOrders);
        PublishOrderChange(SelectedOrder?.Id, SelectedOrder?.Id);
        StatusText = $"Post/Spedition/Abholung gespeichert: {_allOrders.Count(x => x.Type == OrderType.NonMap)}";
    }

    private void AddOrder()
    {
        var item = new OrderItem
        {
            Id = Guid.NewGuid().ToString(),
            CustomerName = "New non-map order",
            Address = string.Empty,
            ScheduledDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
            Latitude = string.Empty,
            Longitude = string.Empty
        };

        NonMapOrders.Insert(0, item);
        SelectedOrder = item;
        UpdateStatusText();
        RaiseCommandStates();
    }

    private async Task AddManualOrderAsync()
    {
        var dialog = new ManualOrderDialogWindow(
            deliveryTypes: DeliveryMethodExtensions.NonMapDeliveryTypeOptions,
            defaultOrderType: OrderType.NonMap)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var createdOrder = dialog.CreatedOrder;
        createdOrder.Type = OrderType.NonMap;
        createdOrder.Location = null;

        _allOrders.RemoveAll(x => string.Equals(x.Id, createdOrder.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(createdOrder);

        if (!await SaveOrderAsync(createdOrder))
        {
            return;
        }
        await RefreshFromRepositoryAsync(createdOrder.Id);
        SelectOrderById(createdOrder.Id);
        PublishOrderChange(null, createdOrder.Id);
        StatusText = $"Auftrag {createdOrder.Id} wurde gespeichert (Post/Spedition/Abholung).";
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
        var dialog = new ManualOrderDialogWindow(
            existing,
            deliveryTypes: DeliveryMethodExtensions.NonMapDeliveryTypeOptions,
            defaultOrderType: OrderType.NonMap)
        {
            Owner = System.Windows.Application.Current?.MainWindow
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
        updated.Type = OrderType.NonMap;
        updated.AssignedTourId = existing.AssignedTourId;
        updated.ConcurrencyToken = existing.ConcurrencyToken;

        if (!await ConfirmManualArchiveForAssignedActiveTourAsync(existing, updated.IsArchived))
        {
            return;
        }

        updated.Location = null;

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
        await RefreshFromRepositoryAsync(updated.Id);
        SelectOrderById(updated.Id);
        PublishOrderChange(originalId, updated.Id);
        StatusText = $"Auftrag {updated.Id} wurde aktualisiert (Post/Spedition/Abholung).";
    }

    private async Task<bool> ConfirmManualArchiveForAssignedActiveTourAsync(Order existing, bool nextIsArchived)
    {
        if (existing.IsArchived || !nextIsArchived)
        {
            return true;
        }

        var assignedTourId = (existing.AssignedTourId ?? string.Empty).Trim();
        if (!int.TryParse(assignedTourId, out var assignedTourIdNumber) || assignedTourIdNumber <= 0)
        {
            return true;
        }

        var tours = await _tourRepository.LoadAsync();
        var assignedTour = tours.FirstOrDefault(t => t.Id == assignedTourIdNumber);
        if (assignedTour is null || assignedTour.IsArchived)
        {
            return true;
        }

        var tourLabel = string.IsNullOrWhiteSpace(assignedTour.Name)
            ? $"Tour {assignedTour.Id}"
            : $"{assignedTour.Name.Trim()} (Tour {assignedTour.Id})";
        var confirmation = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            $"Der Auftrag ist in {tourLabel} eingeplant, und diese Tour ist nicht archiviert.{Environment.NewLine}{Environment.NewLine}" +
            "Auftrag trotzdem archivieren?",
            "Auftrag in aktiver Tour",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return confirmation == MessageBoxResult.Yes;
    }

    private async Task ToggleArchiveSelectedOrderAsync()
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

        var nextIsArchived = !target.IsArchived;
        if (!await ConfirmManualArchiveForAssignedActiveTourAsync(target, nextIsArchived))
        {
            return;
        }

        target.IsArchived = nextIsArchived;
        if (!await SaveOrderAsync(target))
        {
            return;
        }
        await RefreshFromRepositoryAsync(target.Id);
        SelectOrderById(target.Id);
        PublishOrderChange(target.Id, target.Id);
        StatusText = $"Auftrag {target.Id} wurde {(target.IsArchived ? "archiviert" : "reaktiviert")} (Post/Spedition/Abholung).";
        ToastNotificationService.ShowInfo(StatusText);
    }

    private async Task ShowAssignedTourAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var assignedTourIdText = (SelectedOrder.AssignedTourId ?? string.Empty).Trim();
        if (!int.TryParse(assignedTourIdText, out var tourId) || tourId <= 0)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Für diesen Auftrag ist keine gültige Tour zugeordnet.",
                "Tour anzeigen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_openTourAsync is null)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"Der Auftrag ist Tour {tourId} zugeordnet.",
                "Tour anzeigen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await _openTourAsync(tourId);
    }

    private bool CanShowAssignedTour()
    {
        return SelectedOrder is not null &&
               int.TryParse((SelectedOrder.AssignedTourId ?? string.Empty).Trim(), out var tourId) &&
               tourId > 0;
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
        if (SelectedOrder is null)
        {
            return;
        }

        var orderId = SelectedOrder.Id;
        var confirmation = Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            $"Soll der Auftrag {orderId} wirklich gelöscht werden?",
            "Auftrag löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var index = _allOrders.FindIndex(x =>
            x.Type == OrderType.NonMap &&
            string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        var removedOrderId = orderId;
        _lastDeletedOrder = CloneOrder(_allOrders[index]);
        _lastDeletedIndex = index;
        _allOrders.RemoveAt(index);

        if (!await DeleteOrderAsync(removedOrderId, _lastDeletedOrder?.ConcurrencyToken))
        {
            _lastDeletedOrder = null;
            _lastDeletedIndex = -1;
            RaiseCommandStates();
            return;
        }
        await RefreshFromRepositoryAsync();
        PublishOrderChange(removedOrderId, null);
        StatusText = $"Auftrag {removedOrderId} wurde geloescht. Mit 'Zurueck' wiederherstellen.";
        ToastNotificationService.ShowInfo($"Auftrag {removedOrderId} wurde gelöscht.");
        RaiseCommandStates();
    }

    private void RebuildGrid(string? preferredSelectedId = null)
    {
        var selectedOrderId = string.IsNullOrWhiteSpace(preferredSelectedId)
            ? SelectedOrder?.Id
            : preferredSelectedId;
        var query = (_searchText ?? string.Empty).Trim();
        var items = _allOrders
            .Where(o => o.Type == OrderType.NonMap)
            .Where(o => o.IsArchived == ShowArchivedOrders)
            .Where(MatchesTourAssignmentFilter)
            .Where(MatchesSelectedFilters)
            .Where(o => OrderSectionSharedHelpers.MatchesSearchQuery(o, query));

        NonMapOrders.Clear();
        foreach (var order in items.OrderBy(o => o.ScheduledDate).ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            NonMapOrders.Add(ToOrderItem(order));
        }

        SelectedOrder = string.IsNullOrWhiteSpace(selectedOrderId)
            ? NonMapOrders.FirstOrDefault()
            : NonMapOrders.FirstOrDefault(x => string.Equals(x.Id, selectedOrderId, StringComparison.OrdinalIgnoreCase))
                ?? NonMapOrders.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void UpdateStatusText()
    {
        var assigned = NonMapOrders.Count(x => !string.IsNullOrWhiteSpace(x.AssignedTourId));
        var modeLabel = ShowArchivedOrders ? "Archiviert" : "Aktiv";
        StatusText = $"Post/Spedition/Abholung ({modeLabel}): {NonMapOrders.Count} | Zugeordnet: {assigned} | Offen: {NonMapOrders.Count - assigned}";
    }

    private void RaiseCommandStates()
    {
        RaiseCanExecuteChangedIfSupported(SaveCommand);
        RaiseCanExecuteChangedIfSupported(RemoveCommand);
        RaiseCanExecuteChangedIfSupported(EditSelectedOrderCommand);
        RaiseCanExecuteChangedIfSupported(ShowAssignedTourCommand);
        RaiseCanExecuteChangedIfSupported(CopySelectedOrderNumberCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusDefaultCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusOrderedCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusInTransitCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusPartiallyInTransitCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusPartiallyReadyCommand);
        RaiseCanExecuteChangedIfSupported(SetSelectedOrderStatusReadyCommand);
        RaiseCanExecuteChangedIfSupported(ToggleArchiveSelectedOrderCommand);
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
        StatusText = $"Auftrag {restoreOrder.Id} wurde wiederhergestellt (Post/Spedition/Abholung).";
        RaiseCommandStates();
    }

    private async Task RefreshFromRepositoryAsync(string? preferredSelectedId = null)
    {
        _allOrders.Clear();
        _allOrders.AddRange(await _repository.GetAllAsync());
        if (OrderSectionSharedHelpers.SyncDerivedOrderStatuses(_allOrders))
        {
            await _repository.SaveAllAsync(_allOrders);
        }

        UpdateFilterOptions();
        RebuildGrid(preferredSelectedId);
    }

    private async Task<bool> SaveOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_mutationRepository is not null)
            {
                await _mutationRepository.UpsertAsync(order, cancellationToken);
            }
            else
            {
                await _repository.SaveAllAsync(_allOrders, cancellationToken);
            }

            return true;
        }
        catch (ConcurrencyConflictException)
        {
            await HandleConcurrencyConflictAsync(order.Id);
            return false;
        }
    }

    private async Task<bool> DeleteOrderAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_mutationRepository is not null)
            {
                await _mutationRepository.DeleteAsync(orderId, concurrencyToken, cancellationToken);
            }
            else
            {
                await _repository.SaveAllAsync(_allOrders, cancellationToken);
            }

            return true;
        }
        catch (ConcurrencyConflictException)
        {
            await HandleConcurrencyConflictAsync(orderId);
            return false;
        }
    }

    private async Task HandleConcurrencyConflictAsync(string? preferredSelectedId)
    {
        await RefreshFromRepositoryAsync(preferredSelectedId);
        Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            "Der Auftrag wurde zwischenzeitlich von einem anderen Benutzer geaendert oder geloescht. Die Liste wurde neu geladen.",
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
        foreach (var item in DeliveryMethodExtensions.NonMapDeliveryTypeOptions)
        {
            DeliveryTypeFilterOptions.Add(item);
        }

        var statusOptions = _allOrders
            .Where(o => o.Type == OrderType.NonMap)
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
                .Where(o => o.Type == OrderType.NonMap)
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
        if (args.SourceId == _instanceId)
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

        return selectedOrderId;
    }

    private void PublishOrderChange(string? previousOrderId, string? currentOrderId)
    {
        _dataSyncService.PublishOrders(_instanceId, previousOrderId, currentOrderId);
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

    private bool MatchesDeliveryTypeFilter(Order order)
    {
        if (string.IsNullOrWhiteSpace(_selectedDeliveryTypeFilter) ||
            string.Equals(_selectedDeliveryTypeFilter, AllDeliveryTypesLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(order.DeliveryType),
            _selectedDeliveryTypeFilter,
            StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesStatusFilter(Order order)
    {
        if (string.IsNullOrWhiteSpace(_selectedStatusFilter) ||
            string.Equals(_selectedStatusFilter, AllStatusesLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            NormalizeOrderStatus(order.OrderStatus),
            _selectedStatusFilter,
            StringComparison.OrdinalIgnoreCase);
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
            AssignedTourId = order.AssignedTourId ?? string.Empty,
            Latitude = string.Empty,
            Longitude = string.Empty,
            OrderAddressName = order.OrderAddress?.Name ?? string.Empty,
            OrderAddressStreet = order.OrderAddress?.Street ?? string.Empty,
            OrderAddressHouseNumber = order.OrderAddress?.HouseNumber ?? string.Empty,
            OrderAddressPostalCode = order.OrderAddress?.PostalCode ?? string.Empty,
            OrderAddressCity = order.OrderAddress?.City ?? string.Empty,
            DeliveryName = order.DeliveryAddress?.Name ?? order.CustomerName,
            DeliveryContactPerson = order.DeliveryAddress?.ContactPerson ?? string.Empty,
            DeliveryStreet = order.DeliveryAddress?.Street ?? string.Empty,
            DeliveryHouseNumber = order.DeliveryAddress?.HouseNumber ?? string.Empty,
            DeliveryPostalCode = order.DeliveryAddress?.PostalCode ?? string.Empty,
            DeliveryCity = order.DeliveryAddress?.City ?? string.Empty,
            Email = order.Email ?? string.Empty,
            Phone = order.Phone ?? string.Empty,
            DeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(order.DeliveryType),
            OrderStatus = NormalizeOrderStatus(order.OrderStatus),
            OrderStatusBadgeBackground = palette.BackgroundHex,
            OrderStatusBadgeBorderBrush = palette.BorderHex,
            OrderStatusBadgeForeground = palette.ForegroundHex,
            ProductsSummary = OrderProductFormatter.BuildSummary(order.Products),
            Notes = order.Notes ?? string.Empty,
            IstVorauszahlung = order.IstVorauszahlung,
            IsArchived = order.IsArchived
        };
    }

    private void SelectOrderById(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            SelectedOrder = NonMapOrders.FirstOrDefault();
            return;
        }

        SelectedOrder = NonMapOrders.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase))
                        ?? NonMapOrders.FirstOrDefault();
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
            Location = null,
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
                Name = p.Name,
                Supplier = p.Supplier,
                Quantity = p.Quantity,
                UnitWeightKg = p.UnitWeightKg,
                WeightKg = p.WeightKg,
                Dimensions = p.Dimensions,
                DeliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(p.DeliveryStatus)
            }).ToList(),
            DeliveryType = source.DeliveryType,
            OrderStatus = source.OrderStatus,
            Notes = source.Notes,
            IstVorauszahlung = source.IstVorauszahlung,
            IsArchived = source.IsArchived,
            ConcurrencyToken = source.ConcurrencyToken
        };
    }
}



