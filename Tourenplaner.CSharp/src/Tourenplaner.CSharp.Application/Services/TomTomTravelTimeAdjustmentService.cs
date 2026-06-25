namespace Tourenplaner.CSharp.Application.Services;

public readonly record struct TomTomTravelTimeAdjustment(
    int OptimisticSeconds,
    int RealisticSeconds,
    int PessimisticSeconds);

public static class TomTomTravelTimeAdjustmentService
{
    public static TomTomTravelTimeAdjustment ApplySeverityMode(
        int optimisticSeconds,
        int realisticSeconds,
        int pessimisticSeconds,
        int noTrafficDelaySeconds,
        int historicDelaySeconds,
        int liveIncidentDelaySeconds,
        TomTomTrafficSeverityMode severityMode)
    {
        var normalizedOptimistic = Math.Max(0, optimisticSeconds);
        var normalizedRealistic = Math.Max(0, realisticSeconds);
        var normalizedPessimistic = Math.Max(normalizedRealistic, pessimisticSeconds);
        if (severityMode == TomTomTrafficSeverityMode.Standard)
        {
            return new TomTomTravelTimeAdjustment(
                normalizedOptimistic,
                normalizedRealistic,
                normalizedPessimistic);
        }

        var observedDelaySeconds = Math.Max(
            Math.Max(Math.Max(0, noTrafficDelaySeconds), Math.Max(0, historicDelaySeconds)),
            Math.Max(0, liveIncidentDelaySeconds));
        if (observedDelaySeconds <= 0)
        {
            return new TomTomTravelTimeAdjustment(
                normalizedOptimistic,
                normalizedRealistic,
                normalizedPessimistic);
        }

        var optimisticExtraSeconds = severityMode switch
        {
            TomTomTrafficSeverityMode.Strict => CalculateBoundedExtraSeconds(observedDelaySeconds, normalizedRealistic, 0.35d, 0.04d, 8 * 60),
            TomTomTrafficSeverityMode.SlightlyStricter => CalculateBoundedExtraSeconds(observedDelaySeconds, normalizedRealistic, 0.20d, 0.02d, 5 * 60),
            _ => 0
        };

        var realisticExtraSeconds = severityMode switch
        {
            TomTomTrafficSeverityMode.Strict => CalculateBoundedExtraSeconds(observedDelaySeconds, normalizedRealistic, 1.00d, 0.10d, 18 * 60),
            TomTomTrafficSeverityMode.SlightlyStricter => CalculateBoundedExtraSeconds(observedDelaySeconds, normalizedRealistic, 0.50d, 0.06d, 12 * 60),
            _ => 0
        };

        var pessimisticExtraSeconds = severityMode switch
        {
            TomTomTrafficSeverityMode.Strict => CalculateBoundedExtraSeconds(observedDelaySeconds, normalizedRealistic, 1.50d, 0.18d, 30 * 60),
            TomTomTrafficSeverityMode.SlightlyStricter => CalculateBoundedExtraSeconds(observedDelaySeconds, normalizedRealistic, 0.90d, 0.10d, 20 * 60),
            _ => 0
        };

        var adjustedOptimistic = normalizedOptimistic + optimisticExtraSeconds;
        var adjustedRealistic = Math.Max(adjustedOptimistic, normalizedRealistic + realisticExtraSeconds);
        var adjustedPessimistic = Math.Max(adjustedRealistic, normalizedPessimistic + pessimisticExtraSeconds);

        return new TomTomTravelTimeAdjustment(
            adjustedOptimistic,
            adjustedRealistic,
            adjustedPessimistic);
    }

    private static int CalculateBoundedExtraSeconds(
        int observedDelaySeconds,
        int realisticSeconds,
        double observedDelayFactor,
        double realisticFloorFactor,
        int capSeconds)
    {
        var observedComponent = (int)Math.Round(observedDelaySeconds * observedDelayFactor, MidpointRounding.AwayFromZero);
        var floorComponent = (int)Math.Round(realisticSeconds * realisticFloorFactor, MidpointRounding.AwayFromZero);
        return Math.Min(capSeconds, Math.Max(observedComponent, floorComponent));
    }
}
