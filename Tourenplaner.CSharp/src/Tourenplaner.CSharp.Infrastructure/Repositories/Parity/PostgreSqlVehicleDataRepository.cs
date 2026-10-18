using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class PostgreSqlVehicleDataRepository : IVehicleDataStore
{
    private const string VehicleDataKey = "vehicle_data";
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSchemaInitializer _schemaInitializer;

    public PostgreSqlVehicleDataRepository(PostgreSqlStorageSettings settings, PostgreSqlConnectionFactory? connectionFactory = null, PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer();
    }

    public async Task<VehicleDataRecord> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text, updated_at FROM "{schema}"."singletons" WHERE key = @key;""";
        command.Parameters.AddWithValue("key", VehicleDataKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new VehicleDataRecord();
        var result = VehicleNormalizer.NormalizePayload(PostgreSqlRepositorySerializer.Deserialize(reader.GetString(0), () => new VehicleDataRecord()));
        result.ConcurrencyToken = RepositoryConcurrencyToken.Format(reader.GetFieldValue<DateTimeOffset>(1));
        return result;
    }

    public async Task SaveAsync(VehicleDataRecord payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var normalized = VehicleNormalizer.NormalizePayload(payload);
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        var expected = RepositoryConcurrencyToken.Parse(normalized.ConcurrencyToken);
        await using var command = connection.CreateCommand();
        command.CommandText = expected is null
            ? $"""INSERT INTO "{schema}"."singletons" (key, payload, updated_at) VALUES (@key, CAST(@payload AS jsonb), timezone('utc', now())) ON CONFLICT (key) DO NOTHING RETURNING updated_at;"""
            : $"""UPDATE "{schema}"."singletons" SET payload = CAST(@payload AS jsonb), updated_at = timezone('utc', now()) WHERE key = @key AND updated_at = @expectedUpdatedAt RETURNING updated_at;""";
        command.Parameters.AddWithValue("key", VehicleDataKey);
        command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(normalized));
        if (expected.HasValue) command.Parameters.AddWithValue("expectedUpdatedAt", expected.Value);
        var savedAt = await command.ExecuteScalarAsync(cancellationToken);
        if (savedAt is null) throw new ConcurrencyConflictException("Fahrzeugdaten", VehicleDataKey);
        payload.ConcurrencyToken = RepositoryConcurrencyToken.Format(
            RepositoryConcurrencyToken.ReadTimestamp(savedAt));
    }
}
