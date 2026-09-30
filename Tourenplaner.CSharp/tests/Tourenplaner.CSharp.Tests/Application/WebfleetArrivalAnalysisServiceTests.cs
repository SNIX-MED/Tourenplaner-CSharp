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

    private static WebfleetTrackPoint Point(DateOnly date, int hour, int minute, double lat, double lon) =>
        new(new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Local)), lat, lon, 0, 0);
}
