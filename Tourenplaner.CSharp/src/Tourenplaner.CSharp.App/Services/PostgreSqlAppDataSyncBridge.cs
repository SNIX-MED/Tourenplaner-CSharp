using System.Text.Json;
using Npgsql;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.Services;

public sealed class PostgreSqlAppDataSyncBridge : IAppDataSyncBridge, IDisposable
{
    private const string ChannelName = "tourenplaner_app_data_sync";

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
        try
        {
            _listenerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _shutdownCts.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        while (!_shutdownCts.IsCancellationRequested)
        {
            try
            {
                await ListenUntilDisconnectedAsync(_shutdownCts.Token);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                break;
            }
            catch
            {
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

            while (!cancellationToken.IsCancellationRequested)
            {
                await connection.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            connection.Notification -= OnNotification;
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Payload))
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<AppDataSyncPayload>(args.Payload);
        if (payload is null)
        {
            return;
        }

        RemoteDataChanged?.Invoke(
            this,
            new AppDataChangedEventArgs(
                payload.SourceId,
                payload.Kinds,
                payload.PreviousId,
                payload.CurrentId,
                payload.ClientInstanceId));
    }

    private sealed record AppDataSyncPayload(
        Guid SourceId,
        AppDataKind Kinds,
        string? PreviousId,
        string? CurrentId,
        Guid? ClientInstanceId);
}
