using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.Services;

public sealed class StorageRepositoryFactory
{
    public async Task<StorageRepositoryBundle> CreateAsync(
        string dataRootPath,
        string settingsBootstrapPath,
        string ordersJsonPath,
        string toursJsonPath,
        string employeesJsonPath,
        string vehiclesJsonPath,
        string calendarManualEntriesJsonPath,
        CancellationToken cancellationToken = default)
    {
        var bootstrapSettingsStore = new JsonAppSettingsRepository(settingsBootstrapPath);
        var bootstrapSettings = await bootstrapSettingsStore.LoadAsync(cancellationToken);

        var usePostgreSql = bootstrapSettings.StorageMode == AppStorageMode.PostgreSql &&
                            bootstrapSettings.PostgreSqlStorage is not null &&
                            bootstrapSettings.PostgreSqlStorage.IsConfigured();

        if (!usePostgreSql)
        {
            return new StorageRepositoryBundle
            {
                StorageMode = AppStorageMode.JsonFiles,
                OrderRepository = new JsonOrderRepository(ordersJsonPath),
                SettingsRepository = new JsonSettingsRepository(settingsBootstrapPath),
                AppSettingsStore = bootstrapSettingsStore,
                TourRecordStore = new JsonToursRepository(toursJsonPath),
                EmployeeDataStore = new JsonEmployeesRepository(employeesJsonPath),
                VehicleDataStore = new JsonVehicleDataRepository(vehiclesJsonPath),
                CalendarManualEntryStore = new JsonCalendarManualEntryRepository(calendarManualEntriesJsonPath),
                PostgreSqlStorageSettings = null,
                DataRootPath = dataRootPath,
                SettingsBootstrapPath = settingsBootstrapPath,
                OrdersJsonPath = ordersJsonPath,
                ToursJsonPath = toursJsonPath,
                EmployeesJsonPath = employeesJsonPath,
                VehiclesJsonPath = vehiclesJsonPath,
                CalendarManualEntriesJsonPath = calendarManualEntriesJsonPath
            };
        }

        var pg = bootstrapSettings.PostgreSqlStorage!;
        var postgreSqlAppSettingsStore = new PostgreSqlAppSettingsRepository(pg);
        var existingPostgreSqlSettings = await postgreSqlAppSettingsStore.LoadAsync(cancellationToken);
        if (!existingPostgreSqlSettings.PostgreSqlStorage.IsConfigured() &&
            string.IsNullOrWhiteSpace(existingPostgreSqlSettings.CompanyName) &&
            string.IsNullOrWhiteSpace(existingPostgreSqlSettings.CurrentUserName))
        {
            await postgreSqlAppSettingsStore.SaveAsync(bootstrapSettings, cancellationToken);
        }

        return new StorageRepositoryBundle
        {
            StorageMode = AppStorageMode.PostgreSql,
            OrderRepository = new PostgreSqlOrderRepository(pg),
            SettingsRepository = new PostgreSqlSettingsRepository(pg),
            AppSettingsStore = postgreSqlAppSettingsStore,
            TourRecordStore = new PostgreSqlToursRepository(pg),
            EmployeeDataStore = new PostgreSqlEmployeesRepository(pg),
            VehicleDataStore = new PostgreSqlVehicleDataRepository(pg),
            CalendarManualEntryStore = new PostgreSqlCalendarManualEntryRepository(pg),
            PostgreSqlStorageSettings = pg,
            DataRootPath = dataRootPath,
            SettingsBootstrapPath = settingsBootstrapPath,
            OrdersJsonPath = ordersJsonPath,
            ToursJsonPath = toursJsonPath,
            EmployeesJsonPath = employeesJsonPath,
            VehiclesJsonPath = vehiclesJsonPath,
            CalendarManualEntriesJsonPath = calendarManualEntriesJsonPath
        };
    }
}
