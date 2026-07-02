using System.Globalization;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class TourScheduleService
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];
    private const int MaxArrivalRangeMinutes = 120;
    private const int MaxEarlyArrivalSlackMinutes = 15;
    private const double CarriedUncertaintyFactor = 0.6d;
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
            var legEarlySlackMinutes = Math.Max(0, travelProfile.RealisticMinutes - travelProfile.OptimisticMinutes);
            var legLateSlackMinutes = Math.Max(0, travelProfile.PessimisticMinutes - travelProfile.RealisticMinutes);
            var carriedUncertaintyMinutes = Math.Max(0, (int)Math.Round((pessimisticCurrent - optimisticCurrent).TotalMinutes));
            var optimisticArrival = realisticArrival.AddMinutes(-Math.Min(MaxEarlyArrivalSlackMinutes, legEarlySlackMinutes));
            var pessimisticArrival = realisticArrival.AddMinutes(Math.Min(
                MaxArrivalRangeMinutes,
                legLateSlackMinutes + (int)Math.Round(carriedUncertaintyMinutes * CarriedUncertaintyFactor, MidpointRounding.AwayFromZero)));
            var trafficBufferMinutes = TrafficBufferService.CalculateBufferMinutes(
                travelProfile.RealisticMinutes,
                realisticLegDeparture,
                _trafficBufferPercentFrom0500To0730,
                _trafficBufferPercentFrom0730To0900,
                _trafficBufferPercentFrom0900To1530,
                _trafficBufferPercentFrom1530To1830);
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
                    conflictText = $"Ankunft {realisticArrival:HH:mm} ausserhalb Zeitfenster-Ende {windowEnd:HH:mm}.";
                }
                else if (pessimisticArrival > windowEnd)
                {
                    hasConflict = true;
                    conflictText = $"Pessimistische Ankunft {pessimisticArrival:HH:mm} ausserhalb Zeitfenster-Ende {windowEnd:HH:mm}.";
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
                pessimisticArrival));

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

        foreach (var stop in tour.Stops)
        {
            if (!mapped.TryGetValue(stop.Id, out var entry))
            {
                continue;
            }

            var displayedRange = TourArrivalDisplayFormatter.BuildDisplayedArrivalRange(
                entry.OptimisticArrival ?? entry.Arrival,
                entry.Arrival,
                entry.PessimisticArrival ?? entry.Arrival);

            stop.PlannedArrivalOptimistic = displayedRange.Optimistic.ToString("HH:mm");
            stop.PlannedArrival = entry.Arrival.ToString("HH:mm");
            stop.PlannedArrivalPessimistic = displayedRange.Pessimistic.ToString("HH:mm");
            stop.PlannedDeparture = entry.Departure.ToString("HH:mm");
            stop.ScheduleConflict = entry.HasConflict;
            stop.ScheduleConflictText = entry.ConflictText;
        }

        return tour;
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
