using System.Text.Json;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class PostgreSqlSyncNotificationTests
{
    [Theory]
    [InlineData("N")]
    [InlineData("D")]
    public void TriggerNotification_ReachesOtherClientsAndSuppressesOwnEcho(string format)
    {
        var sender = Guid.NewGuid();
        var receiverBridge = new Bridge();
        var receiver = new AppDataSyncService(receiverBridge, Guid.NewGuid());
        var ownBridge = new Bridge();
        var own = new AppDataSyncService(ownBridge, sender);
        var received = new List<AppDataChangedEventArgs>();
        var echoes = 0;
        receiver.DataChanged += (_, e) => received.Add(e);
        own.DataChanged += (_, _) => echoes++;
        var json = JsonSerializer.Serialize(new { SourceId = sender.ToString(format), Kinds = 2, PreviousId = (string?)null, CurrentId = (string?)null, ClientInstanceId = sender.ToString(format) });
        receiverBridge.Deliver(json);
        ownBridge.Deliver(json);
        Assert.Equal(sender, Assert.Single(received).ClientInstanceId);
        Assert.Equal(AppDataKind.Tours, received[0].Kinds);
        Assert.Equal(0, echoes);
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"SourceId\":\"invalid\",\"Kinds\":2}")]
    public void InvalidMessageRequestsReloadAndDoesNotBreakNextMessage(string invalid)
    {
        Assert.True(PostgreSqlAppDataSyncBridge.DecodeNotification(invalid)!.Kinds.HasFlag(AppDataKind.Tours));
        var id = Guid.NewGuid();
        var next = PostgreSqlAppDataSyncBridge.DecodeNotification(JsonSerializer.Serialize(new { SourceId = id.ToString("N"), Kinds = 2 }));
        Assert.Equal(id, next!.SourceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public void EmptyPayloadIsIgnored(string json) => Assert.Null(PostgreSqlAppDataSyncBridge.DecodeNotification(json));

    private sealed class Bridge : IAppDataSyncBridge
    {
        public bool RequiresExplicitBroadcast => false;
        public event EventHandler<AppDataChangedEventArgs>? RemoteDataChanged;
        public event EventHandler<AppDataSyncBridgeStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public Task BroadcastAsync(AppDataChangedEventArgs args, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Deliver(string json)
        {
            var decoded = PostgreSqlAppDataSyncBridge.DecodeNotification(json);
            if (decoded is not null) RemoteDataChanged?.Invoke(this, decoded);
        }
    }
}
