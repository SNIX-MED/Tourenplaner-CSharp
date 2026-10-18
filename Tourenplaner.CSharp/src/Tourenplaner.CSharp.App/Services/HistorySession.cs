namespace Tourenplaner.CSharp.App.Services;

internal enum HistoryDirection
{
    Undo,
    Redo
}

internal sealed class HistorySession<TEntry>
{
    public const int DefaultMaximumDepth = 80;

    private readonly object _gate = new();
    private readonly Stack<TEntry> _undoStack = new();
    private readonly Stack<TEntry> _redoStack = new();
    private readonly Func<TEntry, string> _descriptionSelector;
    private readonly int _maximumDepth;

    public HistorySession(
        Func<TEntry, string> descriptionSelector,
        int maximumDepth = DefaultMaximumDepth)
    {
        ArgumentNullException.ThrowIfNull(descriptionSelector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        _descriptionSelector = descriptionSelector;
        _maximumDepth = maximumDepth;
    }

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

    public int UndoCount
    {
        get
        {
            lock (_gate)
            {
                return _undoStack.Count;
            }
        }
    }

    public int RedoCount
    {
        get
        {
            lock (_gate)
            {
                return _redoStack.Count;
            }
        }
    }

    public string UndoDescription
    {
        get
        {
            lock (_gate)
            {
                return TryGetDescription(_undoStack);
            }
        }
    }

    public string RedoDescription
    {
        get
        {
            lock (_gate)
            {
                return TryGetDescription(_redoStack);
            }
        }
    }

    public void RecordMutation(TEntry entry)
    {
        lock (_gate)
        {
            _undoStack.Push(entry);
            Trim(_undoStack);
            _redoStack.Clear();
        }
    }

    public bool TryGetRestoreTarget(HistoryDirection direction, out TEntry entry)
    {
        lock (_gate)
        {
            return GetSource(direction).TryPeek(out entry!);
        }
    }

    public void CompleteRestore(
        HistoryDirection direction,
        TEntry expectedSourceEntry,
        TEntry inverseEntry)
    {
        lock (_gate)
        {
            var source = GetSource(direction);
            if (!source.TryPeek(out var currentEntry) ||
                !EqualityComparer<TEntry>.Default.Equals(currentEntry, expectedSourceEntry))
            {
                throw new InvalidOperationException(
                    "Der Historieneintrag wurde während der Wiederherstellung verändert.");
            }

            source.Pop();
            var destination = GetDestination(direction);
            destination.Push(inverseEntry);
            Trim(destination);
        }
    }

    public IReadOnlyList<TEntry> GetEntriesNewestFirst(HistoryDirection direction)
    {
        lock (_gate)
        {
            return GetSource(direction).ToArray();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }

    private Stack<TEntry> GetSource(HistoryDirection direction) =>
        direction == HistoryDirection.Undo ? _undoStack : _redoStack;

    private Stack<TEntry> GetDestination(HistoryDirection direction) =>
        direction == HistoryDirection.Undo ? _redoStack : _undoStack;

    private string TryGetDescription(Stack<TEntry> stack) =>
        stack.TryPeek(out var entry)
            ? _descriptionSelector(entry) ?? string.Empty
            : string.Empty;

    private void Trim(Stack<TEntry> stack)
    {
        if (stack.Count <= _maximumDepth)
        {
            return;
        }

        var keptEntries = stack.Take(_maximumDepth).Reverse().ToArray();
        stack.Clear();
        foreach (var entry in keptEntries)
        {
            stack.Push(entry);
        }
    }
}
