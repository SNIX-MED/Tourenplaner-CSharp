using Tourenplaner.CSharp.Application.Common;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourArrivalDisplayFormatterTests
{
    [Fact]
    public void BuildDisplayedArrivalRange_RoundsEarlierTimeDownToQuarterHour()
    {
        var baseDate = new DateTime(2026, 3, 21, 8, 0, 0);

        var result = TourArrivalDisplayFormatter.BuildDisplayedArrivalRange(
            baseDate.AddMinutes(44),
            baseDate.AddMinutes(47),
            baseDate.AddMinutes(53));

        Assert.Equal("08:30", result.Optimistic.ToString("HH:mm"));
        Assert.Equal("09:00", result.Pessimistic.ToString("HH:mm"));
    }

    [Fact]
    public void BuildDisplayedArrivalRange_UsesOnlyQuarterHourBoundaries()
    {
        var baseDate = new DateTime(2026, 3, 21, 8, 0, 0);

        var result = TourArrivalDisplayFormatter.BuildDisplayedArrivalRange(
            baseDate.AddMinutes(1),
            baseDate.AddMinutes(14),
            baseDate.AddMinutes(16));

        Assert.Equal("08:00", result.Optimistic.ToString("HH:mm"));
        Assert.Equal("08:30", result.Pessimistic.ToString("HH:mm"));
    }

    [Fact]
    public void BuildDisplayedArrivalRange_ClampsRangeToTwoHours()
    {
        var baseDate = new DateTime(2026, 3, 21, 8, 0, 0);

        var result = TourArrivalDisplayFormatter.BuildDisplayedArrivalRange(
            baseDate.AddMinutes(45),
            baseDate.AddMinutes(60),
            baseDate.AddMinutes(180));

        Assert.Equal("08:45", result.Optimistic.ToString("HH:mm"));
        Assert.Equal("10:45", result.Pessimistic.ToString("HH:mm"));
    }

    [Fact]
    public void BuildDisplayedArrivalRangeText_RoundsSlotStartDownToNearestQuarterHour()
    {
        var result = TourArrivalDisplayFormatter.BuildDisplayedArrivalRangeText(
            "09:51",
            "09:51",
            "11:19");

        Assert.Equal("09:45 - 11:30", result);
    }

    [Fact]
    public void BuildDisplayedArrivalRange_UsesEarliestArrivalForSlotStart()
    {
        var baseDate = new DateTime(2026, 3, 21, 8, 0, 0);

        var result = TourArrivalDisplayFormatter.BuildDisplayedArrivalRange(
            baseDate.AddMinutes(60),
            baseDate.AddMinutes(55),
            baseDate.AddMinutes(90));

        Assert.Equal("08:45", result.Optimistic.ToString("HH:mm"));
        Assert.Equal("09:30", result.Pessimistic.ToString("HH:mm"));
    }
}
