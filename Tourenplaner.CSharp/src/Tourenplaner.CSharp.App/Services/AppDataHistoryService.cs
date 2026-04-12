using System.IO;

namespace Tourenplaner.CSharp.App.Services;

public sealed class AppDataHistoryService : IDisposable
{
    private sealed class AppDataSnapshot
    {
        public required IReadOnlyDictionary<string, byte[]?> Files { get; init; }
        public required string Description { get; init; }
    }

    private const int MaxHistoryEntries = 80;
    private static readonly TimeSpan CaptureDebounce = TimeSpan.FromMilliseconds(140);

    private readonly object _gate = new();
    private readonly AppDataSyncService _dataSyncService;
    private readonly HashSet<string> _trackedPaths;
    private readonly Stack<AppDataSnapshot> _undoStack = new();
    private readonly Stack<AppDataSnapshot> _redoStack = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private CancellationTokenSource? _captureCts;
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
            .Select(path => Path.GetFullPath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler? StateChanged;

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _undoStack.Count > 0;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _redoStack.Count > 0;
            }
        }
    }

    public string UndoDescription
    {
        get
        {
            lock (_gate)
            {
                return _undoStack.Count == 0 ? string.Empty : _undoStack.Peek().Description;
            }
        }
    }

    public string RedoDescription
    {
        get
        {
            lock (_gate)
            {
                return _redoStack.Count == 0 ? string.Empty : _redoStack.Peek().Description;
            }
        }
    }

    public void Initialize()
    {
        lock (_gate)
        {
            if (_current is not null)
            {
                return;
            }

            _current = ReadSnapshot("Initialzustand");
        }

        SetupWatchers();
        _dataSyncService.DataChanged += OnDataChanged;
        RaiseStateChanged();
    }

    public async Task UndoAsync()
    {
        AppDataSnapshot? target;
        AppDataSnapshot? current;

        lock (_gate)
        {
            if (_undoStack.Count == 0 || _current is null)
            {
                return;
            }

            target = _undoStack.Pop();
            current = _current;
            _redoStack.Push(CloneSnapshotWithDescription(current, target.Description));
            _current = CloneSnapshot(target);
            _captureSuppressed = true;
        }

        try
        {
            await RestoreSnapshotAsync(target);
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

    public async Task RedoAsync()
    {
        AppDataSnapshot? target;
        AppDataSnapshot? current;

        lock (_gate)
        {
            if (_redoStack.Count == 0 || _current is null)
            {
                return;
            }

            target = _redoStack.Pop();
            current = _current;
            _undoStack.Push(CloneSnapshotWithDescription(current, target.Description));
            _current = CloneSnapshot(target);
            _captureSuppressed = true;
        }

        try
        {
            await RestoreSnapshotAsync(target);
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

    public void Dispose()
    {
        _dataSyncService.DataChanged -= OnDataChanged;

        lock (_gate)
        {
            _captureCts?.Cancel();
            _captureCts?.Dispose();
            _captureCts = null;
        }

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void SetupWatchers()
    {
        var grouped = _trackedPaths
            .Select(path => Path.GetDirectoryName(path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var directory in grouped)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(directory!)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
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

        ScheduleCapture(BuildDescriptionFromArgs(args));
    }

    private void OnTrackedFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsTracked(e.FullPath))
        {
            return;
        }

        ScheduleCapture("Dateiänderung", lowPriority: true);
    }

    private void OnTrackedFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsTracked(e.FullPath) && !IsTracked(e.OldFullPath))
        {
            return;
        }

        ScheduleCapture("Dateiumbenennung", lowPriority: true);
    }

    private bool IsTracked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = Path.GetFullPath(path);
        return _trackedPaths.Contains(normalized);
    }

    private void ScheduleCapture(string description, bool lowPriority = false)
    {
        CancellationTokenSource captureCts;

        lock (_gate)
        {
            if (_captureSuppressed || _current is null)
            {
                return;
            }

            var normalizedDescription = string.IsNullOrWhiteSpace(description) ? "Änderung" : description.Trim();
            if (!lowPriority || IsGenericDescription(_pendingDescription) || !IsGenericDescription(normalizedDescription))
            {
                _pendingDescription = normalizedDescription;
            }

            _captureCts?.Cancel();
            _captureCts?.Dispose();
            _captureCts = new CancellationTokenSource();
            captureCts = _captureCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CaptureDebounce, captureCts.Token);
                if (!captureCts.IsCancellationRequested)
                {
                    CaptureIfChanged();
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore superseded captures.
            }
        }, captureCts.Token);
    }

    private void CaptureIfChanged()
    {
        AppDataSnapshot? next;
        AppDataSnapshot? current;
        var shouldRaise = false;

        lock (_gate)
        {
            if (_captureSuppressed || _current is null)
            {
                return;
            }

            current = _current;
            next = ReadSnapshot(_pendingDescription);

            if (SnapshotsEqual(current, next))
            {
                return;
            }

            _undoStack.Push(CloneSnapshotWithDescription(current, next.Description));
            TrimHistory(_undoStack);
            _redoStack.Clear();
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
        var data = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _trackedPaths)
        {
            if (!File.Exists(path))
            {
                data[path] = null;
                continue;
            }

            data[path] = File.ReadAllBytes(path);
        }

        return new AppDataSnapshot
        {
            Files = data,
            Description = string.IsNullOrWhiteSpace(description) ? "Änderung" : description
        };
    }

    private static AppDataSnapshot CloneSnapshot(AppDataSnapshot source)
    {
        var clonedFiles = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source.Files)
        {
            clonedFiles[pair.Key] = pair.Value is null ? null : (byte[])pair.Value.Clone();
        }

        return new AppDataSnapshot
        {
            Files = clonedFiles,
            Description = source.Description
        };
    }

    private static AppDataSnapshot CloneSnapshotWithDescription(AppDataSnapshot source, string description)
    {
        var cloned = CloneSnapshot(source);
        return new AppDataSnapshot
        {
            Files = cloned.Files,
            Description = string.IsNullOrWhiteSpace(description) ? source.Description : description
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
            if (!right.Files.TryGetValue(pair.Key, out var rightBytes))
            {
                return false;
            }

            if (!ByteArraysEqual(pair.Value, rightBytes))
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

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return left.AsSpan().SequenceEqual(right);
    }

    private static void TrimHistory(Stack<AppDataSnapshot> stack)
    {
        if (stack.Count <= MaxHistoryEntries)
        {
            return;
        }

        var kept = stack.Take(MaxHistoryEntries).Reverse().ToArray();
        stack.Clear();
        foreach (var item in kept)
        {
            stack.Push(item);
        }
    }

    private async Task RestoreSnapshotAsync(AppDataSnapshot snapshot)
    {
        foreach (var pair in snapshot.Files)
        {
            var path = pair.Key;
            var content = pair.Value;

            if (content is null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                continue;
            }

            await WriteBytesAtomicAsync(path, content);
        }
    }

    private static async Task WriteBytesAtomicAsync(string path, byte[] content)
    {
        var fileInfo = new FileInfo(path);
        fileInfo.Directory?.Create();

        var tempPath = Path.Combine(fileInfo.DirectoryName ?? string.Empty, $"{fileInfo.Name}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private void PublishFullReload()
    {
        const AppDataKind allKinds = AppDataKind.Orders | AppDataKind.Tours | AppDataKind.Employees | AppDataKind.Vehicles | AppDataKind.Settings;
        _dataSyncService.Publish(new AppDataChangedEventArgs(_instanceId, allKinds));
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribeKinds(AppDataKind kinds)
    {
        if (kinds == AppDataKind.None)
        {
            return "Änderung";
        }

        var labels = new List<string>();
        if (kinds.HasFlag(AppDataKind.Orders))
        {
            labels.Add("Aufträge");
        }

        if (kinds.HasFlag(AppDataKind.Tours))
        {
            labels.Add("Touren");
        }

        if (kinds.HasFlag(AppDataKind.Employees))
        {
            labels.Add("Mitarbeiter");
        }

        if (kinds.HasFlag(AppDataKind.Vehicles))
        {
            labels.Add("Fahrzeuge");
        }

        if (kinds.HasFlag(AppDataKind.Settings))
        {
            labels.Add("Einstellungen");
        }

        return labels.Count == 0 ? "Änderung" : string.Join(", ", labels);
    }

    private static bool IsGenericDescription(string? description)
    {
        var text = (description ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(text) ||
               string.Equals(text, "Änderung", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "Dateiänderung", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "Dateiumbenennung", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDescriptionFromArgs(AppDataChangedEventArgs args)
    {
        var kinds = args.Kinds;
        if (kinds == AppDataKind.Orders)
        {
            return BuildEntityAction("Auftrag", "Aufträge", args.PreviousId, args.CurrentId);
        }

        if (kinds == AppDataKind.Tours)
        {
            return BuildEntityAction("Tour", "Touren", args.PreviousId, args.CurrentId);
        }

        if (kinds == AppDataKind.Employees)
        {
            return BuildEntityAction("Mitarbeiter", "Mitarbeiter", args.PreviousId, args.CurrentId);
        }

        if (kinds == AppDataKind.Vehicles)
        {
            return BuildEntityAction("Fahrzeug", "Fahrzeuge", args.PreviousId, args.CurrentId);
        }

        if (kinds == AppDataKind.Settings)
        {
            return "Einstellungen ändern";
        }

        return DescribeKinds(kinds);
    }

    private static string BuildEntityAction(string singular, string plural, string? previousId, string? currentId)
    {
        var hasPrevious = !string.IsNullOrWhiteSpace(previousId);
        var hasCurrent = !string.IsNullOrWhiteSpace(currentId);

        if (!hasPrevious && hasCurrent)
        {
            return $"{singular} erstellen";
        }

        if (hasPrevious && !hasCurrent)
        {
            return $"{singular} löschen";
        }

        if (hasPrevious && hasCurrent)
        {
            return string.Equals(previousId, currentId, StringComparison.OrdinalIgnoreCase)
                ? $"{singular} aktualisieren"
                : $"{singular} umbenennen";
        }

        return $"{plural} ändern";
    }
}
