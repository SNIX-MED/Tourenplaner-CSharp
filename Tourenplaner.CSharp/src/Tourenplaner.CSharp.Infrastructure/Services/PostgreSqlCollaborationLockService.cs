using Npgsql;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public sealed class PostgreSqlCollaborationLockService : ICollaborationLockService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory = new();
    private readonly PostgreSqlSchemaInitializer _schemaInitializer = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Kind, string Id), string> _heldLocks = new();
    private readonly PeriodicTimer _heartbeatTimer = new(HeartbeatInterval);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _heartbeatTask;
    private int _disposed;

    public PostgreSqlCollaborationLockService(PostgreSqlStorageSettings settings)
    {
        _settings = settings;
        _heartbeatTask = RunHeartbeatAsync(_disposeCts.Token);
    }

    public async Task<CollaborationLockResult> TryAcquireAsync(string resourceKind, string resourceId, string userName, CancellationToken cancellationToken = default)
    {
        var kind = Normalize(resourceKind);
        var id = Normalize(resourceId);
        var user = (userName ?? string.Empty).Trim();
        if (kind.Length == 0 || id.Length == 0 || user.Length == 0)
        {
            return CollaborationLockResult.Conflict(string.Empty);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = _connectionFactory.CreateConnection(_settings);
            await connection.OpenAsync(cancellationToken);
            await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
            var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO "{schema}"."collaboration_locks"
                    (resource_kind, resource_id, user_name, session_id, acquired_at, heartbeat_at, expires_at)
                VALUES (@kind, @id, @user, @session, now(), now(), now() + @lease)
                ON CONFLICT (resource_kind, resource_id) DO UPDATE
                SET user_name = EXCLUDED.user_name,
                    session_id = EXCLUDED.session_id,
                    acquired_at = CASE WHEN "{schema}"."collaboration_locks".session_id = EXCLUDED.session_id
                                       THEN "{schema}"."collaboration_locks".acquired_at ELSE now() END,
                    heartbeat_at = now(),
                    expires_at = now() + @lease
                WHERE "{schema}"."collaboration_locks".expires_at <= now()
                   OR "{schema}"."collaboration_locks".session_id = EXCLUDED.session_id
                RETURNING user_name;
                """;
            command.Parameters.AddWithValue("kind", kind);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("user", user);
            command.Parameters.AddWithValue("session", _sessionId);
            command.Parameters.AddWithValue("lease", LeaseDuration);
            var acquiredBy = (string?)await command.ExecuteScalarAsync(cancellationToken);
            if (acquiredBy is not null)
            {
                _heldLocks[(kind, id)] = user;
                return CollaborationLockResult.Success();
            }

            await using var ownerCommand = connection.CreateCommand();
            ownerCommand.CommandText = $"""
                SELECT user_name FROM "{schema}"."collaboration_locks"
                WHERE resource_kind = @kind AND resource_id = @id AND expires_at > now();
                """;
            ownerCommand.Parameters.AddWithValue("kind", kind);
            ownerCommand.Parameters.AddWithValue("id", id);
            var owner = (string?)await ownerCommand.ExecuteScalarAsync(cancellationToken) ?? string.Empty;
            return CollaborationLockResult.Conflict(owner);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAsync(string resourceKind, string resourceId, CancellationToken cancellationToken = default)
    {
        var key = (Normalize(resourceKind), Normalize(resourceId));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _heldLocks.Remove(key);
            await DeleteLocksAsync(key, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _heldLocks.Clear();
            await DeleteLocksAsync(null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DeleteLocksAsync((string Kind, string Id)? key, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = key is null
            ? $"DELETE FROM \"{schema}\".\"collaboration_locks\" WHERE session_id = @session;"
            : $"DELETE FROM \"{schema}\".\"collaboration_locks\" WHERE session_id = @session AND resource_kind = @kind AND resource_id = @id;";
        command.Parameters.AddWithValue("session", _sessionId);
        if (key is not null)
        {
            command.Parameters.AddWithValue("kind", key.Value.Kind);
            command.Parameters.AddWithValue("id", key.Value.Id);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _heartbeatTimer.WaitForNextTickAsync(cancellationToken))
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    if (_heldLocks.Count == 0) continue;
                    await using var connection = _connectionFactory.CreateConnection(_settings);
                    await connection.OpenAsync(cancellationToken);
                    var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"""
                        UPDATE "{schema}"."collaboration_locks"
                        SET heartbeat_at = now(), expires_at = now() + @lease
                        WHERE session_id = @session;
                        """;
                    command.Parameters.AddWithValue("lease", LeaseDuration);
                    command.Parameters.AddWithValue("session", _sessionId);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCts.Cancel();
        _heartbeatTimer.Dispose();
        var heartbeatStopped = false;
        try
        {
            await _heartbeatTask.WaitAsync(TimeSpan.FromSeconds(2));
            heartbeatStopped = true;
        }
        catch { }

        using (var releaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            try
            {
                _heldLocks.Clear();
                await DeleteLocksAsync(null, releaseCts.Token);
            }
            catch { }
        }

        _disposeCts.Dispose();
        if (heartbeatStopped)
        {
            _gate.Dispose();
        }
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
