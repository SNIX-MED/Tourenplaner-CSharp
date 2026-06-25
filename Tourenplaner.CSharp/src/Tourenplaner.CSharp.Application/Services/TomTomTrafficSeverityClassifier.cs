namespace Tourenplaner.CSharp.Application.Services;

public enum TomTomTrafficSeverityMode
{
    Standard = 0,
    SlightlyStricter = 1,
    Strict = 2
}

public static class TomTomTrafficSeverityClassifier
{
    public static string ResolveTrafficLevel(
        string? simpleCategory,
        double? delayInSeconds,
        int? magnitudeOfDelay,
        double? effectiveSpeedInKmh,
        TomTomTrafficSeverityMode severityMode)
    {
        var trafficLevel = ResolveFromCategory(simpleCategory, severityMode);
        if (!string.Equals(trafficLevel, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return trafficLevel;
        }

        trafficLevel = ResolveFromDelay(delayInSeconds, severityMode);
        if (!string.Equals(trafficLevel, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return trafficLevel;
        }

        trafficLevel = ResolveFromMagnitude(magnitudeOfDelay, severityMode);
        if (!string.Equals(trafficLevel, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return trafficLevel;
        }

        return ResolveFromSpeed(effectiveSpeedInKmh, severityMode);
    }

    private static string ResolveFromCategory(string? simpleCategory, TomTomTrafficSeverityMode severityMode)
    {
        var normalizedCategory = NormalizeCategory(simpleCategory);
        if (string.IsNullOrWhiteSpace(normalizedCategory))
        {
            return "unknown";
        }

        return normalizedCategory switch
        {
            "roadclosure" or "closed" or "jam" or "jammed" or "stationary" or "heavilycongested" => "blocked",
            "congested" or "stopandgo" => "heavy",
            "slow" => severityMode switch
            {
                TomTomTrafficSeverityMode.Strict => "heavy",
                TomTomTrafficSeverityMode.SlightlyStricter => "moderate",
                _ => "light"
            },
            "freeflow" or "open" => "freeFlow",
            "none" => "unknown",
            _ => "unknown"
        };
    }

    private static string ResolveFromDelay(double? delayInSeconds, TomTomTrafficSeverityMode severityMode)
    {
        if (!delayInSeconds.HasValue)
        {
            return "unknown";
        }

        var thresholds = severityMode switch
        {
            TomTomTrafficSeverityMode.Strict => new DelayThresholds(10d, 45d, 120d, 240d),
            TomTomTrafficSeverityMode.SlightlyStricter => new DelayThresholds(15d, 60d, 180d, 300d),
            _ => new DelayThresholds(20d, 90d, 240d, 420d)
        };

        var value = delayInSeconds.Value;
        if (value <= thresholds.FreeFlowMaxSeconds)
        {
            return "freeFlow";
        }

        if (value <= thresholds.LightMaxSeconds)
        {
            return "light";
        }

        if (value <= thresholds.ModerateMaxSeconds)
        {
            return "moderate";
        }

        return value <= thresholds.HeavyMaxSeconds ? "heavy" : "blocked";
    }

    private static string ResolveFromMagnitude(int? magnitudeOfDelay, TomTomTrafficSeverityMode severityMode)
    {
        if (!magnitudeOfDelay.HasValue)
        {
            return "unknown";
        }

        return severityMode switch
        {
            TomTomTrafficSeverityMode.Strict => magnitudeOfDelay.Value switch
            {
                <= 0 => "freeFlow",
                1 => "moderate",
                2 => "blocked",
                _ => "blocked"
            },
            TomTomTrafficSeverityMode.SlightlyStricter => magnitudeOfDelay.Value switch
            {
                <= 0 => "freeFlow",
                1 => "light",
                2 => "moderate",
                3 => "blocked",
                _ => "blocked"
            },
            _ => magnitudeOfDelay.Value switch
            {
                <= 0 => "freeFlow",
                1 => "light",
                2 => "heavy",
                3 => "blocked",
                _ => "blocked"
            }
        };
    }

    private static string ResolveFromSpeed(double? effectiveSpeedInKmh, TomTomTrafficSeverityMode severityMode)
    {
        if (!effectiveSpeedInKmh.HasValue)
        {
            return "unknown";
        }

        var thresholds = severityMode switch
        {
            TomTomTrafficSeverityMode.Strict => new SpeedThresholds(80d, 50d, 30d),
            TomTomTrafficSeverityMode.SlightlyStricter => new SpeedThresholds(75d, 45d, 25d),
            _ => new SpeedThresholds(70d, 40d, 20d)
        };

        var value = effectiveSpeedInKmh.Value;
        if (value >= thresholds.FreeFlowMinKmh)
        {
            return "freeFlow";
        }

        if (value >= thresholds.LightMinKmh)
        {
            return "light";
        }

        if (value >= thresholds.ModerateMinKmh)
        {
            return "moderate";
        }

        return "heavy";
    }

    private static string NormalizeCategory(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private readonly record struct DelayThresholds(
        double FreeFlowMaxSeconds,
        double LightMaxSeconds,
        double ModerateMaxSeconds,
        double HeavyMaxSeconds);

    private readonly record struct SpeedThresholds(
        double FreeFlowMinKmh,
        double LightMinKmh,
        double ModerateMinKmh);
}
