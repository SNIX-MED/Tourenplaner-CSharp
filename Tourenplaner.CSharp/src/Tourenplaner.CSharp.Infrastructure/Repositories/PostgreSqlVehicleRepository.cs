using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class PostgreSqlVehicleRepository : IVehicleRepository
{
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSchemaInitializer _schemaInitializer;

    public PostgreSqlVehicleRepository(
        PostgreSqlStorageSettings settings,
        PostgreSqlConnectionFactory? connectionFactory = null,
        PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer();
    }

    public async Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text FROM "{schema}"."vehicles" ORDER BY id;""";

        var result = new List<Vehicle>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(PostgreSqlRepositorySerializer.Deserialize(reader.GetString(0), () => new Vehicle()));
        }

        return result;
    }

    public async Task SaveAllAsync(IEnumerable<Vehicle> vehicles, CancellationToken cancellationToken = default)
    {
        var items = (vehicles ?? Enumerable.Empty<Vehicle>())
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
            deleteCommand.CommandText = $"""DELETE FROM "{schema}"."vehicles";""";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in items)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO "{schema}"."vehicles" (id, payload, updated_at)
                VALUES (@id, CAST(@payload AS jsonb), timezone('utc', now()));
                """;
            command.Parameters.AddWithValue("id", item.Id.Trim());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
