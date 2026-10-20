using Tourenplaner.CSharp.Application.Common;
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

    public PostgreSqlAppSettingsRepository(PostgreSqlStorageSettings settings, PostgreSqlConnectionFactory? connectionFactory = null, PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text, updated_at FROM "{schema}"."singletons" WHERE key = @key;""";
        command.Parameters.AddWithValue("key", SettingsKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        string? payload = null;
        DateTimeOffset? updatedAt = null;
        if (await reader.ReadAsync(cancellationToken)) { payload = reader.GetString(0); updatedAt = reader.GetFieldValue<DateTimeOffset>(1); }
        var settings = PostgreSqlRepositorySerializer.Deserialize(payload ?? string.Empty, () => new AppSettings());
        settings.ConcurrencyToken = updatedAt.HasValue
            ? RepositoryConcurrencyToken.Format(updatedAt.Value)
            : null;
        await reader.DisposeAsync();
        if (settings.ApplyStayMinutesDefaultsMigration()) await SaveAsync(settings, cancellationToken);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await PostgreSqlConnectionFactory.OpenAsync(connection, cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        var expected = RepositoryConcurrencyToken.Parse(settings.ConcurrencyToken);
        await using var command = connection.CreateCommand();
        command.CommandText = expected is null
            ? $"""INSERT INTO "{schema}"."singletons" (key, payload, updated_at) VALUES (@key, CAST(@payload AS jsonb), timezone('utc', now())) ON CONFLICT (key) DO NOTHING RETURNING updated_at;"""
            : $"""UPDATE "{schema}"."singletons" SET payload = CAST(@payload AS jsonb), updated_at = timezone('utc', now()) WHERE key = @key AND updated_at = @expectedUpdatedAt RETURNING updated_at;""";
        command.Parameters.AddWithValue("key", SettingsKey);
        command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(settings));
        if (expected.HasValue) command.Parameters.AddWithValue("expectedUpdatedAt", expected.Value);
        var savedAt = await command.ExecuteScalarAsync(cancellationToken);
        if (savedAt is null) throw new ConcurrencyConflictException("Einstellungen", SettingsKey);
        settings.ConcurrencyToken = RepositoryConcurrencyToken.Format(
            RepositoryConcurrencyToken.ReadTimestamp(savedAt));
    }
}
