using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class NonMapOrdersSectionViewModel : SectionViewModelBase
{
    private readonly JsonOrderRepository _repository;
    private readonly List<Order> _allOrders = new();
    private Order? _lastDeletedOrder;
    private int _lastDeletedIndex = -1;

    private string _searchText = string.Empty;
    private string _statusText = "Loading non-map orders...";
    private OrderItem? _selectedOrder;

    public NonMapOrdersSectionViewModel(string ordersJsonPath)
        : base("Non-Map Orders", "Orders currently without mappable coordinates.")
    {
        _repository = new JsonOrderRepository(ordersJsonPath);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => NonMapOrders.Count > 0);
        AddCommand = new DelegateCommand(AddOrder);
        AddManualOrderCommand = new AsyncCommand(AddManualOrderAsync);
        EditSelectedOrderCommand = new AsyncCommand(EditSelectedOrderAsync, () => SelectedOrder is not null);
        UndoDeleteCommand = new AsyncCommand(UndoDeleteAsync, () => _lastDeletedOrder is not null);
        RemoveCommand = new AsyncCommand(RemoveSelectedOrderAsync, () => SelectedOrder is not null);
        _ = RefreshAsync();
    }

    public ObservableCollection<OrderItem> NonMapOrders { get; } = new();

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
        _allOrders.Clear();
        _allOrders.AddRange(await _repository.GetAllAsync());
        RebuildGrid();
    }

    public async Task SaveAsync()
    {
        await _repository.SaveAllAsync(_allOrders);
        StatusText = $"Nicht-Karten-Aufträge gespeichert: {_allOrders.Count(x => x.Type == OrderType.NonMap)}";
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
        var dialog = new ManualOrderDialogWindow
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
        await RefreshAsync();
        SelectedOrder = NonMapOrders.FirstOrDefault(x => string.Equals(x.Id, createdOrder.Id, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Nicht-Karten-Auftrag {createdOrder.Id} wurde gespeichert.";
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
        updated.Type = OrderType.NonMap;
        updated.AssignedTourId = existing.AssignedTourId;
        updated.Location = null;

        _allOrders.RemoveAll(x => string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase));
        _allOrders.RemoveAll(x => !string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        _allOrders.Add(updated);

        await _repository.SaveAllAsync(_allOrders);
        await RefreshAsync();
        SelectedOrder = NonMapOrders.FirstOrDefault(x => string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Nicht-Karten-Auftrag {updated.Id} wurde aktualisiert.";
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

        _lastDeletedOrder = CloneOrder(_allOrders[index]);
        _lastDeletedIndex = index;
        _allOrders.RemoveAt(index);

        await _repository.SaveAllAsync(_allOrders);
        await RefreshAsync();
        StatusText = $"Nicht-Karten-Auftrag {SelectedOrder.Id} wurde gelöscht. Mit 'Zurück' wiederherstellen.";
        RaiseCommandStates();
    }

    private void RebuildGrid()
    {
        var query = (_searchText ?? string.Empty).Trim();
        var items = _allOrders.Where(o => o.Type == OrderType.NonMap);
        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(o =>
                o.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (o.AssignedTourId ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        NonMapOrders.Clear();
        foreach (var order in items.OrderBy(o => o.ScheduledDate).ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            NonMapOrders.Add(new OrderItem
            {
                Id = order.Id,
                CustomerName = order.CustomerName,
                Address = order.Address,
                ScheduledDate = order.ScheduledDate.ToString("yyyy-MM-dd"),
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
                DeliveryType = order.DeliveryType ?? string.Empty,
                OrderStatus = order.OrderStatus ?? string.Empty,
                ProductsSummary = string.Join(", ", (order.Products ?? []).Select(p => $"{p.Name} ({p.WeightKg:0.##} kg)")),
                Notes = order.Notes ?? string.Empty
            });
        }

        SelectedOrder = NonMapOrders.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void UpdateStatusText()
    {
        var assigned = NonMapOrders.Count(x => !string.IsNullOrWhiteSpace(x.AssignedTourId));
        StatusText = $"Non-map orders: {NonMapOrders.Count} | Assigned: {assigned} | Unassigned: {NonMapOrders.Count - assigned}";
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
        await RefreshAsync();
        SelectedOrder = NonMapOrders.FirstOrDefault(x => string.Equals(x.Id, restoreOrder.Id, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Nicht-Karten-Auftrag {restoreOrder.Id} wurde wiederhergestellt.";
        RaiseCommandStates();
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
                WeightKg = p.WeightKg
            }).ToList(),
            DeliveryType = source.DeliveryType,
            OrderStatus = source.OrderStatus,
            Notes = source.Notes
        };
    }
}
