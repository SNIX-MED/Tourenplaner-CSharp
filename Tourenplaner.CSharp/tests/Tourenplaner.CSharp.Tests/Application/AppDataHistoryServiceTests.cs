using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class AppDataHistoryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tourenplaner-history-{Guid.NewGuid():N}");

    [Fact]
    public async Task UndoAndRedo_RestoreTrackedFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "orders.json");
        await File.WriteAllTextAsync(path, "before");
        var sync = new AppDataSyncService();
        using var history = new AppDataHistoryService(sync, path);
        await history.InitializeAsync();

        await File.WriteAllTextAsync(path, "after");
        sync.PublishOrders(Guid.NewGuid(), "1", "1");
        await WaitUntilAsync(() => history.CanUndo);

        await history.UndoAsync();
        Assert.Equal("before", await File.ReadAllTextAsync(path));
        Assert.True(history.CanRedo);

        await history.RedoAsync();
        Assert.Equal("after", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ResetSession_ClearsBothStacksWithoutChangingData()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "orders.json");
        await File.WriteAllTextAsync(path, "before");
        var sync = new AppDataSyncService();
        using var history = new AppDataHistoryService(sync, path);
        await history.InitializeAsync();

        await File.WriteAllTextAsync(path, "after");
        sync.PublishOrders(Guid.NewGuid(), "1", "1");
        await WaitUntilAsync(() => history.CanUndo);
        await history.ResetSessionAsync("Other User");

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("after", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
        Assert.True(condition());
    }
}
