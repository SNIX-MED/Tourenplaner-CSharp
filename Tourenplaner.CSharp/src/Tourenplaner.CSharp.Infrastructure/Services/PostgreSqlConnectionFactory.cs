using Npgsql;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public sealed class PostgreSqlConnectionFactory
{
    public NpgsqlConnection CreateConnection(PostgreSqlStorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsConfigured())
        {
            throw new InvalidOperationException("PostgreSQL storage is not fully configured.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host.Trim(),
            Port = settings.Port,
            Database = settings.Database.Trim(),
            Username = settings.Username.Trim(),
            Password = settings.Password ?? string.Empty,
            Timeout = settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 10,
            SslMode = settings.UseSsl ? SslMode.Prefer : SslMode.Disable
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }
}
