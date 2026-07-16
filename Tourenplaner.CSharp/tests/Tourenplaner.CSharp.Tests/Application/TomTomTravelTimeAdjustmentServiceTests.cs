using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class TomTomTravelTimeAdjustmentServiceTests
{
    [Fact]
    public void ApplySeverityMode_LeavesTravelTimesUnchangedForStandard()
    {
        var adjusted = TomTomTravelTimeAdjustmentService.ApplySeverityMode(
            optimisticSeconds: 5_400,
            realisticSeconds: 6_300,
            pessimisticSeconds: 7_200,
            noTrafficDelaySeconds: 900,
            historicDelaySeconds: 300,
            liveIncidentDelaySeconds: 120,
            TomTomTrafficSeverityMode.Standard);

        Assert.Equal(5_400, adjusted.OptimisticSeconds);
        Assert.Equal(6_300, adjusted.RealisticSeconds);
        Assert.Equal(7_200, adjusted.PessimisticSeconds);
    }

    [Fact]
    public void ApplySeverityMode_IncreasesTravelTimesForSlightlyStricter()
    {
        var adjusted = TomTomTravelTimeAdjustmentService.ApplySeverityMode(
            optimisticSeconds: 5_400,
            realisticSeconds: 6_300,
            pessimisticSeconds: 7_200,
            noTrafficDelaySeconds: 900,
            historicDelaySeconds: 300,
            liveIncidentDelaySeconds: 120,
            TomTomTrafficSeverityMode.SlightlyStricter);

        Assert.True(adjusted.OptimisticSeconds > 5_400);
        Assert.True(adjusted.RealisticSeconds > 6_300);
        Assert.True(adjusted.PessimisticSeconds > 7_200);
    }

    [Fact]
    public void ApplySeverityMode_IncreasesTravelTimesMoreForStrictThanSlightlyStricter()
    {
        var slightlyStricter = TomTomTravelTimeAdjustmentService.ApplySeverityMode(
            optimisticSeconds: 5_400,
            realisticSeconds: 6_300,
            pessimisticSeconds: 7_200,
            noTrafficDelaySeconds: 900,
            historicDelaySeconds: 300,
            liveIncidentDelaySeconds: 120,
            TomTomTrafficSeverityMode.SlightlyStricter);
        var strict = TomTomTravelTimeAdjustmentService.ApplySeverityMode(
            optimisticSeconds: 5_400,
            realisticSeconds: 6_300,
            pessimisticSeconds: 7_200,
            noTrafficDelaySeconds: 900,
            historicDelaySeconds: 300,
            liveIncidentDelaySeconds: 120,
            TomTomTrafficSeverityMode.Strict);

        Assert.True(strict.OptimisticSeconds > slightlyStricter.OptimisticSeconds);
        Assert.True(strict.RealisticSeconds > slightlyStricter.RealisticSeconds);
        Assert.True(strict.PessimisticSeconds > slightlyStricter.PessimisticSeconds);
    }

    [Fact]
    public void ApplySeverityMode_DoesNotAddExtraWithoutObservedDelay()
    {
        var slightlyStricter = TomTomTravelTimeAdjustmentService.ApplySeverityMode(
            optimisticSeconds: 2_400,
            realisticSeconds: 2_700,
            pessimisticSeconds: 3_000,
            noTrafficDelaySeconds: 0,
            historicDelaySeconds: 0,
            liveIncidentDelaySeconds: 0,
            TomTomTrafficSeverityMode.SlightlyStricter);

        Assert.Equal(2_400, slightlyStricter.OptimisticSeconds);
        Assert.Equal(2_700, slightlyStricter.RealisticSeconds);
        Assert.Equal(3_000, slightlyStricter.PessimisticSeconds);
    }
}
