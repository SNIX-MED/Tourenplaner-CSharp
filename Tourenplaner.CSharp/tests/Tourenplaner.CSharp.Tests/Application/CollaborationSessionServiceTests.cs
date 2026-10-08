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

    private sealed class FakeLockService : ICollaborationLockService
    {
        public string ConflictUser { get; set; } = string.Empty;
        public int ResourceAcquireCount { get; private set; }
        public int ResourceReleaseCount { get; private set; }

        public Task<CollaborationLockResult> TryAcquireAsync(string resourceKind, string resourceId, string userName, CancellationToken cancellationToken = default)
        {
            if (resourceKind == "user" && ConflictUser.Length > 0)
                return Task.FromResult(CollaborationLockResult.Conflict(ConflictUser));
            if (resourceKind != "user") ResourceAcquireCount++;
            return Task.FromResult(CollaborationLockResult.Success());
        }

        public Task ReleaseAsync(string resourceKind, string resourceId, CancellationToken cancellationToken = default)
        {
            if (resourceKind != "user") ResourceReleaseCount++;
            return Task.CompletedTask;
        }

        public Task ReleaseAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
