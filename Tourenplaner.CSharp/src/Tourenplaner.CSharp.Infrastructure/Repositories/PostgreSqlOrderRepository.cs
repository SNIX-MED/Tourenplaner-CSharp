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
            .ToList();

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"""DELETE FROM "{schema}"."orders";""";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in items)
        {
            item.ConcurrencyToken = CreateConcurrencyToken();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO "{schema}"."orders" (id, payload, updated_at)
                VALUES (@id, CAST(@payload AS jsonb), @updatedAt);
                """;
            command.Parameters.AddWithValue("id", item.Id.Trim());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            command.Parameters.AddWithValue("updatedAt", ParseConcurrencyToken(item.ConcurrencyToken)!);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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
        var expectedToken = ParseConcurrencyToken(order.ConcurrencyToken);
        var nextToken = DateTimeOffset.UtcNow;
        order.ConcurrencyToken = FormatConcurrencyToken(nextToken);

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

            order.ConcurrencyToken = FormatConcurrencyToken((DateTimeOffset)insertedAt);
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
        var updatedAt = await updateCommand.ExecuteScalarAsync(cancellationToken);
        if (updatedAt is null)
        {
            throw new ConcurrencyConflictException("Auftrag", normalizedId);
        }

        order.ConcurrencyToken = FormatConcurrencyToken((DateTimeOffset)updatedAt);
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
        var expectedToken = ParseConcurrencyToken(concurrencyToken);

        await using var command = connection.CreateCommand();
        if (expectedToken is null)
        {
            command.CommandText = $"""DELETE FROM "{schema}"."orders" WHERE id = @id;""";
            command.Parameters.AddWithValue("id", normalizedId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        command.CommandText = $"""DELETE FROM "{schema}"."orders" WHERE id = @id AND updated_at = @expectedUpdatedAt;""";
        command.Parameters.AddWithValue("id", normalizedId);
        command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows == 0)
        {
            throw new ConcurrencyConflictException("Auftrag", normalizedId);
        }
    }

    private static Order DeserializeOrder(string payload, DateTimeOffset updatedAt)
    {
        var order = PostgreSqlRepositorySerializer.Deserialize(payload, () => new Order());
        order.ConcurrencyToken = FormatConcurrencyToken(updatedAt);
        return order;
    }

    private static string CreateConcurrencyToken()
        => FormatConcurrencyToken(DateTimeOffset.UtcNow);

    private static string FormatConcurrencyToken(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O");

    private static DateTimeOffset? ParseConcurrencyToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return DateTimeOffset.Parse(token.Trim());
    }
}
