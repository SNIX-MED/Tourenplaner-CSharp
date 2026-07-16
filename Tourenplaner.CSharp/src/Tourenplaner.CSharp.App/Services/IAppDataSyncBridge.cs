namespace Tourenplaner.CSharp.App.Services;

public interface IAppDataSyncBridge
{
    event EventHandler<AppDataChangedEventArgs>? RemoteDataChanged;
    event EventHandler<AppDataSyncBridgeStatusChangedEventArgs>? StatusChanged;

    Task BroadcastAsync(AppDataChangedEventArgs args, CancellationToken cancellationToken = default);
}

public sealed record AppDataSyncBridgeStatusChangedEventArgs(
    bool IsConnected,
    string StatusText,
    string? ErrorMessage,
    DateTime OccurredAtUtc);
