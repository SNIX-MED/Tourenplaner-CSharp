using System.Collections.ObjectModel;
using System.Text.Json;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

public sealed class OrderSectionWorkflowService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderMutationRepository? _orderMutationRepository;
    private readonly ITourRecordStore _tourRepository;
    private readonly IAppSettingsStore _settingsRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly Func<int, Task>? _openTourAsync;
    private readonly string _geocodeCachePath;
    private readonly Guid _sourceId;
    private readonly OrderDeliveryService _deliveryService = new();

    public event EventHandler<OrderChangedEventArgs>? OrdersChanged
    {
        add => _dataSyncService.OrdersChanged += value;
        remove => _dataSyncService.OrdersChanged -= value;
    }

    public bool HasOrderMutationRepository => _orderMutationRepository is not null;

    public OrderSectionWorkflowService(
        IOrderRepository orderRepository,
        ITourRecordStore tourRepository,
        IAppSettingsStore settingsRepository,
        AppDataSyncService dataSyncService,
        string geocodeCachePath,
        Guid sourceId,
        Func<int, Task>? openTourAsync = null)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(tourRepository);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(dataSyncService);
        ArgumentException.ThrowIfNullOrWhiteSpace(geocodeCachePath);

        _orderRepository = orderRepository;
        _orderMutationRepository = orderRepository as IOrderMutationRepository;
        _tourRepository = tourRepository;
        _settingsRepository = settingsRepository;
        _dataSyncService = dataSyncService;
        _geocodeCachePath = geocodeCachePath;
        _sourceId = sourceId;
        _openTourAsync = openTourAsync;
    }

    public async Task<OrderWorkflowLoadResult> LoadAndNormalizeAsync(
        bool normalizeDeliveries = true,
        CancellationToken cancellationToken = default)
    {
        var orders = (await _orderRepository.GetAllAsync(cancellationToken)).ToList();
        var originalOrders = OrderMaintenancePersistenceService.CreateSnapshot(orders);
        var changed = false;

        if (normalizeDeliveries)
        {
            foreach (var order in orders)
            {
                changed |= _deliveryService.Normalize(order);
                changed |= _deliveryService.EnsureRemainderDelivery(order) is not null;
                changed |= _deliveryService.DistributeOrderNotesToDeliveries(order);
            }
        }

        changed |= OrderSectionSharedHelpers.SyncDerivedOrderStatuses(orders);
        if (changed)
        {
            await OrderMaintenancePersistenceService.SaveChangesAsync(
                _orderRepository,
                originalOrders,
                orders,
                cancellationToken);
        }

        return new OrderWorkflowLoadResult(orders, changed);
    }

    public Task SaveAllAsync(
        IEnumerable<Order> orders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return _orderRepository.SaveAllAsync(orders, cancellationToken);
    }

    public async Task<OrderWorkflowMutationResult> SaveOrderAsync(
        Order order,
        IReadOnlyCollection<Order> currentOrders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(currentOrders);

        try
        {
            if (_orderMutationRepository is not null)
            {
                await _orderMutationRepository.UpsertAsync(order, cancellationToken);
            }
            else
            {
                await _orderRepository.SaveAllAsync(currentOrders, cancellationToken);
            }

            return OrderWorkflowMutationResult.Success(order.Id);
        }
        catch (ConcurrencyConflictException exception)
        {
            return OrderWorkflowMutationResult.Conflict(order.Id, exception);
        }
    }

    public async Task<OrderWorkflowMutationResult> SaveEditedOrderAsync(
        string? previousOrderId,
        Order candidate,
        IReadOnlyList<Order> currentOrders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(currentOrders);

        try
        {
            await OrderEditPersistenceService.SaveAsync(
                _orderRepository,
                currentOrders,
                candidate,
                cancellationToken);
            return OrderWorkflowMutationResult.Success(candidate.Id, previousOrderId);
        }
        catch (ConcurrencyConflictException exception)
        {
            return OrderWorkflowMutationResult.Conflict(candidate.Id, exception, previousOrderId);
        }
    }

    public async Task<OrderWorkflowMutationResult> DeleteOrderAsync(
        string orderId,
        string? concurrencyToken,
        IReadOnlyCollection<Order> remainingOrders,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(remainingOrders);

        try
        {
            if (_orderMutationRepository is not null)
            {
                await _orderMutationRepository.DeleteAsync(orderId, concurrencyToken, cancellationToken);
            }
            else
            {
                await _orderRepository.SaveAllAsync(remainingOrders, cancellationToken);
            }

            return OrderWorkflowMutationResult.Success(null, orderId);
        }
        catch (ConcurrencyConflictException exception)
        {
            return OrderWorkflowMutationResult.Conflict(orderId, exception, orderId);
        }
    }

    internal async Task<OrderEditorSaveResult> SaveEditorResultAsync(
        Order original, Order candidate, CancellationToken cancellationToken = default)
    {
        var result = await OrderEditorSaveService.SaveAsync(
            _orderRepository, _tourRepository, original, candidate, cancellationToken: cancellationToken);
        if (result.ToursChanged) _dataSyncService.PublishTours(_sourceId);
        return result;
    }

    public async Task ReconcileToursWithOrdersAsync(
        IEnumerable<Order> orders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var tours = (await _tourRepository.LoadAsync(cancellationToken)).ToList();
        var originalTours = TourReconciliationPersistenceService.CreateSnapshot(tours);
        var cleanup = TourOrderReferenceService.ReconcileActiveToursWithOrders(tours, orders);
        if (!cleanup.HasChanges)
        {
            return;
        }

        var scheduleService = new TourScheduleService();
        foreach (var changedTourId in cleanup.RescheduledTourIds)
        {
            var changedTour = tours.FirstOrDefault(tour => tour.Id == changedTourId);
            if (changedTour is not null)
            {
                scheduleService.ApplySchedule(changedTour);
            }
        }

        await TourReconciliationPersistenceService.SaveAsync(
            _tourRepository,
            originalTours,
            tours,
            cleanup,
            cancellationToken);
        _dataSyncService.PublishTours(_sourceId);
    }

    public async Task<AddressGeocodingResult?> ApplyDeliveryMethodRoutingAsync(
        Order order,
        GeoPoint? fallbackLocation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        return await OrderDeliveryRoutingService.ApplyAsync(
            order,
            fallbackLocation,
            candidate => TryResolveOrderAsync(candidate, cancellationToken));
    }

    public async Task<AddressGeocodingResult?> TryResolveOrderAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var settings = await _settingsRepository.LoadAsync(cancellationToken);
        var tomTomApiKey = (settings.TomTomApiKey ?? string.Empty).Trim();
        cancellationToken.ThrowIfCancellationRequested();
        var result = await AddressGeocodingService.TryResolveOrderAsync(
            order,
            tomTomApiKey,
            _geocodeCachePath);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<OrderArchiveAssignment> ResolveArchiveAssignmentAsync(
        Order order,
        bool nextIsArchived,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.IsArchived || !nextIsArchived)
        {
            return OrderArchiveAssignment.NoConfirmationRequired;
        }

        var assignedTourId = (order.AssignedTourId ?? string.Empty).Trim();
        if (!int.TryParse(assignedTourId, out var tourId) || tourId <= 0)
        {
            return OrderArchiveAssignment.NoConfirmationRequired;
        }

        var tours = await _tourRepository.LoadAsync(cancellationToken);
        var assignedTour = tours.FirstOrDefault(tour => tour.Id == tourId);
        if (assignedTour is null || assignedTour.IsArchived)
        {
            return OrderArchiveAssignment.NoConfirmationRequired;
        }

        var tourLabel = string.IsNullOrWhiteSpace(assignedTour.Name)
            ? $"Tour {assignedTour.Id}"
            : $"{assignedTour.Name.Trim()} (Tour {assignedTour.Id})";
        return new OrderArchiveAssignment(true, assignedTour.Id, tourLabel);
    }

    public static bool CanNavigateToAssignedTour(string? assignedTourId)
    {
        return TryParseTourId(assignedTourId, out _);
    }

    public async Task<OrderTourNavigationResult> NavigateToAssignedTourAsync(
        string? assignedTourId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseTourId(assignedTourId, out var tourId))
        {
            return new OrderTourNavigationResult(OrderTourNavigationStatus.InvalidTourId, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_openTourAsync is null)
        {
            return new OrderTourNavigationResult(OrderTourNavigationStatus.NavigationUnavailable, tourId);
        }

        await _openTourAsync(tourId);
        return new OrderTourNavigationResult(OrderTourNavigationStatus.Navigated, tourId);
    }

    public void PublishOrderChange(string? previousOrderId, string? currentOrderId)
    {
        _dataSyncService.PublishOrders(_sourceId, previousOrderId, currentOrderId);
    }

    public bool IsOwnChange(OrderChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.SourceId == _sourceId;
    }

    public static string? ResolvePreferredSelectedId(
        OrderChangedEventArgs args,
        string? selectedOrderId)
    {
        ArgumentNullException.ThrowIfNull(args);

        return !string.IsNullOrWhiteSpace(selectedOrderId) &&
               string.Equals(selectedOrderId, args.PreviousOrderId, StringComparison.OrdinalIgnoreCase)
            ? args.CurrentOrderId
            : selectedOrderId;
    }

    public static void SynchronizeSelection(
        ObservableCollection<OrderItem> target,
        IEnumerable<OrderItem> selectedOrders)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(selectedOrders);

        target.Clear();
        foreach (var order in selectedOrders)
        {
            target.Add(order);
        }
    }

    public static OrderItem? SelectOrderById(
        IEnumerable<OrderItem> visibleOrders,
        string? preferredOrderId)
    {
        ArgumentNullException.ThrowIfNull(visibleOrders);

        var orders = visibleOrders as IReadOnlyList<OrderItem> ?? visibleOrders.ToList();
        if (orders.Count == 0)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(preferredOrderId)
            ? orders[0]
            : orders.FirstOrDefault(order =>
                string.Equals(order.Id, preferredOrderId, StringComparison.OrdinalIgnoreCase))
              ?? orders[0];
    }

    public static string BuildStatusText(
        string sectionLabel,
        IReadOnlyCollection<OrderItem> visibleOrders,
        bool showArchivedOrders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionLabel);
        ArgumentNullException.ThrowIfNull(visibleOrders);

        var assignedCount = visibleOrders.Count(order => !string.IsNullOrWhiteSpace(order.AssignedTourId));
        var modeLabel = showArchivedOrders ? "Archiviert" : "Aktiv";
        return $"{sectionLabel} ({modeLabel}): {visibleOrders.Count} | Zugeordnet: {assignedCount} | Offen: {visibleOrders.Count - assignedCount}";
    }

    public static Order CloneOrder(Order source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(source))
               ?? throw new InvalidOperationException("Auftrag konnte nicht kopiert werden.");
    }

    private static bool TryParseTourId(string? assignedTourId, out int tourId)
    {
        return int.TryParse((assignedTourId ?? string.Empty).Trim(), out tourId) && tourId > 0;
    }
}

public sealed record OrderWorkflowLoadResult(
    IReadOnlyList<Order> Orders,
    bool MaintenanceChangesSaved);

public sealed record OrderWorkflowMutationResult(
    bool Succeeded,
    bool HasConcurrencyConflict,
    string? CurrentOrderId,
    string? PreviousOrderId,
    ConcurrencyConflictException? ConflictException)
{
    public static OrderWorkflowMutationResult Success(
        string? currentOrderId,
        string? previousOrderId = null)
    {
        return new OrderWorkflowMutationResult(
            true,
            false,
            currentOrderId,
            previousOrderId,
            null);
    }

    public static OrderWorkflowMutationResult Conflict(
        string? currentOrderId,
        ConcurrencyConflictException conflict,
        string? previousOrderId = null)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return new OrderWorkflowMutationResult(
            false,
            true,
            currentOrderId,
            previousOrderId,
            conflict);
    }
}

public sealed record OrderArchiveAssignment(
    bool RequiresConfirmation,
    int? TourId,
    string TourLabel)
{
    public static OrderArchiveAssignment NoConfirmationRequired { get; } = new(false, null, string.Empty);
}

public enum OrderTourNavigationStatus
{
    InvalidTourId,
    NavigationUnavailable,
    Navigated
}

public sealed record OrderTourNavigationResult(
    OrderTourNavigationStatus Status,
    int? TourId);
