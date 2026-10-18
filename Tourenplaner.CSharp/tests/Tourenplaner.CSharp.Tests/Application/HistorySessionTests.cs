using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class HistorySessionTests : HistorySessionContractTests
{
    protected override IHistorySessionContractAdapter CreateSession() =>
        new HistorySessionContractAdapter();

    private sealed class HistorySessionContractAdapter : IHistorySessionContractAdapter
    {
        private readonly HistorySession<HistoryContractEntry> _session =
            new(entry => entry.Description, MaximumHistoryDepth);

        public bool CanUndo => _session.CanUndo;
        public bool CanRedo => _session.CanRedo;
        public int UndoCount => _session.UndoCount;
        public int RedoCount => _session.RedoCount;
        public string UndoDescription => _session.UndoDescription;
        public string RedoDescription => _session.RedoDescription;
        public IReadOnlyList<HistoryContractEntry> UndoEntriesNewestFirst =>
            _session.GetEntriesNewestFirst(HistoryDirection.Undo);
        public IReadOnlyList<HistoryContractEntry> RedoEntriesNewestFirst =>
            _session.GetEntriesNewestFirst(HistoryDirection.Redo);

        public void RecordMutation(HistoryContractEntry entry) =>
            _session.RecordMutation(entry);

        public void Undo(Func<HistoryContractEntry, HistoryContractEntry> restore) =>
            Restore(HistoryDirection.Undo, restore);

        public void Redo(Func<HistoryContractEntry, HistoryContractEntry> restore) =>
            Restore(HistoryDirection.Redo, restore);

        public void Reset() =>
            _session.Reset();

        private void Restore(
            HistoryDirection direction,
            Func<HistoryContractEntry, HistoryContractEntry> restore)
        {
            if (!_session.TryGetRestoreTarget(direction, out var target))
            {
                return;
            }

            var inverse = restore(target);
            _session.CompleteRestore(direction, target, inverse);
        }
    }
}
