using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class NonMapOrdersSectionViewModel : SectionViewModelBase
{
    private const string AllDeliveryTypesLabel = "Alle Lieferarten";
    private const string AllStatusesLabel = "Alle Status";
    private const string DefaultOrderStatus = Order.DefaultOrderStatus;
    private static readonly IReadOnlyList<string> KnownStatusOptions =
    [
        DefaultOrderStatus,
        Order.OrderedStatus,
        Order.InTransitStatus,
        Order.PartiallyInTransitStatus,
        Order.PartiallyReadyStatus,
        Order.ReadyToDeliverStatus
    ];

    private readonly JsonOrderRepository _repository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly List<Order> _allOrders = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private Order? _lastDeletedOrder;
    private int _lastDeletedIndex = -1;

    private string _searchText = string.Empty;
    private string _statusText = "Lade Post/Spedition/Abholung...";
    private OrderItem? _selectedOrder;
    private string _selectedDeliveryTypeFilter = AllDeliveryTypesLabel;
    private string _selectedStatusFilter = AllStatusesLabel;
    private bool _isCustomerColumnVisible = true;
    private bool _isDeliveryAddressColumnVisible = true;
    private bool _isDeliveryPersonColumnVisible = true;
    private bool _showArchivedOrders;

    public NonMapOrdersSectionViewModel(string ordersJsonPath, AppDataSyncService dataSyncService)
        : base("Post/Spedition/Abholung", "Auftraege fuer Post, Spedition oder Selbstabholung.")
    {
        _repository = new JsonOrderRepository(ordersJsonPath);
        _dataSyncService = dataSyncService;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => NonMapOrders.Count > 0);
        AddCommand = new DelegateCommand(AddOrder);
        AddManualOrderCommand = new AsyncCommand(AddManualOrderAsync);
        EditSelectedOrderCommand = new AsyncCommand(EditSelectedOrderAsync, () => SelectedOrder is not null);
        UndoDeleteCommand = new AsyncCommand(UndoDeleteAsync, () => _lastDeletedOrder is not null);
        RemoveCommand = new AsyncCommand(RemoveSelectedOrderAsync, () => SelectedOrder is not null);
        ShowActiveOrdersCommand = new DelegateCommand(() => ShowArchivedOrders = false);
        ShowArchivedOrdersCommand = new DelegateCommand(() => ShowArchivedOrders = true);
        ToggleArchiveModeCommand = new DelegateCommand(() => ShowArchivedOrders = !ShowArchivedOrders);
        _dataSyncService.OrdersChanged += OnOrdersChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<OrderItem> NonMapOrders { get; } = new();
    public ObservableCollection<string> DeliveryTypeFilterOptions { get; } = [];
    public ObservableCollection<string> StatusFilterOptions { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand AddManualOrderCommand { get; }

    public ICommand EditSelectedOrderCommand { get; }

    public ICommand UndoDeleteCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand ShowActiveOrdersCommand { get; }

    public ICommand ShowArchivedOrdersCommand { get; }

    public ICommand ToggleArchiveModeCommand { get; }

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

    public OrderItem? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
            {
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
        SyncDerivedOrderStatuses(_allOrders);
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

        await _repository.SaveAllAsync(_allOrders);
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

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var updated = dialog.CreatedOrder;
        updated.Type = OrderType.NonMap;
        updated.AssignedTourId = existing.AssignedTourId;
        updated.Location = null;

        _allOrders.RemoveAll(x => string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase));
        _allOrders.RemoveAll(x => !string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(updated);

        await _repository.SaveAllAsync(_allOrders);
        await RefreshFromRepositoryAsync(updated.Id);
        SelectOrderById(updated.Id);
        PublishOrderChange(originalId, updated.Id);
        StatusText = $"Auftrag {updated.Id} wurde aktualisiert (Post/Spedition/Abholung).";
    }

    private async Task RemoveSelectedOrderAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var index = _allOrders.FindIndex(x =>
            x.Type == OrderType.NonMap &&
            string.Equals(x.Id, SelectedOrder.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        var removedOrderId = SelectedOrder.Id;
        _lastDeletedOrder = CloneOrder(_allOrders[index]);
        _lastDeletedIndex = index;
        _allOrders.RemoveAt(index);

        await _repository.SaveAllAsync(_allOrders);
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
            .Where(o => MatchesDeliveryTypeFilter(o))
            .Where(o => MatchesStatusFilter(o))
            .Where(o => MatchesSearchQuery(o, query));

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
        await _repository.SaveAllAsync(_allOrders);
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
        if (SyncDerivedOrderStatuses(_allOrders))
        {
            await _repository.SaveAllAsync(_allOrders);
        }

        UpdateFilterOptions();
        RebuildGrid(preferredSelectedId);
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

    private static bool SyncDerivedOrderStatuses(IEnumerable<Order> orders)
    {
        var changed = false;
        foreach (var order in orders)
        {
            if (order is null)
            {
                continue;
            }

            var derivedStatus = Order.ResolveOrderStatusFromProducts(order.Products);
            if (string.Equals(Order.NormalizeOrderStatus(order.OrderStatus), derivedStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            order.OrderStatus = derivedStatus;
            changed = true;
        }

        return changed;
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

    private static bool MatchesSearchQuery(Order order, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return order.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               order.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               order.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (order.AssignedTourId ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static OrderItem ToOrderItem(Order order)
    {
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
            OrderAddressPostalCode = order.OrderAddress?.PostalCode ?? string.Empty,
            OrderAddressCity = order.OrderAddress?.City ?? string.Empty,
            DeliveryName = order.DeliveryAddress?.Name ?? order.CustomerName,
            DeliveryContactPerson = order.DeliveryAddress?.ContactPerson ?? string.Empty,
            DeliveryStreet = order.DeliveryAddress?.Street ?? string.Empty,
            DeliveryPostalCode = order.DeliveryAddress?.PostalCode ?? string.Empty,
            DeliveryCity = order.DeliveryAddress?.City ?? string.Empty,
            Email = order.Email ?? string.Empty,
            Phone = order.Phone ?? string.Empty,
            DeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(order.DeliveryType),
            OrderStatus = NormalizeOrderStatus(order.OrderStatus),
            ProductsSummary = OrderProductFormatter.BuildSummary(order.Products),
            Notes = order.Notes ?? string.Empty,
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
                PostalCode = source.OrderAddress?.PostalCode ?? string.Empty,
                City = source.OrderAddress?.City ?? string.Empty
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = source.DeliveryAddress?.Name ?? string.Empty,
                ContactPerson = source.DeliveryAddress?.ContactPerson ?? string.Empty,
                Street = source.DeliveryAddress?.Street ?? string.Empty,
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
            IsArchived = source.IsArchived
        };
    }
}

