using Tourenplaner.CSharp.Application.Abstractions;

namespace Tourenplaner.CSharp.App.Services;

public sealed class CollaborationResourceLease : IDisposable
{
    private readonly string _resourceKind;
    private readonly string _resourceId;
    private int _disposed;

    internal CollaborationResourceLease(string resourceKind, string resourceId)
    {
        _resourceKind = resourceKind;
        _resourceId = resourceId;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            CollaborationSessionService.ReleaseResourceAsync(_resourceKind, _resourceId).Forget();
        }
    }
}

public static class CollaborationSessionService
{
    private sealed class LocalLockService : ICollaborationLockService
    {
        public Task<CollaborationLockResult> TryAcquireAsync(string resourceKind, string resourceId, string userName, CancellationToken cancellationToken = default)
            => Task.FromResult(CollaborationLockResult.Success());
        public Task ReleaseAsync(string resourceKind, string resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReleaseAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ICollaborationLockService _lockService = new LocalLockService();
    private static string _currentUserName = string.Empty;
    private static readonly object ReferenceGate = new();
    private static readonly Dictionary<(string Kind, string Id), int> ReferenceCounts = new();

    public static string CurrentUserName => _currentUserName;
    public static bool HasActiveResourceLocks
    {
        get { lock (ReferenceGate) return ReferenceCounts.Count > 0; }
    }

    public static void Configure(ICollaborationLockService? lockService)
    {
        _lockService = lockService ?? new LocalLockService();
    }

    public static async Task<CollaborationLockResult> TryLoginAsync(string userName, CancellationToken cancellationToken = default)
    {
        var normalized = (userName ?? string.Empty).Trim();
        var result = await _lockService.TryAcquireAsync("user", normalized, normalized, cancellationToken);
        if (result.Acquired)
        {
            if (!string.IsNullOrWhiteSpace(_currentUserName) &&
                !string.Equals(_currentUserName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                await _lockService.ReleaseAsync("user", _currentUserName, cancellationToken);
            }
            _currentUserName = normalized;
        }
        return result;
    }

    public static async Task<(CollaborationResourceLease? Lease, CollaborationLockResult Result)> TryAcquireResourceAsync(
        string resourceKind,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var key = (resourceKind.Trim().ToLowerInvariant(), resourceId.Trim().ToLowerInvariant());
        lock (ReferenceGate)
        {
            if (ReferenceCounts.TryGetValue(key, out var count))
            {
                ReferenceCounts[key] = count + 1;
                return (new CollaborationResourceLease(resourceKind, resourceId), CollaborationLockResult.Success());
            }
        }

        var result = await _lockService.TryAcquireAsync(resourceKind, resourceId, _currentUserName, cancellationToken);
        if (result.Acquired)
        {
            lock (ReferenceGate) ReferenceCounts[key] = 1;
        }
        return result.Acquired
            ? (new CollaborationResourceLease(resourceKind, resourceId), result)
            : (null, result);
    }

    internal static Task ReleaseResourceAsync(string resourceKind, string resourceId)
    {
        var key = (resourceKind.Trim().ToLowerInvariant(), resourceId.Trim().ToLowerInvariant());
        lock (ReferenceGate)
        {
            if (ReferenceCounts.TryGetValue(key, out var count) && count > 1)
            {
                ReferenceCounts[key] = count - 1;
                return Task.CompletedTask;
            }
            ReferenceCounts.Remove(key);
        }
        return _lockService.ReleaseAsync(resourceKind, resourceId);
    }

    public static async Task ShutdownAsync()
    {
        lock (ReferenceGate) ReferenceCounts.Clear();
        await _lockService.DisposeAsync();
        _currentUserName = string.Empty;
    }
}
