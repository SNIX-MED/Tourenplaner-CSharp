using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class PostgreSqlToursRepository : ITourRecordStore, ITourRecordMutationStore, ITourOrderTransactionStore
{
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSchemaInitializer _schemaInitializer;

    public PostgreSqlToursRepository(
        PostgreSqlStorageSettings settings,
        PostgreSqlConnectionFactory? connectionFactory = null,
        PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer();
    }

    public async Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text, updated_at FROM "{schema}"."tour_records" ORDER BY id;""";

        var result = new List<TourRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(DeserializeTour(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return result;
    }

    public async Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default)
    {
        var items = (tours ?? Array.Empty<TourRecord>())
            .Where(x => x is not null)
            .Select(TourNormalizer.NormalizeTour)
            .GroupBy(x => x.Id)
            .Select(x => x.Last())
            .ToList();

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var savedTokens = new List<(TourRecord Tour, string Token)>();

        foreach (var item in items)
        {
            var expectedToken = RepositoryConcurrencyToken.Parse(item.ConcurrencyToken);
            var nextToken = DateTimeOffset.UtcNow;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = expectedToken is null
                ? $"""
                    INSERT INTO "{schema}"."tour_records" (id, payload, updated_at)
                    VALUES (@id, CAST(@payload AS jsonb), @updatedAt)
                    ON CONFLICT (id) DO NOTHING
                    RETURNING updated_at;
                    """
                : $"""
                    WITH current_row AS (
                        SELECT payload, updated_at FROM "{schema}"."tour_records" WHERE id = @id
                    ), updated_row AS (
                        UPDATE "{schema}"."tour_records"
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
            command.Parameters.AddWithValue("id", item.Id.ToString());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            command.Parameters.AddWithValue("updatedAt", nextToken);
            if (expectedToken.HasValue)
            {
                command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
            }

            object? savedAt;
            try { savedAt = await command.ExecuteScalarAsync(cancellationToken); }
            catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
            {
                throw new ConcurrencyConflictException("Tour", item.Id.ToString());
            }
            if (savedAt is null)
            {
                throw new ConcurrencyConflictException("Tour", item.Id.ToString());
            }

            savedTokens.Add((item, RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(savedAt))));
        }

        await transaction.CommitAsync(cancellationToken);
        foreach (var (tour, token) in savedTokens)
        {
            tour.ConcurrencyToken = token;
        }
    }

    public async Task SaveAsync(
        IEnumerable<TourRecord> tours,
        IEnumerable<Order> orders,
        IEnumerable<TourRecord>? toursToDelete = null,
        IEnumerable<Order>? ordersToDelete = null,
        CancellationToken cancellationToken = default)
    {
        var tourItems = (tours ?? Array.Empty<TourRecord>()).Where(x => x is not null)
            .Select(TourNormalizer.NormalizeTour).GroupBy(x => x.Id).Select(x => x.Last()).ToList();
        var orderItems = (orders ?? Array.Empty<Order>()).Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToList();
        var deletedTours = (toursToDelete ?? Array.Empty<TourRecord>()).Where(x => x is not null)
            .GroupBy(x => x.Id).Select(x => x.Last()).ToList();
        var deletedOrders = (ordersToDelete ?? Array.Empty<Order>()).Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToList();

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var savedTourTokens = new List<(TourRecord Tour, string Token)>();
        var savedOrderTokens = new List<(Order Order, string Token)>();

        foreach (var item in tourItems)
        {
            var expectedToken = RepositoryConcurrencyToken.Parse(item.ConcurrencyToken);
            var nextToken = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = expectedToken is null
                ? $"""
                    INSERT INTO "{schema}"."tour_records" (id, payload, updated_at)
                    VALUES (@id, CAST(@payload AS jsonb), @updatedAt)
                    ON CONFLICT (id) DO NOTHING RETURNING updated_at;
                    """
                : BuildConcurrencySafeUpdateSql(schema, "tour_records");
            command.Parameters.AddWithValue("id", item.Id.ToString());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            command.Parameters.AddWithValue("updatedAt", nextToken);
            if (expectedToken.HasValue) command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
            object? savedAt;
            try { savedAt = await command.ExecuteScalarAsync(cancellationToken); }
            catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
            {
                throw new ConcurrencyConflictException("Tour", item.Id.ToString());
            }
            if (savedAt is null) throw new ConcurrencyConflictException("Tour", item.Id.ToString());
            savedTourTokens.Add((item, RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(savedAt))));
        }

        foreach (var item in orderItems)
        {
            var expectedToken = RepositoryConcurrencyToken.Parse(item.ConcurrencyToken);
            var nextToken = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = expectedToken is null
                ? $"""
                    INSERT INTO "{schema}"."orders" (id, payload, updated_at)
                    VALUES (@id, CAST(@payload AS jsonb), @updatedAt)
                    ON CONFLICT (id) DO NOTHING RETURNING updated_at;
                    """
                : BuildConcurrencySafeUpdateSql(schema, "orders");
            command.Parameters.AddWithValue("id", item.Id.Trim());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            command.Parameters.AddWithValue("updatedAt", nextToken);
            if (expectedToken.HasValue) command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
            object? savedAt;
            try { savedAt = await command.ExecuteScalarAsync(cancellationToken); }
            catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
            {
                throw new ConcurrencyConflictException("Auftrag", item.Id.Trim());
            }
            if (savedAt is null) throw new ConcurrencyConflictException("Auftrag", item.Id.Trim());
            savedOrderTokens.Add((item, RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(savedAt))));
        }

        foreach (var item in deletedTours)
        {
            await DeleteInTransactionAsync(connection, transaction, schema, "tour_records", item.Id.ToString(), item.ConcurrencyToken, "Tour", cancellationToken);
        }
        foreach (var item in deletedOrders)
        {
            await DeleteInTransactionAsync(connection, transaction, schema, "orders", item.Id.Trim(), item.ConcurrencyToken, "Auftrag", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        foreach (var (tour, token) in savedTourTokens) tour.ConcurrencyToken = token;
        foreach (var (order, token) in savedOrderTokens) order.ConcurrencyToken = token;
    }

    public async Task<TourRecord?> GetByIdAsync(int tourId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text, updated_at FROM "{schema}"."tour_records" WHERE id = @id;""";
        command.Parameters.AddWithValue("id", tourId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return DeserializeTour(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1));
    }

    public async Task UpsertAsync(TourRecord tour, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tour);

        var normalized = TourNormalizer.NormalizeTour(tour);
        var expectedToken = RepositoryConcurrencyToken.Parse(normalized.ConcurrencyToken);
        var nextToken = DateTimeOffset.UtcNow;

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        if (expectedToken is null)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = $"""
                INSERT INTO "{schema}"."tour_records" (id, payload, updated_at)
                VALUES (@id, CAST(@payload AS jsonb), @updatedAt)
                ON CONFLICT (id) DO NOTHING
                RETURNING updated_at;
                """;
            insertCommand.Parameters.AddWithValue("id", normalized.Id.ToString());
            insertCommand.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(normalized));
            insertCommand.Parameters.AddWithValue("updatedAt", nextToken);
            var insertedAt = await insertCommand.ExecuteScalarAsync(cancellationToken);
            if (insertedAt is null)
            {
                throw new ConcurrencyConflictException("Tour", normalized.Id.ToString());
            }

            normalized.ConcurrencyToken = RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(insertedAt));
            tour.ConcurrencyToken = normalized.ConcurrencyToken;
            return;
        }

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = $"""
            UPDATE "{schema}"."tour_records"
            SET payload = CAST(@payload AS jsonb), updated_at = @updatedAt
            WHERE id = @id AND updated_at = @expectedUpdatedAt
            RETURNING updated_at;
            """;
        updateCommand.Parameters.AddWithValue("id", normalized.Id.ToString());
        updateCommand.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(normalized));
        updateCommand.Parameters.AddWithValue("updatedAt", nextToken);
        updateCommand.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
        object? updatedAt;
        try { updatedAt = await updateCommand.ExecuteScalarAsync(cancellationToken); }
        catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
        {
            throw new ConcurrencyConflictException("Tour", normalized.Id.ToString());
        }
        if (updatedAt is null)
        {
            throw new ConcurrencyConflictException("Tour", normalized.Id.ToString());
        }

        normalized.ConcurrencyToken = RepositoryConcurrencyToken.Format(RepositoryConcurrencyToken.ReadTimestamp(updatedAt));
        tour.ConcurrencyToken = normalized.ConcurrencyToken;
    }

    public async Task DeleteAsync(int tourId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        var expectedToken = RepositoryConcurrencyToken.Parse(concurrencyToken);

        await using var command = connection.CreateCommand();
        if (expectedToken is null)
        {
            command.CommandText = $"""DELETE FROM "{schema}"."tour_records" WHERE id = @id;""";
            command.Parameters.AddWithValue("id", tourId.ToString());
            try { await command.ExecuteNonQueryAsync(cancellationToken); }
            catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
            {
                throw new ConcurrencyConflictException("Tour", tourId.ToString());
            }
            return;
        }

        command.CommandText = $"""DELETE FROM "{schema}"."tour_records" WHERE id = @id AND updated_at = @expectedUpdatedAt;""";
        command.Parameters.AddWithValue("id", tourId.ToString());
        command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
        int affectedRows;
        try { affectedRows = await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
        {
            throw new ConcurrencyConflictException("Tour", tourId.ToString());
        }
        if (affectedRows == 0)
        {
            throw new ConcurrencyConflictException("Tour", tourId.ToString());
        }
    }

    private static TourRecord DeserializeTour(string payload, DateTimeOffset updatedAt)
    {
        var item = PostgreSqlRepositorySerializer.Deserialize(payload, () => new TourRecord());
        item.ConcurrencyToken = RepositoryConcurrencyToken.Format(updatedAt);
        return TourNormalizer.NormalizeTour(item);
    }

    private static string BuildConcurrencySafeUpdateSql(string schema, string table)
        => $"""
            WITH current_row AS (
                SELECT payload, updated_at FROM "{schema}"."{table}" WHERE id = @id
            ), updated_row AS (
                UPDATE "{schema}"."{table}"
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

    private static async Task DeleteInTransactionAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        string schema,
        string table,
        string id,
        string? concurrencyToken,
        string entityName,
        CancellationToken cancellationToken)
    {
        var expectedToken = RepositoryConcurrencyToken.Parse(concurrencyToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = expectedToken is null
            ? $"""DELETE FROM "{schema}"."{table}" WHERE id = @id;"""
            : $"""DELETE FROM "{schema}"."{table}" WHERE id = @id AND updated_at = @expectedUpdatedAt;""";
        command.Parameters.AddWithValue("id", id);
        if (expectedToken.HasValue) command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
        int affectedRows;
        try { affectedRows = await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
        {
            throw new ConcurrencyConflictException(entityName, id);
        }
        if (expectedToken.HasValue && affectedRows == 0) throw new ConcurrencyConflictException(entityName, id);
    }

}
