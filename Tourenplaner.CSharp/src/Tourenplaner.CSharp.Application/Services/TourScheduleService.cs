using System.Globalization;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class TourScheduleService
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];

    public TourScheduleResult BuildSchedule(TourRecord tour, int fallbackTravelMinutes = 15)
    {
        var start = ResolveStartDateTime(tour);
        var entries = new List<TourStopScheduleEntry>();
        var current = start;

        var orderedStops = (tour.Stops ?? [])
            .OrderBy(s => s.Order > 0 ? s.Order : int.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < orderedStops.Count; i++)
        {
            var stop = orderedStops[i];
            var travelMinutes = ResolveTravelMinutes(tour.TravelTimeCache, orderedStops, i, fallbackTravelMinutes);
            var arrival = current.AddMinutes(travelMinutes);

            var serviceMinutes = Math.Max(stop.ServiceMinutes, 0);
            var waitMinutes = Math.Max(stop.WaitMinutes, 0);
            var conflictText = string.Empty;
            var hasConflict = false;

            if (TryParseTime(arrival.Date, stop.TimeWindowStart, out var windowStart) && arrival < windowStart)
            {
                arrival = windowStart;
            }

            if (TryParseTime(arrival.Date, stop.TimeWindowStart, out windowStart) &&
                TryParseTime(arrival.Date, stop.TimeWindowEnd, out var windowEnd) &&
                windowStart > windowEnd)
            {
                hasConflict = true;
                conflictText = "Ungueltiges Zeitfenster: Start liegt nach Ende.";
            }
            else if (TryParseTime(arrival.Date, stop.TimeWindowEnd, out windowEnd) && arrival > windowEnd)
            {
                hasConflict = true;
                conflictText = $"Ankunft {arrival:HH:mm} ausserhalb Zeitfenster-Ende {windowEnd:HH:mm}.";
            }

            var departure = arrival.AddMinutes(serviceMinutes + waitMinutes);
            entries.Add(new TourStopScheduleEntry(
                stop.Id,
                arrival,
                departure,
                hasConflict,
                conflictText));

            current = departure;
        }

        return new TourScheduleResult(start, entries.LastOrDefault()?.Departure ?? start, entries);
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

            stop.PlannedArrival = entry.Arrival.ToString("HH:mm");
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

    private static int ResolveTravelMinutes(
        IReadOnlyDictionary<string, int> cache,
        IReadOnlyList<TourStopRecord> stops,
        int currentIndex,
        int fallbackTravelMinutes)
    {
        if (currentIndex <= 0)
        {
            return 0;
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
            if (cache.TryGetValue(key, out var value))
            {
                return Math.Max(0, value);
            }
        }

        return Math.Max(0, fallbackTravelMinutes);
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
}
