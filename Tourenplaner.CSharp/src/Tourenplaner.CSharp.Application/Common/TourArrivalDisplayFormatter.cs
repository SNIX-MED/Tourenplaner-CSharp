using System.Globalization;

namespace Tourenplaner.CSharp.Application.Common;

public static class TourArrivalDisplayFormatter
{
    public const int DisplayRoundingMinutes = 15;
    public const int MaxDisplayedArrivalRangeMinutes = 120;

    public static (DateTime Optimistic, DateTime Pessimistic) BuildDisplayedArrivalRange(DateTime optimistic, DateTime realistic, DateTime pessimistic)
    {
        var earliestArrival = Min(optimistic, realistic, pessimistic);
        var latestArrival = Max(optimistic, realistic, pessimistic);
        var roundedOptimistic = RoundDownToQuarterHour(earliestArrival);
        var roundedPessimistic = Max(realistic, RoundUpToQuarterHour(latestArrival));
        var maxDisplayedPessimistic = roundedOptimistic.AddMinutes(MaxDisplayedArrivalRangeMinutes);
        if (roundedPessimistic > maxDisplayedPessimistic)
        {
            roundedPessimistic = maxDisplayedPessimistic;
        }

        return (roundedOptimistic, roundedPessimistic);
    }

    public static string BuildDisplayedArrivalRangeText(string? optimisticText, string? realisticText, string? pessimisticText)
    {
        if (!TryParseTime(optimisticText, out var optimistic) ||
            !TryParseTime(pessimisticText, out var pessimistic))
        {
            return string.Empty;
        }

        var realistic = TryParseTime(realisticText, out var parsedRealistic)
            ? parsedRealistic
            : optimistic;

        var displayedRange = BuildDisplayedArrivalRange(optimistic, realistic, pessimistic);
        var displayedOptimistic = displayedRange.Optimistic.ToString("HH:mm", CultureInfo.InvariantCulture);
        var displayedPessimistic = displayedRange.Pessimistic.ToString("HH:mm", CultureInfo.InvariantCulture);
        return string.Equals(displayedOptimistic, displayedPessimistic, StringComparison.Ordinal)
            ? string.Empty
            : $"{displayedOptimistic} - {displayedPessimistic}";
    }

    public static DateTime RoundDownToQuarterHour(DateTime value)
    {
        var roundedMinutes = (value.Minute / DisplayRoundingMinutes) * DisplayRoundingMinutes;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, roundedMinutes, 0, value.Kind);
    }

    public static DateTime RoundUpToQuarterHour(DateTime value)
    {
        var rounded = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);
        var remainder = rounded.Minute % DisplayRoundingMinutes;
        if (remainder == 0)
        {
            return rounded;
        }

        return rounded.AddMinutes(DisplayRoundingMinutes - remainder);
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static DateTime Max(DateTime first, DateTime second, DateTime third)
    {
        return Max(Max(first, second), third);
    }

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;

    private static DateTime Min(DateTime first, DateTime second, DateTime third)
    {
        return Min(Min(first, second), third);
    }

    private static bool TryParseTime(string? value, out DateTime result)
    {
        result = default;
        if (!TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        var today = DateTime.Today;
        result = new DateTime(today.Year, today.Month, today.Day, time.Hour, time.Minute, 0, DateTimeKind.Local);
        return true;
    }
}
