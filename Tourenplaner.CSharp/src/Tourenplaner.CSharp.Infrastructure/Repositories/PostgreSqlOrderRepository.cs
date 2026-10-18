using Npgsql;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class PostgreSqlOrderRepository : IOrderRepository, IOrderMutationRepository
{
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSchemaInitializer _schemaInitializer;

    public PostgreSqlOrderRepository(
        PostgreSqlStorageSettings settings,
        PostgreSqlConnectionFactory? connectionFactory = null,
        PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer();
    }

    public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text, updated_at FROM "{schema}"."orders" ORDER BY id;""";

        var result = new List<Order>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(DeserializeOrder(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return result;
    }

    public async Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default)
    {
        var items = (orders ?? Enumerable.Empty<Order>())
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .ToList();

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var savedTokens = new List<(Order Order, string Token)>();

        foreach (var item in items)
        {
            var expectedToken = RepositoryConcurrencyToken.Parse(item.ConcurrencyToken);
            var nextToken = DateTimeOffset.UtcNow;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = expectedToken is null
                ? $"""
                    INSERT INTO "{schema}"."orders" (id, payload, updated_at)
                    VALUES (@id, CAST(@payload AS jsonb), @updatedAt)
                    ON CONFLICT (id) DO NOTHING
                    RETURNING updated_at;
                    """
                : $"""
                    WITH current_row AS (
                        SELECT payload, updated_at FROM "{schema}"."orders" WHERE id = @id
                    ), updated_row AS (
                        UPDATE "{schema}"."orders"
                        SET payload = CAST(@payload AS jsonb), updated_at = @updatedAt
                        WHERE id = @id
                          AND updated_at = @expectedUpdatedAt
                          AND (payload - 'ConcurrencyToken' - 'concurrencyToken')
                              IS DISTINCT FROM (CAST(@payload AS jsonb) - 'ConcurrencyToken' - 'concurrencyToken')
                        RETURNING updated_at
                    )
                    SELECT updated_at FROM updated_row
                    UNION ALL
                    SELECT updated_at FROM current_row
                    WHERE (payload - 'ConcurrencyToken' - 'concurrencyToken')
                          IS NOT DISTINCT FROM (CAST(@payload AS jsonb) - 'ConcurrencyToken' - 'concurrencyToken')
                    LIMIT 1;
                    """;
            command.Parameters.AddWithValue("id", item.Id.Trim());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            command.Parameters.AddWithValue("updatedAt", nextToken);
            if (expectedToken.HasValue)
            {
                command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
            }

            object? savedAt;
            try { savedAt = await command.ExecuteScalarAsync(cancellationToken); }
            catch (NpgsqlException ex) when (ex.SqlState == "40001")
            {
                throw new ConcurrencyConflictException("Auftrag", item.Id.Trim());
            }
            if (savedAt is null)
            {
                throw new ConcurrencyConflictException("Auftrag", item.Id.Trim());
            }

            savedTokens.Add((item, RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(savedAt))));
        }

        await transaction.CommitAsync(cancellationToken);
        foreach (var (order, token) in savedTokens)
        {
            order.ConcurrencyToken = token;
        }
    }

    public async Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var normalizedId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return null;
        }

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text, updated_at FROM "{schema}"."orders" WHERE id = @id;""";
        command.Parameters.AddWithValue("id", normalizedId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return DeserializeOrder(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1));
    }

    public async Task UpsertAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var normalizedId = order.Id.Trim();
        var expectedToken = RepositoryConcurrencyToken.Parse(order.ConcurrencyToken);
        var nextToken = DateTimeOffset.UtcNow;

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        if (expectedToken is null)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = $"""
                INSERT INTO "{schema}"."orders" (id, payload, updated_at)
                VALUES (@id, CAST(@payload AS jsonb), @updatedAt)
                ON CONFLICT (id) DO NOTHING
                RETURNING updated_at;
                """;
            insertCommand.Parameters.AddWithValue("id", normalizedId);
            insertCommand.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(order));
            insertCommand.Parameters.AddWithValue("updatedAt", nextToken);
            var insertedAt = await insertCommand.ExecuteScalarAsync(cancellationToken);
            if (insertedAt is null)
            {
                throw new ConcurrencyConflictException("Auftrag", normalizedId);
            }

            order.ConcurrencyToken = RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(insertedAt));
            return;
        }

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = $"""
            UPDATE "{schema}"."orders"
            SET payload = CAST(@payload AS jsonb), updated_at = @updatedAt
            WHERE id = @id AND updated_at = @expectedUpdatedAt
            RETURNING updated_at;
            """;
        updateCommand.Parameters.AddWithValue("id", normalizedId);
        updateCommand.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(order));
        updateCommand.Parameters.AddWithValue("updatedAt", nextToken);
        updateCommand.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
        object? updatedAt;
        try { updatedAt = await updateCommand.ExecuteScalarAsync(cancellationToken); }
        catch (NpgsqlException ex) when (ex.SqlState == "40001")
        {
            throw new ConcurrencyConflictException("Auftrag", normalizedId);
        }
        if (updatedAt is null)
        {
            throw new ConcurrencyConflictException("Auftrag", normalizedId);
        }

        order.ConcurrencyToken = RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(updatedAt));
    }

    public async Task DeleteAsync(string orderId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
    {
        var normalizedId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        var expectedToken = RepositoryConcurrencyToken.Parse(concurrencyToken);

        await using var command = connection.CreateCommand();
        if (expectedToken is null)
        {
            command.CommandText = $"""DELETE FROM "{schema}"."orders" WHERE id = @id;""";
            command.Parameters.AddWithValue("id", normalizedId);
            try { await command.ExecuteNonQueryAsync(cancellationToken); }
            catch (NpgsqlException ex) when (ex.SqlState == "40001")
            {
                throw new ConcurrencyConflictException("Auftrag", normalizedId);
            }
            return;
        }

        command.CommandText = $"""DELETE FROM "{schema}"."orders" WHERE id = @id AND updated_at = @expectedUpdatedAt;""";
        command.Parameters.AddWithValue("id", normalizedId);
        command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
        int affectedRows;
        try { affectedRows = await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (NpgsqlException ex) when (ex.SqlState == "40001")
        {
            throw new ConcurrencyConflictException("Auftrag", normalizedId);
        }
        if (affectedRows == 0)
        {
            throw new ConcurrencyConflictException("Auftrag", normalizedId);
        }
    }

    private static Order DeserializeOrder(string payload, DateTimeOffset updatedAt)
    {
        var order = PostgreSqlRepositorySerializer.Deserialize(payload, () => new Order());
        order.ConcurrencyToken = RepositoryConcurrencyToken.Format(updatedAt);
        return order;
    }

}
