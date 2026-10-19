namespace Tourenplaner.CSharp.App.Services;

/// <summary>Combines today's live fixes with the periodically reconciled recorded track.</summary>
public sealed class WebfleetLiveTrackSession
{
    public List<WebfleetTrackPoint> Points { get; } = new();
    public string ObjectUid { get; private set; } = string.Empty;
    public DateOnly Date { get; private set; }
    public int Generation { get; private set; }
    private DateTimeOffset _lastHistoryAttempt;

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
            !vehicle.PositionTime.HasValue || !vehicle.Latitude.HasValue || !vehicle.Longitude.HasValue) return false;
        var point = new WebfleetTrackPoint(vehicle.PositionTime.Value, vehicle.Latitude.Value, vehicle.Longitude.Value, null, null);
        if (!IsValid(point) || point.PositionTime > now ||
            (Points.Count > 0 && point.PositionTime <= Points[^1].PositionTime)) return false;
        Points.Add(point);
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
        return true;
    }
}
