using System.Globalization;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class TourScheduleService
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];
    public const int CurrentVersion = 2;
    public const int PessimisticArrivalToleranceMinutes = 5;
    private int _trafficBufferPercentFrom0500To0730;
    private int _trafficBufferPercentFrom0730To0900;
    private int _trafficBufferPercentFrom0900To1530;
    private int _trafficBufferPercentFrom1530To1830;

    public TourScheduleService(
        int trafficBufferPercentFrom0500To0730 = AppSettings.DefaultTrafficBufferPercentFrom0500To0730,
        int trafficBufferPercentFrom0730To0900 = AppSettings.DefaultTrafficBufferPercentFrom0730To0900,
        int trafficBufferPercentFrom0900To1530 = AppSettings.DefaultTrafficBufferPercentFrom0900To1530,
        int trafficBufferPercentFrom1530To1830 = AppSettings.DefaultTrafficBufferPercentFrom1530To1830)
    {
        SetTrafficBufferPercentProfile(
            trafficBufferPercentFrom0500To0730,
            trafficBufferPercentFrom0730To0900,
            trafficBufferPercentFrom0900To1530,
            trafficBufferPercentFrom1530To1830);
    }

    public void SetTrafficBufferPercentProfile(
        int trafficBufferPercentFrom0500To0730,
        int trafficBufferPercentFrom0730To0900,
        int trafficBufferPercentFrom0900To1530,
        int trafficBufferPercentFrom1530To1830)
    {
        _trafficBufferPercentFrom0500To0730 = Math.Clamp(trafficBufferPercentFrom0500To0730, 0, 100);
        _trafficBufferPercentFrom0730To0900 = Math.Clamp(trafficBufferPercentFrom0730To0900, 0, 100);
        _trafficBufferPercentFrom0900To1530 = Math.Clamp(trafficBufferPercentFrom0900To1530, 0, 100);
        _trafficBufferPercentFrom1530To1830 = Math.Clamp(trafficBufferPercentFrom1530To1830, 0, 100);
    }

    public TourScheduleResult BuildSchedule(TourRecord tour, int fallbackTravelMinutes = 15)
    {
        var start = ResolveStartDateTime(tour);
        var entries = new List<TourStopScheduleEntry>();
        var optimisticCurrent = start;
        var realisticCurrent = start;
        var pessimisticCurrent = start;

        var orderedStops = (tour.Stops ?? [])
            .OrderBy(s => s.Order > 0 ? s.Order : int.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < orderedStops.Count; i++)
        {
            var stop = orderedStops[i];
            var travelProfile = ResolveTravelTimeProfile(
                tour.TravelTimeProfileCache,
                tour.TravelTimeCache,
                orderedStops,
                i,
                fallbackTravelMinutes);

            var realisticArrival = realisticCurrent.AddMinutes(travelProfile.RealisticMinutes);
            var realisticLegDeparture = realisticCurrent;
            var optimisticArrival = optimisticCurrent.AddMinutes(Math.Min(travelProfile.OptimisticMinutes, travelProfile.RealisticMinutes));
            var pessimisticArrival = pessimisticCurrent.AddMinutes(Math.Max(travelProfile.PessimisticMinutes, travelProfile.RealisticMinutes));
            var bufferProfile = tour.ScheduleTrafficBufferPercentages;
            var trafficBufferMinutes = TrafficBufferService.CalculateBufferMinutes(
                travelProfile.RealisticMinutes,
                realisticLegDeparture,
                bufferProfile.Count == 4 ? bufferProfile[0] : _trafficBufferPercentFrom0500To0730,
                bufferProfile.Count == 4 ? bufferProfile[1] : _trafficBufferPercentFrom0730To0900,
                bufferProfile.Count == 4 ? bufferProfile[2] : _trafficBufferPercentFrom0900To1530,
                bufferProfile.Count == 4 ? bufferProfile[3] : _trafficBufferPercentFrom1530To1830);
            if (trafficBufferMinutes > 0)
            {
                optimisticArrival = optimisticArrival.AddMinutes(trafficBufferMinutes);
                realisticArrival = realisticArrival.AddMinutes(trafficBufferMinutes);
                pessimisticArrival = pessimisticArrival.AddMinutes(trafficBufferMinutes);
            }

            var serviceMinutes = Math.Max(stop.ServiceMinutes, 0);
            var waitMinutes = Math.Max(stop.WaitMinutes, 0);
            var conflictText = string.Empty;
            var hasConflict = false;

            if (TryParseTime(realisticArrival.Date, stop.TimeWindowStart, out var windowStart))
            {
                optimisticArrival = Max(optimisticArrival, windowStart);
                realisticArrival = Max(realisticArrival, windowStart);
                pessimisticArrival = Max(pessimisticArrival, windowStart);
            }
            DateTime? promisedEnd = null;
            if (stop.IsArrivalWindowFixed)
            {
                var promise = ResolveFixedWindow(stop, realisticArrival, start);
                var promisedStart = promise?.Start;
                promisedEnd = promise?.End;
                if (promisedStart.HasValue)
                {
                    if (promisedEnd < promisedStart) promisedEnd = promisedEnd.Value.AddDays(1);
                    optimisticArrival = Max(optimisticArrival, promisedStart.Value);
                    realisticArrival = Max(realisticArrival, promisedStart.Value);
                    pessimisticArrival = Max(pessimisticArrival, promisedStart.Value);
                }
            }
            if (promisedEnd.HasValue && (realisticArrival > promisedEnd.Value || pessimisticArrival > promisedEnd.Value.AddMinutes(PessimisticArrivalToleranceMinutes)))
            {
                hasConflict = true;
                conflictText = realisticArrival > promisedEnd.Value
                    ? $"Ankunft ca. {realisticArrival:HH:mm}, Zeitfenster bis {promisedEnd:HH:mm}."
                    : $"Bei Verzögerung bis {pessimisticArrival:HH:mm}, Zeitfenster bis {promisedEnd:HH:mm}.";
            }

            if (TryParseTime(realisticArrival.Date, stop.TimeWindowStart, out windowStart) &&
                TryParseTime(realisticArrival.Date, stop.TimeWindowEnd, out var windowEnd) &&
                windowStart > windowEnd)
            {
                hasConflict = true;
                conflictText = "Ungültiges Zeitfenster: Start liegt nach Ende.";
            }
            else if (TryParseTime(realisticArrival.Date, stop.TimeWindowEnd, out windowEnd))
            {
                if (realisticArrival > windowEnd)
                {
                    hasConflict = true;
                    conflictText = $"Ankunft ca. {realisticArrival:HH:mm}, Zeitfenster bis {windowEnd:HH:mm}.";
                }
                else if (pessimisticArrival > windowEnd.AddMinutes(PessimisticArrivalToleranceMinutes))
                {
                    hasConflict = true;
                    conflictText = $"Bei Verzögerung Ankunft bis {pessimisticArrival:HH:mm}, Zeitfenster bis {windowEnd:HH:mm}.";
                }
            }

            var optimisticDeparture = optimisticArrival.AddMinutes(serviceMinutes + waitMinutes);
            var realisticDeparture = realisticArrival.AddMinutes(serviceMinutes + waitMinutes);
            var pessimisticDeparture = pessimisticArrival.AddMinutes(serviceMinutes + waitMinutes);

            entries.Add(new TourStopScheduleEntry(
                stop.Id,
                realisticArrival,
                realisticDeparture,
                hasConflict,
                conflictText,
                optimisticArrival,
                pessimisticArrival,
                trafficBufferMinutes));

            optimisticCurrent = optimisticDeparture;
            realisticCurrent = realisticDeparture;
            pessimisticCurrent = pessimisticDeparture;
        }

        return new TourScheduleResult(
            start,
            entries.LastOrDefault()?.Departure ?? start,
            entries,
            optimisticCurrent,
            pessimisticCurrent);
    }

    public TourRecord ApplySchedule(TourRecord tour, int fallbackTravelMinutes = 15)
    {
        var schedule = BuildSchedule(tour, fallbackTravelMinutes);
        var mapped = schedule.Stops.ToDictionary(x => x.StopId, x => x);
        DateTime? previousStart = null;
        DateTime? previousEnd = null;

        foreach (var stop in tour.Stops.OrderBy(x => x.Order > 0 ? x.Order : int.MaxValue).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!mapped.TryGetValue(stop.Id, out var entry))
            {
                continue;
            }

            var displayedRange = TourArrivalDisplayFormatter.BuildDisplayedArrivalRange(
                entry.OptimisticArrival ?? entry.Arrival,
                entry.Arrival,
                entry.PessimisticArrival ?? entry.Arrival);
            var windowStart = displayedRange.Optimistic;
            var windowEnd = displayedRange.Pessimistic;
            var validWindow = true;
            var isCustomer = !TourStopIdentity.IsCompanyStop(stop) &&
                !TourStopIdentity.IsPauseStop(stop) &&
                (!(stop.StopKind == "manual" || stop.Id.StartsWith("manual:", StringComparison.OrdinalIgnoreCase)) || stop.IsArrivalWindowFixed);
            var conflict = entry.ConflictText;
            if (isCustomer && previousStart.HasValue)
            {
                // Keep the earliest possible arrival covered; a backwards start is a conflict.
                if (windowStart < previousStart.Value)
                    conflict = AppendConflict(conflict, "Zeitfenster beginnt vor dem vorherigen Stopp.");
                if (windowEnd < previousEnd)
                    windowEnd = windowStart.AddHours(2);
            }

            if (stop.IsArrivalWindowFixed)
            {
                // Legacy fixed promises have only HH:mm; lift them onto the tour timeline once.
                var promise = ResolveFixedWindow(stop, entry.Arrival, schedule.Start);
                if (promise.HasValue)
                {
                    windowStart = promise.Value.Start;
                    windowEnd = promise.Value.End;
                }
                else
                {
                    validWindow = false;
                    conflict = AppendConflict(conflict, "Fixiertes Zeitfenster fehlt oder ist ungültig.");
                }
            }
            else
            {
                stop.PlannedArrivalOptimistic = windowStart.ToString("HH:mm");
                stop.PlannedArrivalPessimistic = windowEnd.ToString("HH:mm");
            }
            if (isCustomer)
            {
                if (windowStart > entry.OptimisticArrival || windowEnd < entry.Arrival ||
                    windowEnd.AddMinutes(PessimisticArrivalToleranceMinutes) < entry.PessimisticArrival)
                {
                    if (string.IsNullOrWhiteSpace(conflict))
                        conflict = $"Zeitfenster {windowStart:HH:mm}–{windowEnd:HH:mm} prüfen (Ankunft ca. {entry.Arrival:HH:mm}).";
                }
                if ((windowEnd - windowStart).TotalMinutes > 120)
                    conflict = AppendConflict(conflict, "Zeitfenster überschreitet zwei Stunden.");
                if (previousEnd.HasValue && (windowEnd < previousEnd || windowStart < previousStart))
                    conflict = AppendConflict(conflict, "Zeitfenster widerspricht der Stoppreihenfolge.");
                previousStart = windowStart;
                previousEnd = windowEnd;
            }
            stop.CalculatedArrivalOptimistic = entry.OptimisticArrival;
            stop.CalculatedArrival = entry.Arrival;
            stop.CalculatedArrivalPessimistic = entry.PessimisticArrival;
            stop.CalculatedDeparture = entry.Departure;
            stop.CalculatedTrafficBufferMinutes = entry.TrafficBufferMinutes;
            // Never turn a missing fixed promise into an apparently valid calculated window.
            stop.ArrivalWindowStart = validWindow ? windowStart : null;
            stop.ArrivalWindowEnd = validWindow ? windowEnd : null;
            stop.PlannedArrival = entry.Arrival.ToString("HH:mm");
            stop.PlannedDeparture = entry.Departure.ToString("HH:mm");
            stop.ScheduleConflict = entry.HasConflict || !string.IsNullOrWhiteSpace(conflict);
            stop.ScheduleConflictText = conflict;
        }
        tour.ScheduleVersion = CurrentVersion;
        tour.ScheduleCalculatedAtUtc = DateTimeOffset.UtcNow;
        if (tour.ScheduleTrafficBufferPercentages.Count != 4)
            tour.ScheduleTrafficBufferPercentages = [_trafficBufferPercentFrom0500To0730, _trafficBufferPercentFrom0730To0900,
                _trafficBufferPercentFrom0900To1530, _trafficBufferPercentFrom1530To1830];
        return tour;
    }

    private static string AppendConflict(string current, string message) =>
        string.IsNullOrWhiteSpace(current) ? message : current + " " + message;

    private static (DateTime Start, DateTime End)? ResolveFixedWindow(TourStopRecord stop, DateTime arrival, DateTime tourStart)
    {
        if (stop.ArrivalWindowStart.HasValue && stop.ArrivalWindowEnd.HasValue)
            return (stop.ArrivalWindowStart.Value, stop.ArrivalWindowEnd.Value);
        if (!TryParseTime(arrival.Date, stop.PlannedArrivalOptimistic, out var start) ||
            !TryParseTime(arrival.Date, stop.PlannedArrivalPessimistic, out var end)) return null;
        if (end < start)
        {
            // A legacy 23:45-00:45 promise belongs to the preceding evening after midnight,
            // rather than making the driver wait another 24 hours.
            if (arrival.TimeOfDay <= end.TimeOfDay ||
                (arrival.Date > tourStart.Date && arrival.TimeOfDay < start.TimeOfDay))
                start = start.AddDays(-1);
            else end = end.AddDays(1);
        }
        return (start, end);
    }

    // Reading a persisted plan must never use workplace settings or reinterpret its tolerance.
    public static TourScheduleResult ReadSchedule(TourRecord tour)
    {
        var start = ResolveStartDateTime(tour);
        var current = start;
        var entries = new List<TourStopScheduleEntry>();
        foreach (var stop in tour.Stops.OrderBy(x => x.Order))
        {
            var arrival = stop.CalculatedArrival;
            if (!arrival.HasValue)
            {
                if (!TryParseTime(current.Date, stop.PlannedArrival, out var parsed)) parsed = current;
                if (parsed < current) parsed = parsed.AddDays(1);
                arrival = parsed;
            }
            var departure = stop.CalculatedDeparture;
            if (!departure.HasValue)
            {
                if (!TryParseTime(arrival.Value.Date, stop.PlannedDeparture, out var parsed)) parsed = arrival.Value;
                if (parsed < arrival.Value) parsed = parsed.AddDays(1);
                departure = parsed;
            }
            entries.Add(new(stop.Id, arrival.Value, departure.Value, stop.ScheduleConflict, stop.ScheduleConflictText,
                stop.CalculatedArrivalOptimistic ?? stop.ArrivalWindowStart ?? ReadLegacyWindowTime(stop.PlannedArrivalOptimistic, arrival.Value),
                stop.CalculatedArrivalPessimistic ?? stop.ArrivalWindowEnd ?? ReadLegacyWindowTime(stop.PlannedArrivalPessimistic, arrival.Value),
                stop.CalculatedTrafficBufferMinutes));
            current = departure.Value;
        }
        var last = entries.LastOrDefault();
        var lastStay = last is null ? TimeSpan.Zero : last.Departure - last.Arrival;
        return new(start, current, entries, last?.OptimisticArrival + lastStay, last?.PessimisticArrival + lastStay);
    }

    private static DateTime ReadLegacyWindowTime(string text, DateTime arrival)
    {
        if (!TryParseTime(arrival.Date, text, out var value)) return arrival;
        if (value - arrival > TimeSpan.FromHours(12)) value = value.AddDays(-1);
        else if (arrival - value > TimeSpan.FromHours(12)) value = value.AddDays(1);
        return value;
    }

    private static DateTime ResolveStartDateTime(TourRecord tour)
    {
        var date = ParseDate(tour.Date) ?? DateTime.Today;
        if (!TryParseTime(date, tour.StartTime, out var start))
        {
            start = new DateTime(date.Year, date.Month, date.Day, 8, 0, 0);
        }

        return start;
    }

    private static TourTravelTimeProfile ResolveTravelTimeProfile(
        IReadOnlyDictionary<string, TourTravelTimeProfile> profileCache,
        IReadOnlyDictionary<string, int> cache,
        IReadOnlyList<TourStopRecord> stops,
        int currentIndex,
        int fallbackTravelMinutes)
    {
        if (currentIndex <= 0)
        {
            return new TourTravelTimeProfile();
        }

        var previous = stops[currentIndex - 1];
        var current = stops[currentIndex];
        var keys = new[]
        {
            $"{previous.Id}|{current.Id}",
            $"{previous.Id}->{current.Id}",
            $"{previous.Id}:{current.Id}",
            $"{currentIndex - 1}-{currentIndex}"
        };

        foreach (var key in keys)
        {
            if (profileCache.TryGetValue(key, out var profile) && profile is not null)
            {
                return new TourTravelTimeProfile
                {
                    OptimisticMinutes = Math.Max(0, profile.OptimisticMinutes),
                    RealisticMinutes = Math.Max(0, profile.RealisticMinutes),
                    PessimisticMinutes = Math.Max(0, profile.PessimisticMinutes)
                };
            }

            if (cache.TryGetValue(key, out var value))
            {
                var normalized = Math.Max(0, value);
                return new TourTravelTimeProfile
                {
                    OptimisticMinutes = normalized,
                    RealisticMinutes = normalized,
                    PessimisticMinutes = normalized
                };
            }
        }

        var fallback = Math.Max(0, fallbackTravelMinutes);
        return new TourTravelTimeProfile
        {
            OptimisticMinutes = fallback,
            RealisticMinutes = fallback,
            PessimisticMinutes = fallback
        };
    }

    private static bool TryParseTime(DateTime date, string? text, out DateTime value)
    {
        if (TimeSpan.TryParseExact(text?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var time))
        {
            value = date.Date.Add(time);
            return true;
        }

        value = date;
        return false;
    }

    private static DateTime? ParseDate(string? text)
    {
        var raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var format in DateFormats)
        {
            if (DateTime.TryParseExact(raw, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            {
                return value.Date;
            }
        }

        return null;
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;
}
