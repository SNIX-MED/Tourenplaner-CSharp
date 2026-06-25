using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class TomTomTrafficSeverityClassifierTests
{
    [Fact]
    public void ResolveTrafficLevel_MapsSlowCategoryBySeverityMode()
    {
        var standard = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            "slow",
            delayInSeconds: null,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.Standard);
        var slightlyStricter = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            "slow",
            delayInSeconds: null,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.SlightlyStricter);
        var strict = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            "slow",
            delayInSeconds: null,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.Strict);

        Assert.Equal("light", standard);
        Assert.Equal("moderate", slightlyStricter);
        Assert.Equal("heavy", strict);
    }

    [Fact]
    public void ResolveTrafficLevel_UsesStricterDelayThresholds()
    {
        var standard = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: 70d,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.Standard);
        var slightlyStricter = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: 70d,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.SlightlyStricter);
        var strict = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: 70d,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.Strict);

        Assert.Equal("light", standard);
        Assert.Equal("moderate", slightlyStricter);
        Assert.Equal("moderate", strict);
    }

    [Fact]
    public void ResolveTrafficLevel_UsesSeverityModeForMagnitudeOfDelay()
    {
        var standard = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: null,
            magnitudeOfDelay: 2,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.Standard);
        var slightlyStricter = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: null,
            magnitudeOfDelay: 2,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.SlightlyStricter);
        var strict = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: null,
            magnitudeOfDelay: 2,
            effectiveSpeedInKmh: null,
            TomTomTrafficSeverityMode.Strict);

        Assert.Equal("heavy", standard);
        Assert.Equal("moderate", slightlyStricter);
        Assert.Equal("blocked", strict);
    }

    [Fact]
    public void ResolveTrafficLevel_UsesSeverityModeForEffectiveSpeedFallback()
    {
        var standard = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: null,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: 28d,
            TomTomTrafficSeverityMode.Standard);
        var slightlyStricter = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: null,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: 28d,
            TomTomTrafficSeverityMode.SlightlyStricter);
        var strict = TomTomTrafficSeverityClassifier.ResolveTrafficLevel(
            simpleCategory: null,
            delayInSeconds: null,
            magnitudeOfDelay: null,
            effectiveSpeedInKmh: 28d,
            TomTomTrafficSeverityMode.Strict);

        Assert.Equal("moderate", standard);
        Assert.Equal("moderate", slightlyStricter);
        Assert.Equal("heavy", strict);
    }
}
