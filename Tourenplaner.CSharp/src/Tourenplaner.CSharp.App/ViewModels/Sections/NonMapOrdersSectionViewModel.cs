using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class NonMapOrdersSectionViewModel : SectionViewModelBase
{
    private readonly JsonOrderRepository _repository;
    private readonly OrderPartitionService _partitionService;
    private readonly List<Order> _allOrders = new();

    private string _searchText = string.Empty;
    private string _statusText = "Loading non-map orders...";
    private OrderItem? _selectedOrder;

    public NonMapOrdersSectionViewModel(string ordersJsonPath)
        : base("Non-Map Orders", "Orders currently without mappable coordinates.")
    {
        _repository = new JsonOrderRepository(ordersJsonPath);
        _partitionService = new OrderPartitionService();
        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => NonMapOrders.Count > 0);
        AddCommand = new DelegateCommand(AddOrder);
        RemoveCommand = new DelegateCommand(RemoveSelectedOrder, () => SelectedOrder is not null);
        _ = RefreshAsync();
    }

    public ObservableCollection<OrderItem> NonMapOrders { get; } = new();

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
        var updated = NonMapOrders
            .Where(o => !string.IsNullOrWhiteSpace(o.CustomerName))
            .Select(o => new Order
            {
                Id = string.IsNullOrWhiteSpace(o.Id) ? Guid.NewGuid().ToString() : o.Id.Trim(),
                CustomerName = (o.CustomerName ?? string.Empty).Trim(),
                Address = (o.Address ?? string.Empty).Trim(),
                Type = OrderType.NonMap,
                ScheduledDate = ParseDateOrToday(o.ScheduledDate),
                AssignedTourId = string.IsNullOrWhiteSpace(o.AssignedTourId) ? null : o.AssignedTourId.Trim(),
                Location = null
            })
            .ToList();

        var merged = _partitionService.MergeNonMapOrders(_allOrders, updated);
        await _repository.SaveAllAsync(merged);
        await RefreshAsync();
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

    private void RemoveSelectedOrder()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        NonMapOrders.Remove(SelectedOrder);
        SelectedOrder = NonMapOrders.FirstOrDefault();
        UpdateStatusText();
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
                Longitude = string.Empty
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
}
