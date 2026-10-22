using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class AppDataRefreshQueueTests
{
    [Fact]
    public async Task FailureRetainsLaterNotificationsAndTheOrderOfRenameChains()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<AppDataChangedEventArgs>? retried = null;
        var calls = 0;
        var queue = new AppDataRefreshQueue(async changes =>
        {
            if (++calls == 1) { started.SetResult(); await release.Task; throw new IOException("offline"); }
            retried = changes;
        });
        var source = Guid.NewGuid();
        var first = new AppDataChangedEventArgs(source, AppDataKind.Orders, "A", "B");
        var second = new AppDataChangedEventArgs(source, AppDataKind.Orders, "B", "C");
        var failed = queue.EnqueueAsync(first);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.EnqueueAsync(second).Forget();
        release.SetResult();
        await Assert.ThrowsAsync<IOException>(() => failed);
        await queue.EnqueueAsync(new(source, AppDataKind.Settings));
        Assert.Equal(new[] { first, second }, retried!.Where(c => c.Kinds == AppDataKind.Orders));
        Assert.Contains(retried!, c => c.Kinds == AppDataKind.Settings);
    }

    [Fact]
    public async Task BurstIsCombinedAndIncludesAllEntityKinds()
    {
        var batches = new List<IReadOnlyList<AppDataChangedEventArgs>>();
        var queue = new AppDataRefreshQueue(changes => { batches.Add(changes); return Task.CompletedTask; });
        var source = Guid.NewGuid();
        var first = new AppDataChangedEventArgs(source, AppDataKind.Orders, "A", "B");
        var task = queue.EnqueueAsync(first);
        for (var i = 0; i < 100; i++) Assert.Same(task, queue.EnqueueAsync(first));
        queue.EnqueueAsync(new(source, AppDataKind.Tours, "42", "42")).Forget();
        await task;
        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.Count);
        Assert.Contains(batch, c => c.Kinds == AppDataKind.Orders && c.CurrentId == "B");
        Assert.Contains(batch, c => c.Kinds == AppDataKind.Tours);
    }

    [Fact]
    public async Task NotificationsDuringReloadBecomeOneFollowUpWithoutOverlappingWorkers()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var inFlight = 0;
        var maximum = 0;
        var queue = new AppDataRefreshQueue(async _ =>
        {
            maximum = Math.Max(maximum, ++inFlight);
            if (++calls == 1) { started.SetResult(); await release.Task; }
            inFlight--;
        });
        var source = Guid.NewGuid();
        var worker = queue.EnqueueAsync(new(source, AppDataKind.Tours));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 100; i++) queue.EnqueueAsync(new(source, AppDataKind.Orders)).Forget();
        release.SetResult();
        await worker;
        Assert.Equal(2, calls);
        Assert.Equal(1, maximum);
    }

    [Fact]
    public async Task FailureIsObservedAndANewNotificationCanRetry()
    {
        var calls = 0;
        var queue = new AppDataRefreshQueue(_ => ++calls == 1
            ? Task.FromException(new IOException("offline")) : Task.CompletedTask);
        var change = new AppDataChangedEventArgs(Guid.NewGuid(), AppDataKind.Tours);
        await Assert.ThrowsAsync<IOException>(() => queue.EnqueueAsync(change));
        await queue.EnqueueAsync(change);
        Assert.Equal(2, calls);
    }
}
