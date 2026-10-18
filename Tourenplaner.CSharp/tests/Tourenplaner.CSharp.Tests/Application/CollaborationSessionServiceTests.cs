using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Abstractions;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class CollaborationSessionServiceTests
{
    [Fact]
    public async Task ResourceLock_IsReleasedAfterLastLocalLease()
    {
        var locks = new FakeLockService();
        CollaborationSessionService.Configure(locks);
        Assert.True((await CollaborationSessionService.TryLoginAsync("Anna")).Acquired);

        var first = await CollaborationSessionService.TryAcquireResourceAsync("order", "A-1");
        var second = await CollaborationSessionService.TryAcquireResourceAsync("order", "A-1");

        Assert.NotNull(first.Lease);
        Assert.NotNull(second.Lease);
        Assert.Equal(1, locks.ResourceAcquireCount);
        first.Lease!.Dispose();
        Assert.Equal(0, locks.ResourceReleaseCount);
        second.Lease!.Dispose();
        await Task.Delay(20);
        Assert.Equal(1, locks.ResourceReleaseCount);
        await CollaborationSessionService.ShutdownAsync();
    }

    [Fact]
    public async Task LoginConflict_ReturnsCurrentOwner()
    {
        var locks = new FakeLockService { ConflictUser = "Beat" };
        CollaborationSessionService.Configure(locks);

        var result = await CollaborationSessionService.TryLoginAsync("Beat");

        Assert.False(result.Acquired);
        Assert.Equal("Beat", result.LockedByUserName);
        await CollaborationSessionService.ShutdownAsync();
    }

    [Fact]
    public async Task Reacquire_WaitsUntilPreviousDatabaseReleaseHasCompleted()
    {
        var locks = new FakeLockService { BlockResourceRelease = true };
        CollaborationSessionService.Configure(locks);
        Assert.True((await CollaborationSessionService.TryLoginAsync("Anna")).Acquired);

        var first = await CollaborationSessionService.TryAcquireResourceAsync("tour", "19");
        Assert.NotNull(first.Lease);
        first.Lease!.Dispose();
        await locks.ResourceReleaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var reacquire = CollaborationSessionService.TryAcquireResourceAsync("tour", "19");
        await Task.Delay(20);
        Assert.False(reacquire.IsCompleted);

        locks.CompleteResourceRelease();
        var second = await reacquire;
        Assert.NotNull(second.Lease);
        Assert.Equal(2, locks.ResourceAcquireCount);

        second.Lease!.Dispose();
        await CollaborationSessionService.ShutdownAsync();
    }

    private sealed class FakeLockService : ICollaborationLockService
    {
        public string ConflictUser { get; set; } = string.Empty;
        public int ResourceAcquireCount { get; private set; }
        public int ResourceReleaseCount { get; private set; }
        public bool BlockResourceRelease { get; init; }
        public TaskCompletionSource ResourceReleaseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource ResourceReleaseCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CollaborationLockResult> TryAcquireAsync(string resourceKind, string resourceId, string userName, CancellationToken cancellationToken = default)
        {
            if (resourceKind == "user" && ConflictUser.Length > 0)
                return Task.FromResult(CollaborationLockResult.Conflict(ConflictUser));
            if (resourceKind != "user") ResourceAcquireCount++;
            return Task.FromResult(CollaborationLockResult.Success());
        }

        public async Task ReleaseAsync(string resourceKind, string resourceId, CancellationToken cancellationToken = default)
        {
            if (resourceKind != "user") ResourceReleaseCount++;
            if (resourceKind != "user" && BlockResourceRelease)
            {
                ResourceReleaseStarted.TrySetResult();
                await ResourceReleaseCompletion.Task.WaitAsync(cancellationToken);
            }
        }

        public void CompleteResourceRelease() => ResourceReleaseCompletion.TrySetResult();

        public Task ReleaseAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
