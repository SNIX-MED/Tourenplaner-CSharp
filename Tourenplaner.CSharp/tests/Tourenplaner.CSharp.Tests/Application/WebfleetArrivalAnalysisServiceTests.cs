using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebfleetArrivalAnalysisServiceTests
{
    [Fact]
    public void Analyze_DetectsFirstTrackEntryAndEvaluatesArrivalWindow()
    {
        var date = new DateOnly(2026, 10, 2);
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord
                {
                    Id = "A", Name = "Kunde A", Order = 1, Lat = 47.3769, Lon = 8.5417,
                    PlannedArrivalOptimistic = "08:00", PlannedArrivalPessimistic = "09:00"
                }
            ]
        };
        var track = new[]
        {
            Point(date, 7, 50, 47.3700, 8.5300),
            Point(date, 8, 32, 47.37691, 8.54171),
            Point(date, 8, 35, 47.37692, 8.54172)
        };

        var result = Assert.Single(WebfleetArrivalAnalysisService.Analyze(tour, track, date));

        Assert.Equal("08:32", result.ActualArrival?.ToString("HH:mm"));
        Assert.True(result.ArrivedWithinWindow);
    }

    [Fact]
    public void Analyze_FlagsArrivalOutsideWindowAndIgnoresCompanyAndPauseStops()
    {
        var date = new DateOnly(2026, 10, 2);
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord { Id = "company:start", Lat = 47, Lon = 8 },
                new TourStopRecord { Id = "pause", StopKind = "pause", Lat = 47, Lon = 8 },
                new TourStopRecord
                {
                    Id = "B", Name = "Kunde B", Order = 3, Lat = 47.4, Lon = 8.6,
                    PlannedArrivalOptimistic = "10:00", PlannedArrivalPessimistic = "11:00"
                }
            ]
        };

        var result = Assert.Single(WebfleetArrivalAnalysisService.Analyze(
            tour, [Point(date, 11, 20, 47.4, 8.6)], date));

        Assert.False(result.ArrivedWithinWindow);
        Assert.Contains("0/1", WebfleetArrivalAnalysisService.BuildSummary([result]));
    }

    [Fact]
    public void BuildDisplayModel_FormatsRowsAndExplainsLateArrival()
    {
        var date = new DateOnly(2026, 10, 2);
        var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Local));
        var end = start.AddHours(1);
        var results = new[]
        {
            new WebfleetStopArrivalAnalysis("A", "Kunde A", start.AddMinutes(30), start, end),
            new WebfleetStopArrivalAnalysis("B", "Kunde B", end.AddMinutes(2), start, end)
        };

        var display = WebfleetArrivalAnalysisService.BuildDisplayModel(results);

        Assert.Equal(1, display.WithinCount);
        Assert.Equal(1, display.OutsideCount);
        Assert.Equal("10:00–11:00", display.Rows[1].TimeWindow);
        Assert.Equal("11:02", display.Rows[1].Arrival);
        Assert.Equal("outside", display.Rows[1].Status);
        Assert.Equal("2 Min. zu spät", display.Rows[1].StatusText);
    }

    [Fact]
    public void BuildDisplayModel_FormatsUtcArrivalInTourWindowOffset()
    {
        var windowStart = new DateTimeOffset(2026, 10, 2, 10, 30, 0, TimeSpan.FromHours(2));
        var result = new WebfleetStopArrivalAnalysis(
            "A",
            "Kunde A",
            new DateTimeOffset(2026, 10, 2, 8, 31, 0, TimeSpan.Zero),
            windowStart,
            windowStart.AddHours(1));

        var display = WebfleetArrivalAnalysisService.BuildDisplayModel([result]);

        Assert.Equal("10:31", display.Rows[0].Arrival);
        Assert.Equal("within", display.Rows[0].Status);
    }

    private static WebfleetTrackPoint Point(DateOnly date, int hour, int minute, double lat, double lon) =>
        new(new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Local)), lat, lon, 0, 0);
}
