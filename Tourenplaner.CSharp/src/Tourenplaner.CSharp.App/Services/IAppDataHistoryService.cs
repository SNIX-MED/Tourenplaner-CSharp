namespace Tourenplaner.CSharp.App.Services;

public sealed record AppDataHistoryDisplayItem(
    long ChangeId,
    DateTimeOffset ChangedAt,
    string UserName,
    string Action,
    string EntityType,
    string EntityId);

public interface IAppDataHistoryService : IDisposable
{
    event EventHandler? StateChanged;

    bool CanUndo { get; }
    bool CanRedo { get; }
    string UndoDescription { get; }
    string RedoDescription { get; }
    bool HasPersistentHistory { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ResetSessionAsync(string? userName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppDataHistoryDisplayItem>> LoadRecentEntriesAsync(int maximumCount = 100, CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredEntriesAsync(int retentionDays, CancellationToken cancellationToken = default);
    Task UndoAsync();
    Task RedoAsync();
}
