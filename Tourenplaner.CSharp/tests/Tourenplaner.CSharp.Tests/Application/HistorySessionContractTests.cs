namespace Tourenplaner.CSharp.Tests.Application;

public abstract class HistorySessionContractTests
{
    protected const int MaximumHistoryDepth = 80;

    protected abstract IHistorySessionContractAdapter CreateSession();

    [Fact]
    public void NewSession_IsEmptyAndExposesNoDescriptions()
    {
        var session = CreateSession();

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.Empty(session.UndoDescription);
        Assert.Empty(session.RedoDescription);
        Assert.Empty(session.UndoEntriesNewestFirst);
        Assert.Empty(session.RedoEntriesNewestFirst);
    }

    [Fact]
    public void RecordingBeyondMaximumDepth_KeepsExactlyNewestEightyEntries()
    {
        var session = CreateSession();

        for (var index = 0; index < 120; index++)
        {
            session.RecordMutation(CreateEntry(index));
        }

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(MaximumHistoryDepth, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.Equal("Änderung 119", session.UndoDescription);
        Assert.Equal(
            Enumerable.Range(40, MaximumHistoryDepth).Reverse(),
            session.UndoEntriesNewestFirst.Select(entry => entry.Sequence));
    }

    [Fact]
    public void NewMutationAfterUndo_ClearsEntireRedoStack()
    {
        var session = CreateSession();
        session.RecordMutation(CreateEntry(1));
        session.RecordMutation(CreateEntry(2));
        session.RecordMutation(CreateEntry(3));
        session.Undo(CreateInverseEntry);
        session.Undo(CreateInverseEntry);

        Assert.Equal(1, session.UndoCount);
        Assert.Equal(2, session.RedoCount);
        Assert.True(session.CanRedo);

        session.RecordMutation(CreateEntry(4));

        Assert.Equal(2, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.False(session.CanRedo);
        Assert.Empty(session.RedoDescription);
        Assert.Empty(session.RedoEntriesNewestFirst);
    }

    [Fact]
    public void FailedUndo_KeepsSourceTopAndDestinationStackUnchanged()
    {
        var session = CreateSession();
        var first = CreateEntry(1);
        var second = CreateEntry(2);
        session.RecordMutation(first);
        session.RecordMutation(second);

        var exception = Assert.Throws<HistoryContractRestoreException>(() =>
            session.Undo(_ => throw new HistoryContractRestoreException("Wiederherstellung fehlgeschlagen.")));

        Assert.Equal("Wiederherstellung fehlgeschlagen.", exception.Message);
        Assert.Equal(2, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.Equal(second, session.UndoEntriesNewestFirst[0]);
        Assert.Equal(first, session.UndoEntriesNewestFirst[1]);
        Assert.Equal(second.Description, session.UndoDescription);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void FailedRedo_KeepsSourceTopAndDestinationStackUnchanged()
    {
        var session = CreateSession();
        var original = CreateEntry(1);
        session.RecordMutation(original);
        session.Undo(CreateInverseEntry);
        var redoTop = Assert.Single(session.RedoEntriesNewestFirst);

        Assert.Throws<HistoryContractRestoreException>(() =>
            session.Redo(_ => throw new HistoryContractRestoreException("Wiederholung fehlgeschlagen.")));

        Assert.Equal(0, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
        Assert.Equal(redoTop, Assert.Single(session.RedoEntriesNewestFirst));
        Assert.Equal(redoTop.Description, session.RedoDescription);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void SuccessfulUndoAndRedo_MoveInverseEntriesWithOriginalDescription()
    {
        var session = CreateSession();
        var original = CreateEntry(7);
        session.RecordMutation(original);

        session.Undo(CreateInverseEntry);

        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
        var redoEntry = Assert.Single(session.RedoEntriesNewestFirst);
        Assert.Equal(original.Description, redoEntry.Description);
        Assert.Equal("Inverse von Nutzdaten 7", redoEntry.Payload);

        session.Redo(CreateInverseEntry);

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        var restoredUndoEntry = Assert.Single(session.UndoEntriesNewestFirst);
        Assert.Equal(original.Description, restoredUndoEntry.Description);
        Assert.Equal("Inverse von Inverse von Nutzdaten 7", restoredUndoEntry.Payload);
    }

    [Fact]
    public void ConcurrentReadsAndMutations_KeepStacksConsistentAndBounded()
    {
        var session = CreateSession();
        var exceptions = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        Parallel.For(0, 4_000, index =>
        {
            try
            {
                session.RecordMutation(CreateEntry(index));
                _ = session.CanUndo;
                _ = session.CanRedo;
                _ = session.UndoDescription;
                _ = session.RedoDescription;
                _ = session.UndoEntriesNewestFirst.Count;
                _ = session.RedoEntriesNewestFirst.Count;
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(MaximumHistoryDepth, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.Equal(MaximumHistoryDepth, session.UndoEntriesNewestFirst.Count);
        Assert.Equal(
            MaximumHistoryDepth,
            session.UndoEntriesNewestFirst.Select(entry => entry.Sequence).Distinct().Count());
    }

    [Fact]
    public void ImmutableRecordCopies_DoNotModifyStoredHistoryEntry()
    {
        var session = CreateSession();
        var original = CreateEntry(12);
        session.RecordMutation(original);

        var modifiedCopy = original with
        {
            Description = "Geänderte Beschreibung",
            Payload = "Geänderte Nutzdaten"
        };

        var stored = Assert.Single(session.UndoEntriesNewestFirst);
        Assert.Equal("Änderung 12", stored.Description);
        Assert.Equal("Nutzdaten 12", stored.Payload);
        Assert.NotEqual(modifiedCopy, stored);
    }

    [Fact]
    public void Reset_ClearsUndoAndRedoStacksCompletely()
    {
        var session = CreateSession();
        session.RecordMutation(CreateEntry(1));
        session.RecordMutation(CreateEntry(2));
        session.Undo(CreateInverseEntry);

        session.Reset();

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.Empty(session.UndoDescription);
        Assert.Empty(session.RedoDescription);
        Assert.Empty(session.UndoEntriesNewestFirst);
        Assert.Empty(session.RedoEntriesNewestFirst);
    }

    private static HistoryContractEntry CreateEntry(int sequence) =>
        new(sequence, $"Änderung {sequence}", $"Nutzdaten {sequence}");

    private static HistoryContractEntry CreateInverseEntry(HistoryContractEntry entry) =>
        entry with { Payload = $"Inverse von {entry.Payload}" };
}

public interface IHistorySessionContractAdapter
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    int UndoCount { get; }
    int RedoCount { get; }
    string UndoDescription { get; }
    string RedoDescription { get; }
    IReadOnlyList<HistoryContractEntry> UndoEntriesNewestFirst { get; }
    IReadOnlyList<HistoryContractEntry> RedoEntriesNewestFirst { get; }

    void RecordMutation(HistoryContractEntry entry);
    void Undo(Func<HistoryContractEntry, HistoryContractEntry> restore);
    void Redo(Func<HistoryContractEntry, HistoryContractEntry> restore);
    void Reset();
}

public sealed record HistoryContractEntry(
    int Sequence,
    string Description,
    string Payload);

public sealed class HistoryContractRestoreException : Exception
{
    public HistoryContractRestoreException(string message)
        : base(message)
    {
    }
}
