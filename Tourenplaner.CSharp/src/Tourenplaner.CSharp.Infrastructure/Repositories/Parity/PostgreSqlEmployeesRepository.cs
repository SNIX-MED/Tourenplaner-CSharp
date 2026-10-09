using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class PostgreSqlEmployeesRepository : IEmployeeDataStore, IEmployeeMutationStore
{
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSchemaInitializer _schemaInitializer;

    public PostgreSqlEmployeesRepository(
        PostgreSqlStorageSettings settings,
        PostgreSqlConnectionFactory? connectionFactory = null,
        PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer();
    }

    public async Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text FROM "{schema}"."employees" ORDER BY id;""";

        var normalized = new List<Employee>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entry = EmployeeNormalizer.Normalize(PostgreSqlRepositorySerializer.Deserialize(reader.GetString(0), () => new Employee()));
            if (string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                continue;
            }

            if (!seen.Add(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString();
                seen.Add(entry.Id);
            }

            normalized.Add(entry);
        }

        return normalized
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default)
    {
        var items = (employees ?? Array.Empty<Employee>())
            .Where(x => x is not null)
            .Select(EmployeeNormalizer.Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        foreach (var item in items)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO "{schema}"."employees" (id, payload, updated_at)
                VALUES (@id, CAST(@payload AS jsonb), timezone('utc', now()))
                ON CONFLICT (id) DO UPDATE
                SET payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at;
                """;
            command.Parameters.AddWithValue("id", item.Id.Trim());
            command.Parameters.AddWithValue("payload", PostgreSqlRepositorySerializer.Serialize(item));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

    }

    public async Task UpsertAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        await SaveAsync([employee], cancellationToken);
    }

    public async Task DeleteAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        var normalizedId = (employeeId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId)) return;
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DELETE FROM "{schema}"."employees" WHERE id = @id;""";
        command.Parameters.AddWithValue("id", normalizedId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
