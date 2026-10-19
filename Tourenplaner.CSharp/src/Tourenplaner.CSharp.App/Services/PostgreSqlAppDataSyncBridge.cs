using System.Text.Json;
using Npgsql;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.Services;

public sealed class PostgreSqlAppDataSyncBridge : IAppDataSyncBridge, IDisposable, IAsyncDisposable
{
    private const string ChannelName = "tourenplaner_app_data_sync";
    private static readonly JsonSerializerOptions NotificationJsonOptions = new()
    {
        Converters = { new NotificationGuidConverter() }
    };

    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _listenerTask;

    public PostgreSqlAppDataSyncBridge(
        PostgreSqlStorageSettings settings,
        PostgreSqlConnectionFactory? connectionFactory = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
        _listenerTask = Task.Run(ListenLoopAsync);
    }

    public event EventHandler<AppDataChangedEventArgs>? RemoteDataChanged;
    public event EventHandler<AppDataSyncBridgeStatusChangedEventArgs>? StatusChanged;

    public bool RequiresExplicitBroadcast => false;

    public async Task BroadcastAsync(AppDataChangedEventArgs args, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new AppDataSyncPayload(
            args.SourceId,
            args.Kinds,
            args.PreviousId,
            args.CurrentId,
            args.ClientInstanceId));

        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_notify(@channel, @payload);";
        command.Parameters.AddWithValue("channel", ChannelName);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _listenerTask.Forget();
        _shutdownCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        try
        {
            await _listenerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _shutdownCts.Dispose();
        }
    }

    private async Task ListenLoopAsync()
    {
        while (!_shutdownCts.IsCancellationRequested)
        {
            try
            {
                RaiseStatus(isConnected: false, "PostgreSQL-Sync verbindet...", null);
                await ListenUntilDisconnectedAsync(_shutdownCts.Token);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                RaiseStatus(isConnected: false, "PostgreSQL-Sync unterbrochen.", ex.Message);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), _shutdownCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ListenUntilDisconnectedAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        connection.Notification += OnNotification;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""LISTEN "{ChannelName}";""";
            await command.ExecuteNonQueryAsync(cancellationToken);

            RaiseStatus(isConnected: true, "PostgreSQL-Sync verbunden.", null);
            // LISTEN only delivers subsequent commits. Reload after every connection,
            // including reconnects, to catch changes committed while this PC was offline.
            RemoteDataChanged?.Invoke(this, new AppDataChangedEventArgs(Guid.Empty,
                AppDataKind.Orders | AppDataKind.Tours | AppDataKind.Vehicles |
                AppDataKind.Employees | AppDataKind.Settings | AppDataKind.CalendarEntries,
                ClientInstanceId: Guid.Empty));

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await connection.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    await using var heartbeat = connection.CreateCommand();
                    heartbeat.CommandText = "SELECT 1;";
                    heartbeat.CommandTimeout = 10;
                    await heartbeat.ExecuteScalarAsync(cancellationToken);
                }
            }
        }
        finally
        {
            connection.Notification -= OnNotification;
            if (!cancellationToken.IsCancellationRequested)
            {
                RaiseStatus(isConnected: false, "PostgreSQL-Sync getrennt.", null);
            }
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs args)
    {
        var change = DecodeNotification(args.Payload);
        if (change is not null) RemoteDataChanged?.Invoke(this, change);
    }

    internal static AppDataChangedEventArgs? DecodeNotification(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<AppDataSyncPayload>(json, NotificationJsonOptions);
            return payload is null ? null : new(payload.SourceId, payload.Kinds, payload.PreviousId, payload.CurrentId, payload.ClientInstanceId);
        }
        catch (JsonException)
        {
            // An incompatible notification must neither break LISTEN nor silently lose a change.
            return new(Guid.Empty, AppDataKind.Orders | AppDataKind.Tours | AppDataKind.Vehicles |
                AppDataKind.Employees | AppDataKind.Settings | AppDataKind.CalendarEntries, ClientInstanceId: Guid.Empty);
        }
    }

    private sealed class NotificationGuidConverter : System.Text.Json.Serialization.JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Guid.TryParse(reader.GetString(), out var id)) return id;
            throw new JsonException("Ungültige Arbeitsplatz-ID in Synchronisationsmeldung.");
        }

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    private void RaiseStatus(bool isConnected, string statusText, string? errorMessage)
    {
        StatusChanged?.Invoke(
            this,
            new AppDataSyncBridgeStatusChangedEventArgs(
                isConnected,
                statusText,
                errorMessage,
                DateTime.UtcNow));
    }

    private sealed record AppDataSyncPayload(
        Guid SourceId,
        AppDataKind Kinds,
        string? PreviousId,
        string? CurrentId,
        Guid? ClientInstanceId);
}
