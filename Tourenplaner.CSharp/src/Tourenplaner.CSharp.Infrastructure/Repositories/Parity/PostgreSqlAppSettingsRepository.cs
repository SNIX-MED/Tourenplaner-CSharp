using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class PostgreSqlAppSettingsRepository : IAppSettingsStore
{
    private const string SettingsKey = "app_settings";
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSchemaInitializer _schemaInitializer;

    public PostgreSqlAppSettingsRepository(
        PostgreSqlStorageSettings settings,
        PostgreSqlConnectionFactory? connectionFactory = null,
        PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text FROM "{schema}"."singletons" WHERE key = @key;""";
        command.Parameters.AddWithValue("key", SettingsKey);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return PostgreSqlRepositorySerializer.Deserialize(payload ?? string.Empty, () => new AppSettings());
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO "{schema}"."singletons" (key, payload, updated_at)
            VALUES (@key, CAST(@payload AS jsonb), timezone('utc', now()))
            ON CONFLICT (key)
            DO UPDATE SET payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at;
            """;
        command.Parameters.AddWithValue("key", SettingsKey);
        command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(settings ?? new AppSettings()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
