using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class OrdersSectionViewModel : SectionViewModelBase
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
    private string _statusText = "Lade Aufträge...";
    private OrderItem? _selectedOrder;
    private string _selectedDeliveryTypeFilter = AllDeliveryTypesLabel;
    private string _selectedStatusFilter = AllStatusesLabel;
    private bool _isCustomerColumnVisible = true;
    private bool _isDeliveryAddressColumnVisible = true;
    private bool _isDeliveryPersonColumnVisible = true;
    private bool _showArchivedOrders;

    public OrdersSectionViewModel(string ordersJsonPath, AppDataSyncService dataSyncService)
        : base("Aufträge", "Aufträge mit Adresse, Zuordnung und Filterung.")
    {
        _repository = new JsonOrderRepository(ordersJsonPath);
        _dataSyncService = dataSyncService;

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => MapOrders.Count > 0);
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

    public ObservableCollection<OrderItem> MapOrders { get; } = new();
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
        StatusText = $"Aufträge gespeichert: {_allOrders.Count(x => x.Type == OrderType.Map)}";
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
            deliveryTypes: DeliveryMethodExtensions.MapDeliveryTypeOptions,
            defaultOrderType: OrderType.Map)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var createdOrder = dialog.CreatedOrder;
        createdOrder.Location ??= await AddressGeocodingService.TryGeocodeOrderAsync(createdOrder);

        _allOrders.RemoveAll(x => string.Equals(x.Id, createdOrder.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(createdOrder);

        await _repository.SaveAllAsync(_allOrders);
        await RefreshFromRepositoryAsync(createdOrder.Id);
        SelectOrderById(createdOrder.Id);
        PublishOrderChange(null, createdOrder.Id);
        StatusText = createdOrder.Location is null
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
        var dialog = new ManualOrderDialogWindow(
            existing,
            deliveryTypes: DeliveryMethodExtensions.MapDeliveryTypeOptions,
            defaultOrderType: OrderType.Map)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var updated = dialog.CreatedOrder;
        updated.Type = OrderType.Map;
        updated.AssignedTourId = existing.AssignedTourId;
        updated.Location = await AddressGeocodingService.TryGeocodeOrderAsync(updated) ?? existing.Location;

        _allOrders.RemoveAll(x => string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase));
        _allOrders.RemoveAll(x => !string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(updated);

        await _repository.SaveAllAsync(_allOrders);
        await RefreshFromRepositoryAsync(updated.Id);
        SelectOrderById(updated.Id);
        PublishOrderChange(originalId, updated.Id);
        StatusText = $"Auftrag {updated.Id} wurde aktualisiert.";
    }

    private async Task RemoveSelectedOrderAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var index = _allOrders.FindIndex(x =>
            x.Type == OrderType.Map &&
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
        StatusText = $"Auftrag {removedOrderId} wurde gelöscht. Mit 'Zurück' wiederherstellen.";
        ToastNotificationService.ShowInfo($"Auftrag {removedOrderId} wurde gelöscht.");
        RaiseCommandStates();
    }

    private void RebuildGrid(string? preferredSelectedId = null)
    {
        var selectedOrderId = string.IsNullOrWhiteSpace(preferredSelectedId)
            ? SelectedOrder?.Id
            : preferredSelectedId;
        var query = (_searchText ?? string.Empty).Trim();
        var map = _allOrders
            .Where(o => o.Type == OrderType.Map)
            .Where(o => o.IsArchived == ShowArchivedOrders)
            .Where(o => MatchesDeliveryTypeFilter(o))
            .Where(o => MatchesStatusFilter(o))
            .Where(o => MatchesSearchQuery(o, query));

        MapOrders.Clear();
        foreach (var order in map.OrderBy(o => o.ScheduledDate).ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            MapOrders.Add(ToOrderItem(order));
        }

        SelectedOrder = string.IsNullOrWhiteSpace(selectedOrderId)
            ? MapOrders.FirstOrDefault()
            : MapOrders.FirstOrDefault(x => string.Equals(x.Id, selectedOrderId, StringComparison.OrdinalIgnoreCase))
                ?? MapOrders.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void UpdateStatusText()
    {
        var assigned = MapOrders.Count(x => !string.IsNullOrWhiteSpace(x.AssignedTourId));
        var modeLabel = ShowArchivedOrders ? "Archiviert" : "Aktiv";
        StatusText = $"Aufträge ({modeLabel}): {MapOrders.Count} | Zugeordnet: {assigned} | Offen: {MapOrders.Count - assigned}";
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
        StatusText = $"Auftrag {restoreOrder.Id} wurde wiederhergestellt.";
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
        foreach (var item in DeliveryMethodExtensions.MapDeliveryTypeOptions)
        {
            DeliveryTypeFilterOptions.Add(item);
        }

        var statusOptions = _allOrders
            .Where(o => o.Type == OrderType.Map)
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
            Latitude = order.Location?.Latitude.ToString("0.######") ?? string.Empty,
            Longitude = order.Location?.Longitude.ToString("0.######") ?? string.Empty,
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
            SelectedOrder = MapOrders.FirstOrDefault();
            return;
        }

        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase))
                        ?? MapOrders.FirstOrDefault();
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
            Location = source.Location is null ? null : new GeoPoint(source.Location.Latitude, source.Location.Longitude),
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

public sealed class OrderItem : ObservableObject
{
    private string _id = string.Empty;
    private string _customerName = string.Empty;
    private string _address = string.Empty;
    private string _scheduledDate = string.Empty;
    private string _assignedTourId = string.Empty;
    private string _latitude = string.Empty;
    private string _longitude = string.Empty;
    private string _orderAddressName = string.Empty;
    private string _orderAddressStreet = string.Empty;
    private string _orderAddressPostalCode = string.Empty;
    private string _orderAddressCity = string.Empty;
    private string _deliveryName = string.Empty;
    private string _deliveryContactPerson = string.Empty;
    private string _deliveryStreet = string.Empty;
    private string _deliveryPostalCode = string.Empty;
    private string _deliveryCity = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _deliveryType = string.Empty;
    private string _orderStatus = string.Empty;
    private string _productsSummary = string.Empty;
    private string _notes = string.Empty;
    private bool _isArchived;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string CustomerName
    {
        get => _customerName;
        set => SetProperty(ref _customerName, value);
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string ScheduledDate
    {
        get => _scheduledDate;
        set => SetProperty(ref _scheduledDate, value);
    }

    public string OrderAddressLine
    {
        get
        {
            var street = (OrderAddressStreet ?? string.Empty).Trim();
            var postal = (OrderAddressPostalCode ?? string.Empty).Trim();
            var city = (OrderAddressCity ?? string.Empty).Trim();
            var postalCity = string.Join(' ', new[] { postal, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.Join(", ", new[] { street, postalCity }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }

    public string DeliveryStreetLine
    {
        get
        {
            var street = (DeliveryStreet ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(street) ? (Address ?? string.Empty).Trim() : street;
        }
    }

    public string DeliveryPostalCityLine
    {
        get
        {
            var postal = (DeliveryPostalCode ?? string.Empty).Trim();
            var city = (DeliveryCity ?? string.Empty).Trim();
            return string.Join(' ', new[] { postal, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }

    public string DeliveryPersonPrimary
    {
        get
        {
            var contact = (DeliveryContactPerson ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(contact))
            {
                return contact;
            }

            return (DeliveryName ?? string.Empty).Trim();
        }
    }

    public string DeliveryPersonSecondary
    {
        get
        {
            var phone = (Phone ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(phone))
            {
                return string.Empty;
            }

            var primary = DeliveryPersonPrimary;
            return string.Equals(primary, phone, StringComparison.OrdinalIgnoreCase) ? string.Empty : phone;
        }
    }

    public string AssignedTourId
    {
        get => _assignedTourId;
        set => SetProperty(ref _assignedTourId, value);
    }

    public string Latitude
    {
        get => _latitude;
        set => SetProperty(ref _latitude, value);
    }

    public string Longitude
    {
        get => _longitude;
        set => SetProperty(ref _longitude, value);
    }

    public string OrderAddressName
    {
        get => _orderAddressName;
        set => SetProperty(ref _orderAddressName, value);
    }

    public string OrderAddressStreet
    {
        get => _orderAddressStreet;
        set => SetProperty(ref _orderAddressStreet, value);
    }

    public string OrderAddressPostalCode
    {
        get => _orderAddressPostalCode;
        set => SetProperty(ref _orderAddressPostalCode, value);
    }

    public string OrderAddressCity
    {
        get => _orderAddressCity;
        set => SetProperty(ref _orderAddressCity, value);
    }

    public string DeliveryName
    {
        get => _deliveryName;
        set => SetProperty(ref _deliveryName, value);
    }

    public string DeliveryContactPerson
    {
        get => _deliveryContactPerson;
        set => SetProperty(ref _deliveryContactPerson, value);
    }

    public string DeliveryStreet
    {
        get => _deliveryStreet;
        set => SetProperty(ref _deliveryStreet, value);
    }

    public string DeliveryPostalCode
    {
        get => _deliveryPostalCode;
        set => SetProperty(ref _deliveryPostalCode, value);
    }

    public string DeliveryCity
    {
        get => _deliveryCity;
        set => SetProperty(ref _deliveryCity, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string DeliveryType
    {
        get => _deliveryType;
        set => SetProperty(ref _deliveryType, value);
    }

    public string OrderStatus
    {
        get => _orderStatus;
        set => SetProperty(ref _orderStatus, value);
    }

    public string ProductsSummary
    {
        get => _productsSummary;
        set => SetProperty(ref _productsSummary, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsArchived
    {
        get => _isArchived;
        set => SetProperty(ref _isArchived, value);
    }
}

