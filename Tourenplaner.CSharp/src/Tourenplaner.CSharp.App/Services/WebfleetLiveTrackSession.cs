namespace Tourenplaner.CSharp.App.Services;

/// <summary>Combines today's live fixes with the periodically reconciled recorded track.</summary>
public sealed class WebfleetLiveTrackSession
{
    public List<WebfleetTrackPoint> Points { get; } = new();
    public string ObjectUid { get; private set; } = string.Empty;
    public DateOnly Date { get; private set; }
    public int Generation { get; private set; }
    private DateTimeOffset _lastHistoryAttempt;
    private readonly HashSet<DateTimeOffset> _provisionalTimes = new();

    public int Begin(string objectUid, DateOnly date, DateTimeOffset now)
    {
        Clear();
        ObjectUid = objectUid;
        Date = date;
        _lastHistoryAttempt = now;
        return Generation;
    }

    public void Clear()
    {
        Generation++;
        ObjectUid = string.Empty;
        Points.Clear();
        _provisionalTimes.Clear();
    }

    public bool IsToday(DateTimeOffset now) => ObjectUid.Length > 0 && Date == DateOnly.FromDateTime(now.LocalDateTime);

    public bool TryBeginHistoryRefresh(DateTimeOffset now)
    {
        if (!IsToday(now) || now - _lastHistoryAttempt < TimeSpan.FromMinutes(5)) return false;
        _lastHistoryAttempt = now; // Also throttle failures; never retry every vehicle poll.
        return true;
    }

    private bool IsValid(WebfleetTrackPoint point) =>
        double.IsFinite(point.Latitude) && point.Latitude is >= -90 and <= 90 &&
        double.IsFinite(point.Longitude) && point.Longitude is >= -180 and <= 180 &&
        DateOnly.FromDateTime(point.PositionTime.LocalDateTime) == Date;

    public bool Append(WebfleetVehicleSnapshot vehicle, DateTimeOffset now)
    {
        if (!IsToday(now) || !string.Equals(ObjectUid, vehicle.ObjectUid, StringComparison.OrdinalIgnoreCase) ||
            !vehicle.Latitude.HasValue || !vehicle.Longitude.HasValue) return false;
        // Some Work App objects provide coordinates without a position timestamp.
        // Use the observation time only for changed fixes; recorded history reconciles these later.
        if (!vehicle.PositionTime.HasValue && Points.Count > 0 &&
            Points[^1].Latitude == vehicle.Latitude.Value && Points[^1].Longitude == vehicle.Longitude.Value)
            return false;
        var point = new WebfleetTrackPoint(vehicle.PositionTime ?? now, vehicle.Latitude.Value, vehicle.Longitude.Value, null, null);
        if (!IsValid(point) || point.PositionTime > now) return false;
        if (vehicle.PositionTime.HasValue)
        {
            // A real fix may arrive later with a timestamp before our observation time.
            // Do not let provisional points block or reverse the authoritative track.
            var lastRecorded = Points.LastOrDefault(p => !_provisionalTimes.Contains(p.PositionTime));
            if (lastRecorded is not null && point.PositionTime <= lastRecorded.PositionTime) return false;
            Points.RemoveAll(p => _provisionalTimes.Contains(p.PositionTime) && p.PositionTime >= point.PositionTime);
            _provisionalTimes.RemoveWhere(time => time >= point.PositionTime);
        }
        if (Points.Count > 0 && point.PositionTime <= Points[^1].PositionTime) return false;
        Points.Add(point);
        if (!vehicle.PositionTime.HasValue) _provisionalTimes.Add(point.PositionTime);
        return true;
    }

    public bool MergeHistory(int generation, IEnumerable<WebfleetTrackPoint> history)
    {
        if (generation != Generation || ObjectUid.Length == 0) return false;
        var recorded = history.Where(IsValid).OrderBy(p => p.PositionTime).DistinctBy(p => p.PositionTime).ToList();
        // History wins within the returned interval. Preserve the prefix if the server returns a partial day.
        var prefix = recorded.Count == 0 ? new List<WebfleetTrackPoint>() : Points.Where(p => p.PositionTime < recorded[0].PositionTime).ToList();
        var tail = recorded.Count == 0 ? Points.ToList() : Points.Where(p => p.PositionTime > recorded[^1].PositionTime).ToList();
        Points.Clear();
        Points.AddRange(prefix);
        Points.AddRange(recorded);
        Points.AddRange(tail);
        _provisionalTimes.ExceptWith(recorded.Select(p => p.PositionTime));
        _provisionalTimes.IntersectWith(Points.Select(p => p.PositionTime));
        return true;
    }
}
