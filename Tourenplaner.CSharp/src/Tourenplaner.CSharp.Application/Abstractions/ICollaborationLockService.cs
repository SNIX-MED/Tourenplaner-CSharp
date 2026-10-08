namespace Tourenplaner.CSharp.Application.Abstractions;

public sealed record CollaborationLockResult(bool Acquired, string LockedByUserName)
{
    public static CollaborationLockResult Success() => new(true, string.Empty);
    public static CollaborationLockResult Conflict(string userName) => new(false, userName);
}

public interface ICollaborationLockService : IAsyncDisposable
{
    Task<CollaborationLockResult> TryAcquireAsync(string resourceKind, string resourceId, string userName, CancellationToken cancellationToken = default);
    Task ReleaseAsync(string resourceKind, string resourceId, CancellationToken cancellationToken = default);
    Task ReleaseAllAsync(CancellationToken cancellationToken = default);
}
