using Npgsql;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public sealed class PostgreSqlUndoRedoIntegrationTests
{
    [Fact]
    public async Task SaveAll_IgnoresUnchangedStaleOrderAndConsolidatesDuplicateIds()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"history_test_orders_{Guid.NewGuid():N}");
        var initialized = false;
        try
        {
            var repository = new PostgreSqlOrderRepository(settings);
            var unchanged = new Order { Id = "UNCHANGED", CustomerName = "Original" };
            var changed = new Order { Id = "CHANGED", CustomerName = "Before" };
            await repository.SaveAllAsync([unchanged, changed]);
            initialized = true;
            var snapshot = (await repository.GetAllAsync()).ToList();
            var external = await repository.GetByIdAsync(unchanged.Id);
            Assert.NotNull(external);
            external.Notes = "External change";
            await repository.UpsertAsync(external);

            var staleUnchanged = snapshot.Single(x => x.Id == unchanged.Id);
            staleUnchanged.Notes = "External change";
            var locallyChanged = snapshot.Single(x => x.Id == changed.Id);
            locallyChanged.CustomerName = "After";
            snapshot.Add(locallyChanged);
            await repository.SaveAllAsync(snapshot);

            Assert.Equal("External change", (await repository.GetByIdAsync(unchanged.Id))?.Notes);
            Assert.Equal("After", (await repository.GetByIdAsync(changed.Id))?.CustomerName);
        }
        finally
        {
            if (initialized) await DropIsolatedSchemaAsync(settings);
        }
    }

    [Fact]
    public async Task TourAndOrderTransaction_RollsBackConflictsAndDeletesBothEntities()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"history_test_transaction_{Guid.NewGuid():N}");
        var initialized = false;
        try
        {
            var orders = new PostgreSqlOrderRepository(settings);
            var tours = new PostgreSqlToursRepository(settings);
            var order = new Order { Id = "ORDER-1", CustomerName = "Original" };
            var tour = new TourRecord { Id = 101, Name = "Original tour" };
            await orders.UpsertAsync(order);
            initialized = true;
            await tours.UpsertAsync(tour);

            var staleOrder = await orders.GetByIdAsync(order.Id);
            var externalOrder = await orders.GetByIdAsync(order.Id);
            var loadedTour = await tours.GetByIdAsync(tour.Id);
            Assert.NotNull(staleOrder);
            Assert.NotNull(externalOrder);
            Assert.NotNull(loadedTour);
            externalOrder.CustomerName = "External";
            await orders.UpsertAsync(externalOrder);
            staleOrder.CustomerName = "Local conflict";
            loadedTour.Name = "Must roll back";

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                ((ITourOrderTransactionStore)tours).SaveAsync([loadedTour], [staleOrder]));
            Assert.Equal("Original tour", (await tours.GetByIdAsync(tour.Id))?.Name);
            Assert.Equal("External", (await orders.GetByIdAsync(order.Id))?.CustomerName);

            var persistedTour = await tours.GetByIdAsync(tour.Id);
            var persistedOrder = await orders.GetByIdAsync(order.Id);
            Assert.NotNull(persistedTour);
            Assert.NotNull(persistedOrder);
            await ((ITourOrderTransactionStore)tours).SaveAsync([], [], [persistedTour], [persistedOrder]);
            Assert.Null(await tours.GetByIdAsync(tour.Id));
            Assert.Null(await orders.GetByIdAsync(order.Id));
        }
        finally
        {
            if (initialized) await DropIsolatedSchemaAsync(settings);
        }
    }

    [Fact]
    public async Task TourBulkSaveHandlesStaleUnchangedRowsAndFailedUpsertsPreserveTokens()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"history_test_tours_{Guid.NewGuid():N}");
        var initialized = false;
        try
        {
            var tours = new PostgreSqlToursRepository(settings);
            var orders = new PostgreSqlOrderRepository(settings);
            await tours.SaveAsync([new TourRecord { Id = 201, Name = "Unchanged" }, new TourRecord { Id = 202, Name = "Before" }]);
            initialized = true;
            var snapshot = (await tours.LoadAsync()).ToList();
            var external = await tours.GetByIdAsync(201);
            Assert.NotNull(external);
            external.IsArchived = true;
            await tours.UpsertAsync(external);
            snapshot.Single(x => x.Id == 201).IsArchived = true;
            var changed = snapshot.Single(x => x.Id == 202);
            changed.Name = "After";
            snapshot.Add(changed);
            await tours.SaveAsync(snapshot);
            Assert.True((await tours.GetByIdAsync(201))?.IsArchived);
            Assert.Equal("After", (await tours.GetByIdAsync(202))?.Name);

            var staleOrder = new Order { Id = "TOKEN", CustomerName = "One" };
            await orders.UpsertAsync(staleOrder);
            var oldOrderToken = staleOrder.ConcurrencyToken;
            var freshOrder = await orders.GetByIdAsync(staleOrder.Id);
            Assert.NotNull(freshOrder);
            freshOrder.CustomerName = "Two";
            await orders.UpsertAsync(freshOrder);
            staleOrder.CustomerName = "Conflict";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => orders.UpsertAsync(staleOrder));
            Assert.Equal(oldOrderToken, staleOrder.ConcurrencyToken);

            var staleTour = await tours.GetByIdAsync(202);
            var freshTour = await tours.GetByIdAsync(202);
            Assert.NotNull(staleTour);
            Assert.NotNull(freshTour);
            var oldTourToken = staleTour.ConcurrencyToken;
            freshTour.Name = "Newest";
            await tours.UpsertAsync(freshTour);
            staleTour.Name = "Conflict";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => tours.UpsertAsync(staleTour));
            Assert.Equal(oldTourToken, staleTour.ConcurrencyToken);
        }
        finally
        {
            if (initialized) await DropIsolatedSchemaAsync(settings);
        }
    }

    [Fact]
    public async Task RealPostgreSql_TracksUndoesRedoesAndCleansUpInIsolatedSchema()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var schema = $"history_test_{Guid.NewGuid():N}";
        var settings = CreateSettings(connectionString, schema);

        var sync = new AppDataSyncService(clientInstanceId: Guid.NewGuid());
        using var history = new PostgreSqlAppDataHistoryService(sync, settings, "Integration Test");
        var schemaInitialized = false;
        try
        {
            await history.InitializeAsync();
            schemaInitialized = true;
            var repository = new PostgreSqlOrderRepository(settings);
            var order = new Order { Id = "HISTORY-TEST", CustomerName = "Integration Test" };

            await repository.UpsertAsync(order);
            sync.PublishOrders(Guid.NewGuid(), null, order.Id);
            await WaitUntilAsync(() => history.CanUndo);

            var visibleEntries = await history.LoadRecentEntriesAsync();
            Assert.Contains(visibleEntries, item => item.EntityId == order.Id && item.Action == "Erstellt");

            await history.UndoAsync();
            Assert.Null(await repository.GetByIdAsync(order.Id));

            await history.RedoAsync();
            Assert.NotNull(await repository.GetByIdAsync(order.Id));

            await AgeJournalAsync(settings, 90);
            Assert.True(await history.CleanupExpiredEntriesAsync(60) > 0);
        }
        finally
        {
            if (schemaInitialized) await DropIsolatedSchemaAsync(settings);
        }
    }

    private static PostgreSqlStorageSettings CreateSettings(string connectionString, string schema)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return new PostgreSqlStorageSettings
        {
            Host = builder.Host ?? string.Empty,
            Port = builder.Port,
            Database = builder.Database ?? string.Empty,
            Username = builder.Username ?? string.Empty,
            Password = builder.Password ?? string.Empty,
            UseSsl = builder.SslMode != SslMode.Disable,
            TimeoutSeconds = builder.Timeout,
            Schema = schema,
            ChangeHistoryRetentionDays = 60
        };
    }

    private static async Task AgeJournalAsync(PostgreSqlStorageSettings settings, int days)
    {
        await using var connection = new PostgreSqlConnectionFactory().CreateConnection(settings);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""UPDATE "{settings.Schema}"."change_history" SET changed_at = timezone('utc', now()) - (@days * interval '1 day');""";
        command.Parameters.AddWithValue("days", days);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropIsolatedSchemaAsync(PostgreSqlStorageSettings settings)
    {
        Assert.StartsWith("history_test_", settings.Schema, StringComparison.Ordinal);
        await using var connection = new PostgreSqlConnectionFactory().CreateConnection(settings);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DROP SCHEMA IF EXISTS "{settings.Schema}" CASCADE;""";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(condition());
    }
}
