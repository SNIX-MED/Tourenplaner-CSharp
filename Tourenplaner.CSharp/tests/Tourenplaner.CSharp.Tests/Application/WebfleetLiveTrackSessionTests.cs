using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebfleetLiveTrackSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static DateOnly Today => DateOnly.FromDateTime(Now.LocalDateTime);
    private static WebfleetVehicleSnapshot Vehicle(DateTimeOffset? time, string uid = "truck", double lat = 47) =>
        new("1", uid, "Truck", "", lat, 8, time, "", "", "");
    private static WebfleetTrackPoint Point(int minutes, double lat = 47) => new(Now.AddMinutes(minutes), lat, 8, 30, 90);

    [Fact]
    public void LiveFixExtendsTrackWithoutAnotherHistoryRequest()
    {
        var session = new WebfleetLiveTrackSession();
        var generation = session.Begin("truck", Today, Now);
        session.MergeHistory(generation, [Point(-2)]);
        Assert.True(session.Append(Vehicle(Now.AddMinutes(-1)), Now));
        Assert.Equal(2, session.Points.Count);
        Assert.False(session.TryBeginHistoryRefresh(Now.AddSeconds(30)));
    }

    [Fact]
    public void RejectsDuplicateOlderMissingFutureInvalidAndWrongVehicleFixes()
    {
        var session = new WebfleetLiveTrackSession();
        var generation = session.Begin("truck", Today, Now);
        session.MergeHistory(generation, [Point(-1)]);
        Assert.False(session.Append(Vehicle(Now.AddMinutes(-1)), Now));
        Assert.False(session.Append(Vehicle(Now.AddMinutes(-2)), Now));
        Assert.False(session.Append(Vehicle(null), Now));
        Assert.False(session.Append(Vehicle(Now.AddSeconds(1)), Now));
        Assert.False(session.Append(Vehicle(Now, lat: double.NaN), Now));
        Assert.False(session.Append(Vehicle(Now, "other"), Now));
        Assert.False(session.Append(Vehicle(Now.AddDays(-1)), Now));
        Assert.Single(session.Points);
    }

    [Fact]
    public void HistoricalDaysAndMidnightStopLiveUpdates()
    {
        var session = new WebfleetLiveTrackSession();
        session.Begin("truck", Today.AddDays(-1), Now);
        Assert.False(session.Append(Vehicle(Now.AddDays(-1)), Now));
        Assert.False(session.TryBeginHistoryRefresh(Now.AddMinutes(6)));
        session.Begin("truck", Today, Now);
        Assert.False(session.Append(Vehicle(Now.AddDays(1)), Now.AddDays(1)));
        Assert.False(session.TryBeginHistoryRefresh(Now.AddDays(1)));
    }

    [Fact]
    public void HistoryReplacesProvisionalFixesButKeepsNewerLiveTail()
    {
        var session = new WebfleetLiveTrackSession();
        var generation = session.Begin("truck", Today, Now);
        session.MergeHistory(generation, [Point(-3)]);
        session.Append(Vehicle(Now.AddMinutes(-2), lat: 48), Now);
        session.Append(Vehicle(Now), Now);
        Assert.True(session.MergeHistory(generation, [Point(-1), Point(-3), Point(-2), Point(-2)]));
        Assert.Equal(new[] { Now.AddMinutes(-3), Now.AddMinutes(-2), Now.AddMinutes(-1), Now }, session.Points.Select(p => p.PositionTime));
        Assert.Equal(47, session.Points[1].Latitude);
        session.MergeHistory(generation, []);
        Assert.Equal(4, session.Points.Count);
    }

    [Fact]
    public void PartialHistoryDoesNotEraseTheEarlierTrack()
    {
        var session = new WebfleetLiveTrackSession();
        var generation = session.Begin("truck", Today, Now);
        session.MergeHistory(generation, [Point(-10), Point(-5), Point(-2)]);
        session.Append(Vehicle(Now), Now);
        session.MergeHistory(generation, [Point(-3), Point(-1)]);
        Assert.Equal(new[] { Now.AddMinutes(-10), Now.AddMinutes(-5), Now.AddMinutes(-3), Now.AddMinutes(-1), Now }, session.Points.Select(p => p.PositionTime));
    }

    [Fact]
    public void LateResponsesCannotRestoreClearedOrSwitchedTracks()
    {
        var session = new WebfleetLiveTrackSession();
        var first = session.Begin("truck", Today, Now);
        session.Clear();
        Assert.False(session.MergeHistory(first, [Point(-1)]));
        var second = session.Begin("other", Today, Now);
        Assert.False(session.MergeHistory(first, [Point(-1)]));
        Assert.True(session.MergeHistory(second, [Point(-2)]));
    }

    [Fact]
    public void HistoryRefreshIsThrottledEvenAfterFailure()
    {
        var session = new WebfleetLiveTrackSession();
        session.Begin("truck", Today, Now);
        Assert.False(session.TryBeginHistoryRefresh(Now.AddMinutes(4)));
        Assert.True(session.TryBeginHistoryRefresh(Now.AddMinutes(5)));
        Assert.False(session.TryBeginHistoryRefresh(Now.AddMinutes(5).AddSeconds(30)));
        Assert.True(session.TryBeginHistoryRefresh(Now.AddMinutes(10)));
    }
}
