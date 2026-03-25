namespace Tourenplaner.CSharp.App.Services;

[Flags]
public enum AppDataKind
{
    None = 0,
    Orders = 1,
    Tours = 2,
    Vehicles = 4,
    Employees = 8
}

public sealed record AppDataChangedEventArgs(
    Guid SourceId,
    AppDataKind Kinds,
    string? PreviousId = null,
    string? CurrentId = null);

public sealed record OrderChangedEventArgs(
    Guid SourceId,
    string? PreviousOrderId,
    string? CurrentOrderId);

public sealed class AppDataSyncService
{
    public event EventHandler<AppDataChangedEventArgs>? DataChanged;
    public event EventHandler<OrderChangedEventArgs>? OrdersChanged;

    public void Publish(AppDataChangedEventArgs args)
    {
        DataChanged?.Invoke(this, args);

        if (args.Kinds.HasFlag(AppDataKind.Orders))
        {
            OrdersChanged?.Invoke(this, new OrderChangedEventArgs(args.SourceId, args.PreviousId, args.CurrentId));
        }
    }

    public void PublishOrders(Guid sourceId, string? previousOrderId = null, string? currentOrderId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Orders, previousOrderId, currentOrderId));
    }

    public void PublishTours(Guid sourceId, string? previousTourId = null, string? currentTourId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Tours, previousTourId, currentTourId));
    }

    public void PublishVehicles(Guid sourceId, string? previousVehicleId = null, string? currentVehicleId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Vehicles, previousVehicleId, currentVehicleId));
    }

    public void PublishEmployees(Guid sourceId, string? previousEmployeeId = null, string? currentEmployeeId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Employees, previousEmployeeId, currentEmployeeId));
    }
}
