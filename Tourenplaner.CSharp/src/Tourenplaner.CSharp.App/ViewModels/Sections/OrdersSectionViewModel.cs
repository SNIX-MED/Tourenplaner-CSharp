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
    private readonly JsonOrderRepository _repository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly List<Order> _allOrders = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private Order? _lastDeletedOrder;
    private int _lastDeletedIndex = -1;

    private string _searchText = string.Empty;
    private string _statusText = "Loading map orders...";
    private OrderItem? _selectedOrder;

    public OrdersSectionViewModel(string ordersJsonPath, AppDataSyncService dataSyncService)
        : base("Orders", "Map orders with address, assignment and filtering.")
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
        _dataSyncService.OrdersChanged += OnOrdersChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<OrderItem> MapOrders { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand AddManualOrderCommand { get; }

    public ICommand EditSelectedOrderCommand { get; }

    public ICommand UndoDeleteCommand { get; }

    public ICommand RemoveCommand { get; }

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

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

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
        var dialog = new ManualOrderDialogWindow
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

        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.Id, createdOrder.Id, StringComparison.OrdinalIgnoreCase));
        PublishOrderChange(null, createdOrder.Id);
        StatusText = createdOrder.Location is null
            ? $"Auftrag {createdOrder.Id} gespeichert, aber Adresse konnte nicht automatisch geokodiert werden."
            : $"Auftrag {createdOrder.Id} wurde gespeichert.";
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
        var dialog = new ManualOrderDialogWindow(existing)
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
        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
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
        RaiseCommandStates();
    }

    private void RebuildGrid(string? preferredSelectedId = null)
    {
        var selectedOrderId = string.IsNullOrWhiteSpace(preferredSelectedId)
            ? SelectedOrder?.Id
            : preferredSelectedId;
        var query = (_searchText ?? string.Empty).Trim();
        var map = _allOrders.Where(o => o.Type == OrderType.Map);
        if (!string.IsNullOrWhiteSpace(query))
        {
            map = map.Where(o =>
                o.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (o.AssignedTourId ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        MapOrders.Clear();
        foreach (var order in map.OrderBy(o => o.ScheduledDate).ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            MapOrders.Add(new OrderItem
            {
                Id = order.Id,
                CustomerName = order.CustomerName,
                Address = order.Address,
                ScheduledDate = order.ScheduledDate.ToString("yyyy-MM-dd"),
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
                DeliveryType = order.DeliveryType ?? string.Empty,
                OrderStatus = order.OrderStatus ?? string.Empty,
                ProductsSummary = OrderProductFormatter.BuildSummary(order.Products),
                Notes = order.Notes ?? string.Empty
            });
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
        StatusText = $"Map orders: {MapOrders.Count} | Assigned: {assigned} | Unassigned: {MapOrders.Count - assigned}";
    }

    private void RaiseCommandStates()
    {
        if (SaveCommand is AsyncCommand save)
        {
            save.RaiseCanExecuteChanged();
        }

        if (RemoveCommand is AsyncCommand remove)
        {
            remove.RaiseCanExecuteChanged();
        }

        if (EditSelectedOrderCommand is AsyncCommand edit)
        {
            edit.RaiseCanExecuteChanged();
        }

        if (UndoDeleteCommand is AsyncCommand undo)
        {
            undo.RaiseCanExecuteChanged();
        }
    }

    private static DateOnly ParseDateOrToday(string? value)
    {
        if (DateOnly.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return DateOnly.FromDateTime(DateTime.Today);
    }

    private static GeoPoint? TryBuildLocation(string? lat, string? lon)
    {
        if (!double.TryParse(lat, out var latValue))
        {
            return null;
        }

        if (!double.TryParse(lon, out var lonValue))
        {
            return null;
        }

        return new GeoPoint(latValue, lonValue);
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
        SelectedOrder = MapOrders.FirstOrDefault(x => string.Equals(x.Id, restoreOrder.Id, StringComparison.OrdinalIgnoreCase));
        PublishOrderChange(null, restoreOrder.Id);
        StatusText = $"Auftrag {restoreOrder.Id} wurde wiederhergestellt.";
        RaiseCommandStates();
    }

    private async Task RefreshFromRepositoryAsync(string? preferredSelectedId = null)
    {
        _allOrders.Clear();
        _allOrders.AddRange(await _repository.GetAllAsync());
        RebuildGrid(preferredSelectedId);
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
                Quantity = p.Quantity,
                UnitWeightKg = p.UnitWeightKg,
                WeightKg = p.WeightKg,
                Dimensions = p.Dimensions
            }).ToList(),
            DeliveryType = source.DeliveryType,
            OrderStatus = source.OrderStatus,
            Notes = source.Notes
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
}

