using Npgsql;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public sealed class PostgreSqlConnectionFactory
{
    public NpgsqlConnection CreateConnection(PostgreSqlStorageSettings settings, bool dedicatedListener = false)
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
            SslMode = settings.UseSsl ? SslMode.Prefer : SslMode.Disable,
            ApplicationName = PostgreSqlClientContext.BuildApplicationName(),
            Pooling = !dedicatedListener,
            MaxPoolSize = 6,
            MinPoolSize = 0,
            ConnectionIdleLifetime = 30,
            ConnectionPruningInterval = 10
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }

    public static Task OpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default) =>
        OpenWithCapacityRetryAsync(connection.OpenAsync, cancellationToken);

    // Retry only connection admission, never commands or transactions (which may already have committed).
    public static async Task OpenWithCapacityRetryAsync(Func<CancellationToken, Task> open,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await open(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.TooManyConnections && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
