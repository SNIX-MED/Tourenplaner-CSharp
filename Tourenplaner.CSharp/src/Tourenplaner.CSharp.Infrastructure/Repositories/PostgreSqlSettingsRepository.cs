using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

// Both settings interfaces must use the same optimistic concurrency rules.
public sealed class PostgreSqlSettingsRepository : ISettingsRepository
{
    private readonly PostgreSqlAppSettingsRepository _store;

    public PostgreSqlSettingsRepository(PostgreSqlStorageSettings settings,
        PostgreSqlConnectionFactory? connectionFactory = null,
        PostgreSqlSchemaInitializer? schemaInitializer = null)
    {
        _store = new PostgreSqlAppSettingsRepository(settings, connectionFactory, schemaInitializer);
    }

    public Task<AppSettings> GetAsync(CancellationToken cancellationToken = default) =>
        _store.LoadAsync(cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(settings, cancellationToken);
}