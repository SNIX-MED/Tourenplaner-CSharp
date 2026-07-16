namespace Tourenplaner.CSharp.Application.Common;

public sealed record TourScheduleResult(
    DateTime Start,
    DateTime End,
    IReadOnlyList<TourStopScheduleEntry> Stops,
    DateTime? OptimisticEnd = null,
    DateTime? PessimisticEnd = null);

public sealed record TourStopScheduleEntry(
    string StopId,
    DateTime Arrival,
    DateTime Departure,
    bool HasConflict,
    string ConflictText,
    DateTime? OptimisticArrival = null,
    DateTime? PessimisticArrival = null);
