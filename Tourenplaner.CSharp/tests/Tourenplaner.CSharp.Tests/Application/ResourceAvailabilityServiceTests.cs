using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class ResourceAvailabilityServiceTests
{
    [Theory]
    [InlineData("14:59", false)]
    [InlineData("15:00", true)]
    [InlineData("16:30", true)]
    [InlineData("17:00", true)]
    [InlineData("17:01", false)]
    public void IsUnavailableAt_UsesTimesForSingleDayPeriod(string tourTime, bool expected)
    {
        var periods = new[]
        {
            new ResourceUnavailabilityPeriod
            {
                StartDate = "2026-10-10",
                EndDate = "2026-10-10",
                StartTime = "15:00",
                EndTime = "17:00"
            }
        };

        Assert.Equal(expected, ResourceAvailabilityService.IsUnavailableAt(periods, new DateOnly(2026, 10, 10), tourTime));
    }

    [Theory]
    [InlineData("2026-10-10", "14:59", false)]
    [InlineData("2026-10-10", "15:00", true)]
    [InlineData("2026-10-11", "08:00", true)]
    [InlineData("2026-10-13", "12:00", true)]
    [InlineData("2026-10-13", "12:01", false)]
    public void IsUnavailableAt_UsesBoundaryTimesForMultiDayPeriod(string date, string tourTime, bool expected)
    {
        var periods = new[]
        {
            new ResourceUnavailabilityPeriod
            {
                StartDate = "2026-10-10",
                EndDate = "2026-10-13",
                StartTime = "15:00",
                EndTime = "12:00"
            }
        };

        Assert.Equal(expected, ResourceAvailabilityService.IsUnavailableAt(periods, DateOnly.Parse(date), tourTime));
    }

    [Fact]
    public void IsUnavailableAt_WithoutTimes_BlocksWholeDay()
    {
        var periods = new[]
        {
            new ResourceUnavailabilityPeriod { StartDate = "2026-10-10", EndDate = "2026-10-10" }
        };

        Assert.True(ResourceAvailabilityService.IsUnavailableAt(periods, new DateOnly(2026, 10, 10), "05:00"));
        Assert.True(ResourceAvailabilityService.IsUnavailableAt(periods, new DateOnly(2026, 10, 10), "23:00"));
    }
}
