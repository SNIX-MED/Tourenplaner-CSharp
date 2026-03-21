using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class AppSnapshotService
{
    private readonly IOrderRepository _orders;
    private readonly ITourRepository _tours;
    private readonly IEmployeeRepository _employees;
    private readonly IVehicleRepository _vehicles;

    public AppSnapshotService(
        IOrderRepository orders,
        ITourRepository tours,
        IEmployeeRepository employees,
        IVehicleRepository vehicles)
    {
        _orders = orders;
        _tours = tours;
        _employees = employees;
        _vehicles = vehicles;
    }

    public async Task<AppSnapshot> CreateAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _orders.GetAllAsync(cancellationToken);
        var tours = await _tours.GetAllAsync(cancellationToken);
        var employees = await _employees.GetAllAsync(cancellationToken);
        var vehicles = await _vehicles.GetAllAsync(cancellationToken);

        var nonMapOrders = orders.Count(x => x.Type == OrderType.NonMap);

        return new AppSnapshot(
            DateTimeOffset.UtcNow,
            OrderCount: orders.Count,
            NonMapOrderCount: nonMapOrders,
            TourCount: tours.Count,
            EmployeeCount: employees.Count,
            VehicleCount: vehicles.Count);
    }
}
