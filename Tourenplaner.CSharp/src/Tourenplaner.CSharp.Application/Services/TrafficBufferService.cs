namespace Tourenplaner.CSharp.Application.Services;

public static class TrafficBufferService
{
    public static int CalculateBufferMinutes(
        int driveMinutes,
        DateTime departureTime,
        int percentFrom0500To0730,
        int percentFrom0730To0900,
        int percentFrom0900To1530,
        int percentFrom1530To1830)
    {
        var normalizedDriveMinutes = Math.Max(0, driveMinutes);
        var normalizedPercent = Math.Max(0, ResolveBufferPercent(
            departureTime,
            percentFrom0500To0730,
            percentFrom0730To0900,
            percentFrom0900To1530,
            percentFrom1530To1830));
        if (normalizedDriveMinutes <= 30 || normalizedPercent <= 0)
        {
            return 0;
        }

        return (int)Math.Round(normalizedDriveMinutes * normalizedPercent / 100d, MidpointRounding.AwayFromZero);
    }

    private static int ResolveBufferPercent(
        DateTime departureTime,
        int percentFrom0500To0730,
        int percentFrom0730To0900,
        int percentFrom0900To1530,
        int percentFrom1530To1830)
    {
        var timeOfDay = departureTime.TimeOfDay;
        if (timeOfDay >= TimeSpan.FromHours(5) && timeOfDay < TimeSpan.FromHours(6))
        {
            return percentFrom0500To0730;
        }

        if (timeOfDay >= TimeSpan.FromHours(6) && timeOfDay < TimeSpan.FromHours(9))
        {
            return percentFrom0730To0900;
        }

        if (timeOfDay >= TimeSpan.FromHours(9) && timeOfDay < TimeSpan.FromHours(15.5))
        {
            return percentFrom0900To1530;
        }

        if (timeOfDay >= TimeSpan.FromHours(15.5) && timeOfDay < TimeSpan.FromHours(18.5))
        {
            return percentFrom1530To1830;
        }

        return 0;
    }
}
