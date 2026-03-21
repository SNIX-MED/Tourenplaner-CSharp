using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class KarteSectionViewModel : SectionViewModelBase
{
    private readonly JsonOrderRepository _orderRepository;
    private readonly JsonToursRepository _tourRepository;
    private readonly RouteOptimizationService _optimizationService;
    private readonly MapRouteService _mapRouteService;
    private readonly List<Order> _allOrders = new();

    private string _searchText = string.Empty;
    private bool _showOnlyUnassigned = true;
    private MapOrderItem? _selectedOrder;
    private RouteStopItem? _selectedRouteStop;
    private string _routeName = "Neue Karte-Tour";
    private string _routeDate = DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy");
    private string _routeStartTime = "08:00";
    private double _routeDistanceKm;
    private string _statusText = "Loading map orders...";

    public KarteSectionViewModel(string ordersJsonPath, string toursJsonPath)
        : base("Karte", "Map order review, marker filters, route panel and save-to-tour workflow.")
    {
        _orderRepository = new JsonOrderRepository(ordersJsonPath);
        _tourRepository = new JsonToursRepository(toursJsonPath);
        _optimizationService = new RouteOptimizationService();
        _mapRouteService = new MapRouteService();

        RefreshCommand = new AsyncCommand(RefreshAsync);
        AddToRouteCommand = new DelegateCommand(AddSelectedOrderToRoute, () => SelectedOrder is not null);
        RemoveFromRouteCommand = new DelegateCommand(RemoveSelectedRouteStop, () => SelectedRouteStop is not null);
        MoveStopUpCommand = new DelegateCommand(MoveSelectedStopUp, () => CanMoveSelectedStop(-1));
        MoveStopDownCommand = new DelegateCommand(MoveSelectedStopDown, () => CanMoveSelectedStop(1));
        OptimizeRouteCommand = new DelegateCommand(OptimizeRoute, () => RouteStops.Count > 2);
        SaveRouteAsTourCommand = new AsyncCommand(SaveRouteAsTourAsync, () => RouteStops.Count > 0);
        ClearRouteCommand = new DelegateCommand(ClearRoute, () => RouteStops.Count > 0);

        _ = RefreshAsync();
    }

    public ObservableCollection<MapOrderItem> MapOrders { get; } = new();

    public ObservableCollection<RouteStopItem> RouteStops { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand AddToRouteCommand { get; }

    public ICommand RemoveFromRouteCommand { get; }

    public ICommand MoveStopUpCommand { get; }

    public ICommand MoveStopDownCommand { get; }

    public ICommand OptimizeRouteCommand { get; }

    public ICommand SaveRouteAsTourCommand { get; }

    public ICommand ClearRouteCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildOrderGrid();
            }
        }
    }

    public bool ShowOnlyUnassigned
    {
        get => _showOnlyUnassigned;
        set
        {
            if (SetProperty(ref _showOnlyUnassigned, value))
            {
                RebuildOrderGrid();
            }
        }
    }

    public string RouteName
    {
        get => _routeName;
        set => SetProperty(ref _routeName, value);
    }

    public string RouteDate
    {
        get => _routeDate;
        set => SetProperty(ref _routeDate, value);
    }

    public string RouteStartTime
    {
        get => _routeStartTime;
        set => SetProperty(ref _routeStartTime, value);
    }

    public double RouteDistanceKm
    {
        get => _routeDistanceKm;
        private set => SetProperty(ref _routeDistanceKm, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public MapOrderItem? SelectedOrder
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

    public RouteStopItem? SelectedRouteStop
    {
        get => _selectedRouteStop;
        set
        {
            if (SetProperty(ref _selectedRouteStop, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        _allOrders.Clear();
        _allOrders.AddRange(await _orderRepository.GetAllAsync());
        RebuildOrderGrid();
    }

    public void AddOrderToRouteById(string orderId)
    {
        var match = MapOrders.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        SelectedOrder = match;
        AddSelectedOrderToRoute();
    }

    public IReadOnlyList<RouteStopItem> GetRouteSnapshot()
    {
        return RouteStops.Select(x => new RouteStopItem
        {
            Position = x.Position,
            OrderId = x.OrderId,
            Customer = x.Customer,
            Address = x.Address,
            Latitude = x.Latitude,
            Longitude = x.Longitude
        }).ToList();
    }

    public void SelectRouteStopByOrderId(string orderId)
    {
        var match = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedRouteStop = match;
        }
    }

    public void SwapRouteStops(string sourceOrderId, string targetOrderId)
    {
        var swapped = _mapRouteService.SwapStops(ToMapRouteStops(), sourceOrderId, targetOrderId);
        ApplyRouteStops(swapped, sourceOrderId);
    }

    public void UpdateRouteStopCoordinates(string orderId, double lat, double lon)
    {
        var match = RouteStops.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        match.Latitude = lat;
        match.Longitude = lon;
        RebuildPositions();
    }

    private void RebuildOrderGrid()
    {
        var routeOrderIds = RouteStops.Select(s => s.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = (_searchText ?? string.Empty).Trim();
        IEnumerable<Order> filtered = _allOrders
            .Where(o => o.Type == OrderType.Map && o.Location is not null)
            .Where(o => !routeOrderIds.Contains(o.Id));

        if (_showOnlyUnassigned)
        {
            filtered = filtered.Where(o => string.IsNullOrWhiteSpace(o.AssignedTourId));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(o =>
                o.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (o.AssignedTourId ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        MapOrders.Clear();
        foreach (var order in filtered.OrderBy(o => o.ScheduledDate).ThenBy(o => o.CustomerName, StringComparer.OrdinalIgnoreCase))
        {
            MapOrders.Add(new MapOrderItem
            {
                OrderId = order.Id,
                Customer = order.CustomerName,
                Address = order.Address,
                ScheduledDate = order.ScheduledDate.ToString("yyyy-MM-dd"),
                AssignedTourId = order.AssignedTourId ?? string.Empty,
                IsAssigned = !string.IsNullOrWhiteSpace(order.AssignedTourId),
                Latitude = order.Location!.Latitude,
                Longitude = order.Location!.Longitude
            });
        }

        SelectedOrder = MapOrders.FirstOrDefault();
        UpdateStatus();
        RaiseCommandStates();
    }

    private void AddSelectedOrderToRoute()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        RouteStops.Add(new RouteStopItem
        {
            Position = RouteStops.Count + 1,
            OrderId = SelectedOrder.OrderId,
            Customer = SelectedOrder.Customer,
            Address = SelectedOrder.Address,
            Latitude = SelectedOrder.Latitude,
            Longitude = SelectedOrder.Longitude
        });

        RebuildPositions();
        RebuildOrderGrid();
    }

    private void RemoveSelectedRouteStop()
    {
        if (SelectedRouteStop is null)
        {
            return;
        }

        RouteStops.Remove(SelectedRouteStop);
        SelectedRouteStop = RouteStops.FirstOrDefault();
        RebuildPositions();
        RebuildOrderGrid();
    }

    private void MoveSelectedStopUp()
    {
        MoveSelectedStop(-1);
    }

    private void MoveSelectedStopDown()
    {
        MoveSelectedStop(1);
    }

    private void MoveSelectedStop(int delta)
    {
        if (SelectedRouteStop is null)
        {
            return;
        }

        var moved = _mapRouteService.MoveStop(ToMapRouteStops(), SelectedRouteStop.OrderId, delta);
        ApplyRouteStops(moved, SelectedRouteStop.OrderId);
    }

    private bool CanMoveSelectedStop(int delta)
    {
        if (SelectedRouteStop is null)
        {
            return false;
        }

        var index = RouteStops.IndexOf(SelectedRouteStop);
        var newIndex = index + delta;
        return index >= 0 && newIndex >= 0 && newIndex < RouteStops.Count;
    }

    private void OptimizeRoute()
    {
        if (RouteStops.Count < 3)
        {
            return;
        }

        var optimized = _optimizationService.OptimizeNearestNeighbor(
            RouteStops.ToList(),
            x => x.Latitude,
            x => x.Longitude);

        RouteStops.Clear();
        foreach (var stop in optimized)
        {
            RouteStops.Add(stop);
        }

        RebuildPositions();
        StatusText = "Route optimized (nearest-neighbor).";
    }

    private async Task SaveRouteAsTourAsync()
    {
        if (RouteStops.Count == 0)
        {
            return;
        }

        var tours = (await _tourRepository.LoadAsync()).ToList();
        var nextId = _mapRouteService.DetermineNextTourId(tours);
        var tour = _mapRouteService.BuildTour(ToMapRouteStops(), nextId, RouteName, RouteDate, RouteStartTime, defaultServiceMinutes: 10);

        tours.Add(tour);
        await _tourRepository.SaveAsync(tours);

        var routeOrderIds = _mapRouteService.ExtractRouteOrderIds(ToMapRouteStops());
        foreach (var order in _allOrders.Where(o => routeOrderIds.Contains(o.Id)))
        {
            order.AssignedTourId = nextId.ToString();
        }

        await _orderRepository.SaveAllAsync(_allOrders);
        ClearRoute();
        RebuildOrderGrid();
        StatusText = $"Route saved as tour #{nextId}.";
    }

    private IReadOnlyList<MapRouteStop> ToMapRouteStops()
    {
        return RouteStops
            .Select(x => new MapRouteStop(x.Position, x.OrderId, x.Customer, x.Address, x.Latitude, x.Longitude))
            .ToList();
    }

    private void ApplyRouteStops(IReadOnlyList<MapRouteStop> routeStops, string? selectedOrderId = null)
    {
        RouteStops.Clear();
        foreach (var stop in routeStops)
        {
            RouteStops.Add(new RouteStopItem
            {
                Position = stop.Position,
                OrderId = stop.OrderId,
                Customer = stop.Customer,
                Address = stop.Address,
                Latitude = stop.Latitude,
                Longitude = stop.Longitude
            });
        }

        RebuildPositions();
        if (!string.IsNullOrWhiteSpace(selectedOrderId))
        {
            SelectRouteStopByOrderId(selectedOrderId);
        }
    }

    private void ClearRoute()
    {
        RouteStops.Clear();
        SelectedRouteStop = null;
        RebuildPositions();
        RebuildOrderGrid();
    }

    private void RebuildPositions()
    {
        for (var i = 0; i < RouteStops.Count; i++)
        {
            RouteStops[i].Position = i + 1;
        }

        OnPropertyChanged(nameof(RouteStops));
        RouteDistanceKm = _optimizationService.ComputeTotalDistanceKm(RouteStops, x => x.Latitude, x => x.Longitude);
        UpdateStatus();
        RaiseCommandStates();
    }

    private void UpdateStatus()
    {
        StatusText = $"Map orders: {MapOrders.Count} | Route stops: {RouteStops.Count} | Route distance: {RouteDistanceKm:0.##} km";
    }

    private void RaiseCommandStates()
    {
        if (AddToRouteCommand is DelegateCommand add)
        {
            add.RaiseCanExecuteChanged();
        }

        if (RemoveFromRouteCommand is DelegateCommand remove)
        {
            remove.RaiseCanExecuteChanged();
        }

        if (MoveStopUpCommand is DelegateCommand up)
        {
            up.RaiseCanExecuteChanged();
        }

        if (MoveStopDownCommand is DelegateCommand down)
        {
            down.RaiseCanExecuteChanged();
        }

        if (OptimizeRouteCommand is DelegateCommand optimize)
        {
            optimize.RaiseCanExecuteChanged();
        }

        if (SaveRouteAsTourCommand is AsyncCommand save)
        {
            save.RaiseCanExecuteChanged();
        }

        if (ClearRouteCommand is DelegateCommand clear)
        {
            clear.RaiseCanExecuteChanged();
        }
    }
}

public sealed class MapOrderItem
{
    public string OrderId { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ScheduledDate { get; set; } = string.Empty;
    public string AssignedTourId { get; set; } = string.Empty;
    public bool IsAssigned { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class RouteStopItem : ObservableObject
{
    private int _position;

    public int Position
    {
        get => _position;
        set => SetProperty(ref _position, value);
    }

    public string OrderId { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
