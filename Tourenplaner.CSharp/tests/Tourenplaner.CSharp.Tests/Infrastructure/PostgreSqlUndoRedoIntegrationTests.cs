using Npgsql;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

[Collection("PostgreSql client context")]
public sealed class PostgreSqlUndoRedoIntegrationTests
{
    [Fact]
    public async Task SyncTriggerUpgradeNotifiesOnlyRealChangesAndIncludesEntityKeys()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"sync_rows_{Guid.NewGuid():N}");
        PostgreSqlClientContext.Configure(Guid.NewGuid(), "Sync Row Test");
        var initialized = false;
        try
        {
            await using var writer = new PostgreSqlConnectionFactory().CreateConnection(settings);
            await writer.OpenAsync();
            var initializer = new PostgreSqlSchemaInitializer();
            await initializer.EnsureSchemaAsync(writer, settings);
            initialized = true;
            await using (var legacy = writer.CreateCommand())
            {
                legacy.CommandText = $"""
                    DROP TRIGGER orders_app_data_sync ON "{settings.Schema}".orders;
                    CREATE TRIGGER orders_app_data_sync AFTER INSERT OR UPDATE OR DELETE ON "{settings.Schema}".orders
                    FOR EACH STATEMENT EXECUTE FUNCTION "{settings.Schema}".notify_app_data_change();
                    """;
                await legacy.ExecuteNonQueryAsync();
            }
            await initializer.EnsureSchemaAsync(writer, settings);
            await initializer.EnsureSchemaAsync(writer, settings); // Repeated initialization must preserve the migration.
            await using (var shape = writer.CreateCommand())
            {
                shape.CommandText = $"""
                    SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = '{settings.Schema}' AND t.tgname LIKE '%_app_data_sync'
                    AND (t.tgtype::integer & 1) = 0;
                    """;
                Assert.Equal(0L, (long)(await shape.ExecuteScalarAsync())!);
            }
            await using var listener = new PostgreSqlConnectionFactory().CreateConnection(settings);
            await listener.OpenAsync();
            var received = new List<System.Text.Json.JsonElement>();
            var barrierSeen = false;
            listener.Notification += (_, notification) =>
            {
                using var json = System.Text.Json.JsonDocument.Parse(notification.Payload);
                if (json.RootElement.TryGetProperty("Barrier", out var barrier)) barrierSeen = true;
                else received.Add(json.RootElement.Clone());
            };
            await using (var listen = listener.CreateCommand())
            {
                listen.CommandText = "LISTEN tourenplaner_app_data_sync;";
                await listen.ExecuteNonQueryAsync();
            }
            async Task ExecuteAndDrainAsync(string sql)
            {
                received.Clear(); barrierSeen = false;
                await using var command = writer.CreateCommand();
                command.CommandText = sql + "\nSELECT pg_notify('tourenplaner_app_data_sync', '{\"Barrier\":true}');";
                await command.ExecuteNonQueryAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!barrierSeen) await listener.WaitAsync(timeout.Token);
            }
            await ExecuteAndDrainAsync($"INSERT INTO \"{settings.Schema}\".orders(id,payload) VALUES ('A','{{\"Notes\":\"one\"}}');");
            Assert.Equal("A", Assert.Single(received).GetProperty("CurrentId").GetString());
            await ExecuteAndDrainAsync($$"""
                UPDATE "{{settings.Schema}}".orders SET payload=payload WHERE id='missing';
                UPDATE "{{settings.Schema}}".orders SET payload=payload WHERE id='A';
                UPDATE "{{settings.Schema}}".orders SET payload=payload || '{"ConcurrencyToken":"new"}'::jsonb WHERE id='A';
                INSERT INTO "{{settings.Schema}}".orders(id,payload) VALUES ('A','{}') ON CONFLICT DO NOTHING;
                """);
            Assert.Empty(received);
            await ExecuteAndDrainAsync($"UPDATE \"{settings.Schema}\".orders SET payload='{{\"Notes\":\"two\"}}' WHERE id='A';");
            var update = Assert.Single(received);
            Assert.Equal("A", update.GetProperty("PreviousId").GetString());
            Assert.Equal("A", update.GetProperty("CurrentId").GetString());
            Assert.Equal(1, update.GetProperty("Kinds").GetInt32());
            await ExecuteAndDrainAsync($"DELETE FROM \"{settings.Schema}\".orders WHERE id='A';");
            var deletion = Assert.Single(received);
            Assert.Equal("A", deletion.GetProperty("PreviousId").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, deletion.GetProperty("CurrentId").ValueKind);
        }
        finally { if (initialized) await DropIsolatedSchemaAsync(settings); }
    }

    [Fact]
    public async Task ProductStatusEdit_PersistsAcrossRepositoriesAndNotifiesAnotherConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"history_test_status_{Guid.NewGuid():N}");
        PostgreSqlClientContext.Configure(Guid.NewGuid(), "Status Test");
        var initialized = false;
        try
        {
            var repository = new PostgreSqlOrderRepository(settings);
            var original = new Order { Id = "221972", Products = [new OrderProductInfo { PositionId = "P1", Name = "Produkt", Quantity = 1, DeliveryStatus = OrderProductInfo.InTransitStatus }] };
            await repository.UpsertAsync(original);
            initialized = true;
            await using var listener = new PostgreSqlConnectionFactory().CreateConnection(settings);
            await listener.OpenAsync();
            var notified = false;
            listener.Notification += (_, args) => notified |= args.Payload.Contains("\"Kinds\" : 1", StringComparison.Ordinal);
            await using (var command = listener.CreateCommand())
            {
                command.CommandText = "LISTEN tourenplaner_app_data_sync;";
                await command.ExecuteNonQueryAsync();
            }
            var candidate = OrderEditPersistenceService.CreateCandidate(original);
            candidate.Products[0].DeliveryStatus = OrderProductInfo.InStockStatus;
            await OrderEditPersistenceService.SaveAsync(repository, [original, new Order { Id = "STALE-UNRELATED" }], candidate);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!notified) await listener.WaitAsync(timeout.Token);
            var secondPc = new PostgreSqlOrderRepository(settings);
            Assert.Equal(OrderProductInfo.InStockStatus, (await secondPc.GetByIdAsync(original.Id))!.Products[0].DeliveryStatus);
            Assert.Equal(OrderProductInfo.InStockStatus, Assert.Single(await new PostgreSqlOrderRepository(settings).GetAllAsync()).Products[0].DeliveryStatus);
            Assert.Equal(OrderProductInfo.InTransitStatus, original.Products[0].DeliveryStatus);
        }
        finally
        {
            if (initialized) await DropIsolatedSchemaAsync(settings);
        }
    }

    [Fact]
    public async Task SharedTriggers_HandleIdAndKeyTablesOnTheSameConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"history_test_trigger_shapes_{Guid.NewGuid():N}");
        var initialized = false;
        try
        {
            await using var connection = new PostgreSqlConnectionFactory().CreateConnection(settings);
            await connection.OpenAsync();
            var initializer = new PostgreSqlSchemaInitializer();
            await initializer.EnsureSchemaAsync(connection, settings);
            initialized = true;
            // Existing installations also need CREATE OR REPLACE to repair their functions.
            await initializer.EnsureSchemaAsync(connection, settings);

            var notifications = new List<int>();
            connection.Notification += (_, args) =>
            {
                using var payload = System.Text.Json.JsonDocument.Parse(args.Payload);
                notifications.Add(payload.RootElement.GetProperty("Kinds").GetInt32());
            };
            await using (var listen = connection.CreateCommand())
            {
                listen.CommandText = "LISTEN tourenplaner_app_data_sync;";
                await listen.ExecuteNonQueryAsync();
            }

            (string Table, string Column, string Key)[] entities =
            [
                ("orders", "id", "ORDER-1"),
                ("tour_records", "id", "101"),
                ("employees", "id", "EMPLOYEE-1"),
                ("vehicles", "id", "VEHICLE-1"),
                ("calendar_manual_entries", "id", "CALENDAR-1"),
                ("singletons", "key", "app_settings"),
                ("singletons", "key", "vehicle_data")
            ];
            // Exercise both record shapes repeatedly, including DELETE's OLD record.
            foreach (var entity in entities.Concat(entities.Reverse()))
            {
                foreach (var sql in new[]
                {
                    $"""INSERT INTO "{settings.Schema}"."{entity.Table}" ({entity.Column}, payload) VALUES (@key, jsonb_build_object('value', 1));""",
                    $"""UPDATE "{settings.Schema}"."{entity.Table}" SET payload = jsonb_build_object('value', 2) WHERE {entity.Column} = @key;""",
                    $"""DELETE FROM "{settings.Schema}"."{entity.Table}" WHERE {entity.Column} = @key;"""
                })
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.AddWithValue("key", entity.Key);
                    Assert.Equal(1, await command.ExecuteNonQueryAsync());
                }
            }

            await using (var history = connection.CreateCommand())
            {
                history.CommandText = $"""SELECT table_name, entity_id, operation, count(*) FROM "{settings.Schema}"."change_history" GROUP BY table_name, entity_id, operation;""";
                await using var reader = await history.ExecuteReaderAsync();
                var groups = 0;
                while (await reader.ReadAsync())
                {
                    Assert.Contains(entities, entity => entity.Table == reader.GetString(0) && entity.Key == reader.GetString(1));
                    Assert.Contains(reader.GetString(2), new[] { "I", "U", "D" });
                    Assert.Equal(2L, reader.GetInt64(3));
                    groups++;
                }
                Assert.Equal(21, groups);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!new[] { 1, 2, 4, 8, 16, 32 }.All(notifications.Contains))
                await connection.WaitAsync(timeout.Token);
        }
        finally
        {
            if (initialized) await DropIsolatedSchemaAsync(settings);
        }
    }

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

    [Theory]
    [InlineData("Owner")]
    [InlineData("Ein sehr langer Benutzername")]
    [InlineData("Müller Verkauf Büro")]
    public async Task CollaborationLock_BlocksOtherDatabaseSessionButAllowsOwner(string userName)
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"history_test_locks_{Guid.NewGuid():N}");
        var initialized = false;
        PostgreSqlClientContext.Configure(Guid.NewGuid(), userName);
        await using var locks = new PostgreSqlCollaborationLockService(settings);
        try
        {
            var repository = new PostgreSqlOrderRepository(settings);
            var tourRepository = new PostgreSqlToursRepository(settings);
            var order = new Order { Id = "LOCKED-1", CustomerName = "Before" };
            await repository.UpsertAsync(order);
            initialized = true;
            var tour = new TourRecord { Id = 101, Name = "Before" };
            await tourRepository.UpsertAsync(tour);
            var acquired = await locks.TryAcquireAsync("order", order.Id, userName);
            Assert.True(acquired.Acquired);
            Assert.True((await locks.TryAcquireAsync("tour", "101", userName)).Acquired);

            await using (var ownerConnection = new PostgreSqlConnectionFactory().CreateConnection(settings))
            {
                await ownerConnection.OpenAsync();
                await using var sessionCommand = ownerConnection.CreateCommand();
                sessionCommand.CommandText = "SELECT split_part(current_setting('application_name'), '|', 4);";
                Assert.Equal(PostgreSqlClientContext.LockSessionId, await sessionCommand.ExecuteScalarAsync());
            }

            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                ApplicationName = $"GAWELA|{Guid.NewGuid():N}|Other|other123"
            };
            await using var otherConnection = new NpgsqlConnection(builder.ConnectionString);
            await otherConnection.OpenAsync();
            await using var blockedCommand = otherConnection.CreateCommand();
            blockedCommand.CommandText = $"""UPDATE "{settings.Schema}"."orders" SET updated_at = timezone('utc', now()) WHERE id = @id;""";
            blockedCommand.Parameters.AddWithValue("id", order.Id);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => blockedCommand.ExecuteNonQueryAsync());
            Assert.Equal("40001", exception.SqlState);

            order.CustomerName = "Owner update";
            tour.Name = "Owner update";
            await ((ITourOrderTransactionStore)tourRepository).SaveAsync([tour], [order]);
            Assert.Equal("Owner update", (await repository.GetByIdAsync(order.Id))?.CustomerName);
            Assert.Equal("Owner update", (await tourRepository.GetByIdAsync(tour.Id))?.Name);
            // The post-save assignment refresh must also be allowed with our own lease.
            order.Notes = "Post-save maintenance";
            await repository.UpsertAsync(order);
            Assert.Equal("Post-save maintenance", (await repository.GetByIdAsync(order.Id))?.Notes);
        }
        finally
        {
            if (initialized) await DropIsolatedSchemaAsync(settings);
        }
    }

    [Fact]
    public async Task SingletonStores_RejectStaleSettingsAndVehicleUpdates()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var settings = CreateSettings(connectionString, $"history_test_singletons_{Guid.NewGuid():N}");
        var initialized = false;
        try
        {
            var settingsStore = new PostgreSqlAppSettingsRepository(settings);
            var vehicleStore = new PostgreSqlVehicleDataRepository(settings);
            var initialSettings = await settingsStore.LoadAsync();
            initialSettings.CompanyName = "Initial";
            await settingsStore.SaveAsync(initialSettings);
            initialized = true;

            var staleSettings = await settingsStore.LoadAsync();
            var freshSettings = await settingsStore.LoadAsync();
            freshSettings.CompanyName = "Fresh";
            await settingsStore.SaveAsync(freshSettings);
            staleSettings.CompanyName = "Stale";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => settingsStore.SaveAsync(staleSettings));
            Assert.Equal("Fresh", (await settingsStore.LoadAsync()).CompanyName);

            var initialVehicles = await vehicleStore.LoadAsync();
            initialVehicles.Vehicles.Add(new Vehicle { Id = "V1", Name = "Initial" });
            await vehicleStore.SaveAsync(initialVehicles);
            var staleVehicles = await vehicleStore.LoadAsync();
            var freshVehicles = await vehicleStore.LoadAsync();
            freshVehicles.Vehicles[0].Name = "Fresh";
            await vehicleStore.SaveAsync(freshVehicles);
            staleVehicles.Vehicles[0].Name = "Stale";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => vehicleStore.SaveAsync(staleVehicles));
            Assert.Equal("Fresh", (await vehicleStore.LoadAsync()).Vehicles.Single().Name);
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
