using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class AppDataSyncServiceTests
{
    [Fact]
    public void Publish_DoesNotSendDuplicateMessageWhenStoragePublishesTransactionally()
    {
        var bridge = new RecordingBridge(requiresExplicitBroadcast: false);
        var service = new AppDataSyncService(bridge, Guid.NewGuid());

        service.PublishSettings(Guid.NewGuid());

        Assert.Equal(0, bridge.BroadcastCount);
        Assert.Contains("gespeichert und synchronisiert", service.GetDiagnosticsSnapshot().StatusText);
    }

    [Fact]
    public async Task Publish_ReportsExplicitBroadcastFailures()
    {
        var bridge = new RecordingBridge(requiresExplicitBroadcast: true, exception: new InvalidOperationException("Testfehler"));
        var service = new AppDataSyncService(bridge, Guid.NewGuid());

        service.PublishSettings(Guid.NewGuid());
        await Task.Delay(50);

        Assert.Equal(1, bridge.BroadcastCount);
        Assert.Equal("Testfehler", service.GetDiagnosticsSnapshot().LastErrorMessage);
    }

    private sealed class RecordingBridge(bool requiresExplicitBroadcast, Exception? exception = null) : IAppDataSyncBridge
    {
        public bool RequiresExplicitBroadcast { get; } = requiresExplicitBroadcast;
        public int BroadcastCount { get; private set; }
        public event EventHandler<AppDataChangedEventArgs>? RemoteDataChanged { add { } remove { } }
        public event EventHandler<AppDataSyncBridgeStatusChangedEventArgs>? StatusChanged { add { } remove { } }

        public Task BroadcastAsync(AppDataChangedEventArgs args, CancellationToken cancellationToken = default)
        {
            BroadcastCount++;
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }
}
