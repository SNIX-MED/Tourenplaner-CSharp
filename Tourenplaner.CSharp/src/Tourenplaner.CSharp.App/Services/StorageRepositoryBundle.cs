using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

public sealed class StorageRepositoryBundle
{
    public required AppStorageMode StorageMode { get; init; }
    public required IOrderRepository OrderRepository { get; init; }
    public required ISettingsRepository SettingsRepository { get; init; }
    public required IAppSettingsStore AppSettingsStore { get; init; }
    public required ITourRecordStore TourRecordStore { get; init; }
    public required IEmployeeDataStore EmployeeDataStore { get; init; }
    public required IVehicleDataStore VehicleDataStore { get; init; }
    public required ICalendarManualEntryStore CalendarManualEntryStore { get; init; }
    public PostgreSqlStorageSettings? PostgreSqlStorageSettings { get; init; }
    public required string DataRootPath { get; init; }
    public required string SettingsBootstrapPath { get; init; }
    public required string OrdersJsonPath { get; init; }
    public required string ToursJsonPath { get; init; }
    public required string EmployeesJsonPath { get; init; }
    public required string VehiclesJsonPath { get; init; }
    public required string CalendarManualEntriesJsonPath { get; init; }

    public IReadOnlyList<string> GetHistoryTrackedPaths()
    {
        if (StorageMode != AppStorageMode.JsonFiles)
        {
            return Array.Empty<string>();
        }

        return
        [
            OrdersJsonPath,
            ToursJsonPath,
            EmployeesJsonPath,
            VehiclesJsonPath,
            SettingsBootstrapPath
        ];
    }
}
