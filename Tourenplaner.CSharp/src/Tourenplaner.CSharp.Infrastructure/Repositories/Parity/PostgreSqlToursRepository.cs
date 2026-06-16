using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class PostgreSqlToursRepository : ITourRecordStore, ITourRecordMutationStore
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
        await connection.OpenAsync(cancellationToken);
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
            .ToList();

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"""DELETE FROM "{schema}"."tour_records";""";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in items)
        {
            item.ConcurrencyToken = CreateConcurrencyToken();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO "{schema}"."tour_records" (id, payload, updated_at)
                VALUES (@id, CAST(@payload AS jsonb), @updatedAt);
                """;
            command.Parameters.AddWithValue("id", item.Id.ToString());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            command.Parameters.AddWithValue("updatedAt", ParseConcurrencyToken(item.ConcurrencyToken)!);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TourRecord?> GetByIdAsync(int tourId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
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
        var expectedToken = ParseConcurrencyToken(normalized.ConcurrencyToken);
        var nextToken = DateTimeOffset.UtcNow;
        normalized.ConcurrencyToken = FormatConcurrencyToken(nextToken);
        tour.ConcurrencyToken = normalized.ConcurrencyToken;

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
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

            normalized.ConcurrencyToken = FormatConcurrencyToken(ReadTimestamp(insertedAt));
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
        var updatedAt = await updateCommand.ExecuteScalarAsync(cancellationToken);
        if (updatedAt is null)
        {
            throw new ConcurrencyConflictException("Tour", normalized.Id.ToString());
        }

        normalized.ConcurrencyToken = FormatConcurrencyToken(ReadTimestamp(updatedAt));
        tour.ConcurrencyToken = normalized.ConcurrencyToken;
    }

    public async Task DeleteAsync(int tourId, string? concurrencyToken = null, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        var expectedToken = ParseConcurrencyToken(concurrencyToken);

        await using var command = connection.CreateCommand();
        if (expectedToken is null)
        {
            command.CommandText = $"""DELETE FROM "{schema}"."tour_records" WHERE id = @id;""";
            command.Parameters.AddWithValue("id", tourId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        command.CommandText = $"""DELETE FROM "{schema}"."tour_records" WHERE id = @id AND updated_at = @expectedUpdatedAt;""";
        command.Parameters.AddWithValue("id", tourId.ToString());
        command.Parameters.AddWithValue("expectedUpdatedAt", expectedToken.Value);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows == 0)
        {
            throw new ConcurrencyConflictException("Tour", tourId.ToString());
        }
    }

    private static TourRecord DeserializeTour(string payload, DateTimeOffset updatedAt)
    {
        var item = PostgreSqlRepositorySerializer.Deserialize(payload, () => new TourRecord());
        item.ConcurrencyToken = FormatConcurrencyToken(updatedAt);
        return TourNormalizer.NormalizeTour(item);
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

    private static DateTimeOffset ReadTimestamp(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => dt.Kind == DateTimeKind.Unspecified
                ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                : new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
            _ => throw new InvalidOperationException($"Unerwarteter PostgreSQL-Zeitstempeltyp: {value.GetType().FullName}")
        };
    }
}
