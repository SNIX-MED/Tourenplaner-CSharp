namespace Tourenplaner.CSharp.App.Services;

public interface IAppDataSyncBridge
{
    event EventHandler<AppDataChangedEventArgs>? RemoteDataChanged;

    Task BroadcastAsync(AppDataChangedEventArgs args, CancellationToken cancellationToken = default);
}
