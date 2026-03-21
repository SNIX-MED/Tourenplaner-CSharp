using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class OrdersSectionViewModel : SectionViewModelBase
{
    private readonly JsonOrderRepository _repository;
    private readonly List<Order> _allOrders = new();

    private string _searchText = string.Empty;
    private string _statusText = "Loading map orders...";
    private OrderItem? _selectedOrder;

    public OrdersSectionViewModel(string ordersJsonPath)
        : base("Orders", "Map orders with address, assignment and filtering.")
    {
        _repository = new JsonOrderRepository(ordersJsonPath);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => MapOrders.Count > 0);
        AddCommand = new DelegateCommand(AddOrder);
        RemoveCommand = new DelegateCommand(RemoveSelectedOrder, () => SelectedOrder is not null);
        _ = RefreshAsync();
    }

    public ObservableCollection<OrderItem> MapOrders { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand AddCommand { get; }

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
        var untouched = _allOrders.Where(o => o.Type == OrderType.NonMap).ToList();
        var updatedMapOrders = MapOrders
            .Where(o => !string.IsNullOrWhiteSpace(o.CustomerName))
            .Select(o => new Order
            {
                Id = string.IsNullOrWhiteSpace(o.Id) ? Guid.NewGuid().ToString() : o.Id.Trim(),
                CustomerName = (o.CustomerName ?? string.Empty).Trim(),
                Address = (o.Address ?? string.Empty).Trim(),
                Type = OrderType.Map,
                ScheduledDate = ParseDateOrToday(o.ScheduledDate),
                AssignedTourId = string.IsNullOrWhiteSpace(o.AssignedTourId) ? null : o.AssignedTourId.Trim(),
                Location = TryBuildLocation(o.Latitude, o.Longitude)
            })
            .ToList();

        var merged = untouched.Concat(updatedMapOrders).ToList();
        await _repository.SaveAllAsync(merged);
        await RefreshAsync();
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

    private void RemoveSelectedOrder()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        MapOrders.Remove(SelectedOrder);
        SelectedOrder = MapOrders.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void RebuildGrid()
    {
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
                Longitude = order.Location?.Longitude.ToString("0.######") ?? string.Empty
            });
        }

        SelectedOrder = MapOrders.FirstOrDefault();
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

        if (RemoveCommand is DelegateCommand remove)
        {
            remove.RaiseCanExecuteChanged();
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
}
