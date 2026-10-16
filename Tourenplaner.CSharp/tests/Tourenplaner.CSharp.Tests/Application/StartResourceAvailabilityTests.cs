using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class StartResourceAvailabilityTests
{
    [Fact]
    public void ResourceOverviewIncludesFutureOutagesBeyondCalendarPreview()
    {
        var items = new List<StartResourceAbsenceItem>();
        StartSectionViewModel.AddResourcePeriods(items, "Ford neu",
        [
            new() { StartDate = "12.11.2026", EndDate = "16.11.2026", Note = "Vorführen Anhänger" },
            new() { StartDate = "15.10.2026", EndDate = "17.10.2026" },
            new() { StartDate = "12.10.2026", EndDate = "15.10.2026" }
        ], new DateOnly(2026, 10, 16), new AsyncCommand(() => Task.CompletedTask), "#F97316");

        Assert.Equal(2, items.Count);
        Assert.Equal("Ford neu", items[0].ResourceName);
        Assert.Equal("Vorführen Anhänger", items[0].Note);
        Assert.Contains("12.11.", items[0].PeriodText);
        Assert.False(items[0].IsToday);
        Assert.True(items[1].IsToday);
    }
}
