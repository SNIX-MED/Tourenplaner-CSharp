using System.IO;

namespace Tourenplaner.CSharp.App.Services;

public sealed class AppDataHistoryService : IAppDataHistoryService
{
    private sealed record AppDataSnapshot(
        IReadOnlyDictionary<string, byte[]?> Files,
        string Description);

    private static readonly TimeSpan CaptureDebounce = TimeSpan.FromMilliseconds(140);

    private readonly object _gate = new();
    private readonly AppDataSyncService _dataSyncService;
    private readonly HashSet<string> _trackedPaths;
    private readonly HistorySession<AppDataSnapshot> _history =
        new(snapshot => snapshot.Description);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly AsyncDebounceCoordinator _captureDebouncer;
    private AppDataSnapshot? _current;
    private string _pendingDescription = "Änderung";
    private bool _captureSuppressed;

    public AppDataHistoryService(
        AppDataSyncService dataSyncService,
        params string[] trackedJsonPaths)
    {
        _dataSyncService = dataSyncService;
        _trackedPaths = trackedJsonPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _captureDebouncer = new AsyncDebounceCoordinator(
            CaptureDebounce,
            _ =>
            {
                CaptureIfChanged();
                return Task.CompletedTask;
            });
    }

    public event EventHandler? StateChanged;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string UndoDescription => _history.UndoDescription;

    public string RedoDescription => _history.RedoDescription;

    public bool HasPersistentHistory => false;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_current is not null)
            {
                return Task.CompletedTask;
            }

            _current = ReadSnapshot("Initialzustand");
        }

        SetupWatchers();
        _dataSyncService.DataChanged += OnDataChanged;
        RaiseStateChanged();
        return Task.CompletedTask;
    }

    public Task UndoAsync() =>
        RestoreAsync(HistoryDirection.Undo);

    public Task RedoAsync() =>
        RestoreAsync(HistoryDirection.Redo);

    public Task ResetSessionAsync(
        string? userName,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _history.Reset();
            _current = ReadSnapshot("Initialzustand");
        }

        RaiseStateChanged();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AppDataHistoryDisplayItem>> LoadRecentEntriesAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AppDataHistoryDisplayItem>>([]);

    public Task<int> CleanupExpiredEntriesAsync(
        int retentionDays,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public void Dispose()
    {
        _dataSyncService.DataChanged -= OnDataChanged;
        _captureDebouncer.Dispose();

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private async Task RestoreAsync(HistoryDirection direction)
    {
        AppDataSnapshot? target;
        AppDataSnapshot? current;

        lock (_gate)
        {
            if (_current is null || !_history.TryGetRestoreTarget(direction, out target))
            {
                return;
            }

            current = _current;
            _captureSuppressed = true;
        }

        try
        {
            await RestoreSnapshotAsync(target);
            var inverse = CloneSnapshotWithDescription(current, target.Description);
            _history.CompleteRestore(direction, target, inverse);
            lock (_gate)
            {
                _current = CloneSnapshot(target);
            }
        }
        finally
        {
            lock (_gate)
            {
                _captureSuppressed = false;
            }
        }

        PublishFullReload();
        RaiseStateChanged();
    }

    private void SetupWatchers()
    {
        var directories = _trackedPaths
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(directory!)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                               NotifyFilters.Size | NotifyFilters.CreationTime
            };
            watcher.Changed += OnTrackedFileChanged;
            watcher.Created += OnTrackedFileChanged;
            watcher.Renamed += OnTrackedFileRenamed;
            watcher.Deleted += OnTrackedFileChanged;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId)
        {
            return;
        }

        ScheduleCapture(AppDataHistoryDescriptionFormatter.Format(
            args,
            AppDataHistoryDescriptionProfile.LocalFile));
    }

    private void OnTrackedFileChanged(object sender, FileSystemEventArgs args)
    {
        if (IsTracked(args.FullPath))
        {
            ScheduleCapture("Dateiänderung", lowPriority: true);
        }
    }

    private void OnTrackedFileRenamed(object sender, RenamedEventArgs args)
    {
        if (IsTracked(args.FullPath) || IsTracked(args.OldFullPath))
        {
            ScheduleCapture("Dateiumbenennung", lowPriority: true);
        }
    }

    private bool IsTracked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return _trackedPaths.Contains(Path.GetFullPath(path));
    }

    private void ScheduleCapture(string description, bool lowPriority = false)
    {
        lock (_gate)
        {
            if (_captureSuppressed || _current is null)
            {
                return;
            }

            var normalizedDescription = string.IsNullOrWhiteSpace(description)
                ? "Änderung"
                : description.Trim();
            if (!lowPriority ||
                AppDataHistoryDescriptionFormatter.IsGeneric(_pendingDescription) ||
                !AppDataHistoryDescriptionFormatter.IsGeneric(normalizedDescription))
            {
                _pendingDescription = normalizedDescription;
            }
        }

        _captureDebouncer.Schedule();
    }

    private void CaptureIfChanged()
    {
        var shouldRaise = false;
        lock (_gate)
        {
            if (_captureSuppressed || _current is null)
            {
                return;
            }

            var next = ReadSnapshot(_pendingDescription);
            if (SnapshotsEqual(_current, next))
            {
                return;
            }

            _history.RecordMutation(CloneSnapshotWithDescription(_current, next.Description));
            _current = CloneSnapshot(next);
            shouldRaise = true;
        }

        if (shouldRaise)
        {
            RaiseStateChanged();
        }
    }

    private AppDataSnapshot ReadSnapshot(string description)
    {
        var files = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _trackedPaths)
        {
            files[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        return new AppDataSnapshot(
            files,
            string.IsNullOrWhiteSpace(description) ? "Änderung" : description);
    }

    private static AppDataSnapshot CloneSnapshot(AppDataSnapshot source)
    {
        var clonedFiles = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source.Files)
        {
            clonedFiles[pair.Key] = pair.Value is null ? null : (byte[])pair.Value.Clone();
        }

        return new AppDataSnapshot(clonedFiles, source.Description);
    }

    private static AppDataSnapshot CloneSnapshotWithDescription(
        AppDataSnapshot source,
        string description)
    {
        var cloned = CloneSnapshot(source);
        return cloned with
        {
            Description = string.IsNullOrWhiteSpace(description)
                ? source.Description
                : description
        };
    }

    private static bool SnapshotsEqual(AppDataSnapshot left, AppDataSnapshot right)
    {
        if (left.Files.Count != right.Files.Count)
        {
            return false;
        }

        foreach (var pair in left.Files)
        {
            if (!right.Files.TryGetValue(pair.Key, out var rightBytes) ||
                !ByteArraysEqual(pair.Value, rightBytes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ByteArraysEqual(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null &&
               right is not null &&
               left.Length == right.Length &&
               left.AsSpan().SequenceEqual(right);
    }

    private static async Task RestoreSnapshotAsync(AppDataSnapshot snapshot)
    {
        foreach (var pair in snapshot.Files)
        {
            if (pair.Value is null)
            {
                if (File.Exists(pair.Key))
                {
                    File.Delete(pair.Key);
                }

                continue;
            }

            await WriteBytesAtomicAsync(pair.Key, pair.Value);
        }
    }

    private static async Task WriteBytesAtomicAsync(string path, byte[] content)
    {
        var fileInfo = new FileInfo(path);
        fileInfo.Directory?.Create();
        var tempPath = Path.Combine(
            fileInfo.DirectoryName ?? string.Empty,
            $"{fileInfo.Name}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(tempPath, content);
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.Replace(
                        tempPath,
                        path,
                        destinationBackupFileName: null,
                        ignoreMetadataErrors: true);
                    return;
                }
                catch (IOException) when (attempt < 2)
                {
                    await Task.Delay(40 * (attempt + 1));
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void PublishFullReload()
    {
        const AppDataKind allKinds = AppDataKind.Orders | AppDataKind.Tours |
                                     AppDataKind.Employees | AppDataKind.Vehicles |
                                     AppDataKind.Settings | AppDataKind.CalendarEntries;
        _dataSyncService.Publish(new AppDataChangedEventArgs(_instanceId, allKinds));
    }

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, EventArgs.Empty);
}
