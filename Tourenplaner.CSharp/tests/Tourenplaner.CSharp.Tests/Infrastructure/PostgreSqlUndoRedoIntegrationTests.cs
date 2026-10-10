using Npgsql;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public sealed class PostgreSqlUndoRedoIntegrationTests
{
    [Fact]
    public async Task RealPostgreSql_TracksUndoesRedoesAndCleansUpInIsolatedSchema()
    {
        var connectionString = Environment.GetEnvironmentVariable("TOURENPLANER_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var schema = $"history_test_{Guid.NewGuid():N}";
        var settings = new PostgreSqlStorageSettings
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
