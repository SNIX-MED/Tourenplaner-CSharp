using System.Reflection;
using System.Runtime.CompilerServices;
using Tourenplaner.CSharp.App.Views.Sections;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class MapSelectionRefreshTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public async Task RapidSelectionChanges_CoalesceWithoutLosingPendingUpdates(int debounce)
    {
        var view = (KarteSectionView)RuntimeHelpers.GetUninitializedObject(typeof(KarteSectionView));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var queue = typeof(KarteSectionView).GetMethod("QueueMapRefresh", flags)!;
        var pending = typeof(KarteSectionView).GetField("_pendingMapRefresh", flags)!;
        var revision = typeof(KarteSectionView).GetField("_mapRefreshRevision", flags)!;
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            for (var i = 0; i < 100; i++)
                queue.Invoke(view, [Enum.Parse(pending.FieldType, i % 2 == 0 ? "MarkerSelection" : "RouteSelection"), debounce]);
            Assert.NotEqual(0, Convert.ToInt32(pending.GetValue(view)));
            Assert.Equal(100, Convert.ToInt32(revision.GetValue(view)));
            context.Drain();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        // Timer continuations post back to the captured UI context.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Convert.ToInt32(pending.GetValue(view)) != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1);
            context.Drain();
        }
        Assert.Equal(0, Convert.ToInt32(pending.GetValue(view)));
        Assert.False((bool)typeof(KarteSectionView).GetField("_isMapRefreshLoopRunning", flags)!.GetValue(view)!);
    }

    [Fact]
    public void RefreshDuringActiveLoop_LeavesWorkForThatLoop()
    {
        var view = (KarteSectionView)RuntimeHelpers.GetUninitializedObject(typeof(KarteSectionView));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var running = typeof(KarteSectionView).GetField("_isMapRefreshLoopRunning", flags)!;
        var pending = typeof(KarteSectionView).GetField("_pendingMapRefresh", flags)!;
        var queue = typeof(KarteSectionView).GetMethod("QueueMapRefresh", flags)!;
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            running.SetValue(view, true);
            queue.Invoke(view, [Enum.Parse(pending.FieldType, "RouteSelection"), 0]);
            context.Drain();
            Assert.NotEqual(0, Convert.ToInt32(pending.GetValue(view)));
            Assert.True((bool)running.GetValue(view)!);
            running.SetValue(view, false);
            queue.Invoke(view, [Enum.Parse(pending.FieldType, "MarkerSelection"), 0]);
            context.Drain();
            Assert.Equal(0, Convert.ToInt32(pending.GetValue(view)));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public async Task SelectionRefresh_WaitsForSynchronousSelectionTransaction()
    {
        // No WebView is needed: exercise the real queue on an uninitialised view.
        var view = (KarteSectionView)RuntimeHelpers.GetUninitializedObject(typeof(KarteSectionView));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var pending = typeof(KarteSectionView).GetField("_pendingMapRefresh", flags)!;
        var operation = pending.FieldType;
        var marker = Convert.ToInt32(Enum.Parse(operation, "MarkerSelection"));
        var route = Convert.ToInt32(Enum.Parse(operation, "RouteSelection"));
        pending.SetValue(view, Enum.ToObject(operation, marker));
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        Task task;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            task = (Task)typeof(KarteSectionView).GetMethod("ProcessMapRefreshQueueAsync", flags)!
                .Invoke(view, [0])!;
            Assert.False(task.IsCompleted);
            Assert.Equal(marker, Convert.ToInt32(pending.GetValue(view)));
            // The remaining setter notifications join the same pending update.
            pending.SetValue(view, Enum.ToObject(operation, marker | route));
            context.Drain();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        await task;
        Assert.Equal(0, Convert.ToInt32(pending.GetValue(view)));
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        public override void Post(SendOrPostCallback d, object? state) => _callbacks.Enqueue((d, state));
        public void Drain()
        {
            while (_callbacks.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
}
