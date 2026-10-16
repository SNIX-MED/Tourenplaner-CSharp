using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class StartCalendarSummaryTests
{
    [Fact]
    public void SummaryIncludesEveryTourAndManualEntryInTimeOrder()
    {
        var lines = StartSectionViewModel.BuildStartSummaryLines(
            [new() { Name = "Tour spät", StartTime = "14:00" }, new() { Name = "Tour früh", StartTime = "07:30" }],
            [new() { Title = "Ganzer Tag" }, new() { Title = "Termin eins", Time = "09:00", ColorHex = "#123456" },
             new() { Title = "Termin zwei", Time = "10:00" }, new() { Title = "Termin drei", Time = "11:00" }]);
        Assert.Equal(6, lines.Count);
        Assert.Contains("Tour früh", lines[0].Text);
        Assert.Equal("09:00 Termin eins", lines[1].Text);
        Assert.True(lines[1].ShowManualDot);
        Assert.Equal("#123456", lines[1].DotColor);
        Assert.Contains("Tour spät", lines[4].Text);
        Assert.Equal("Ganzer Tag", lines[5].Text);
    }

    [Fact]
    public void ResourceEntriesAppearOnEveryAffectedDayOnly()
    {
        var period = new ResourceUnavailabilityPeriod { StartDate = "16.10.2026", EndDate = "18.10.2026", Note = "Werkstatt" };
        Employee[] employees = [new() { DisplayName = "Mitarbeiter", UnavailabilityPeriods = [period] }];
        var vehicles = new VehicleDataRecord
        {
            Vehicles = [new() { Name = "Fahrzeug", UnavailabilityPeriods = [period] }],
            Trailers = [new() { Name = "Anhänger", UnavailabilityPeriods = [period] }]
        };
        Assert.Equal(3, StartSectionViewModel.BuildResourceCalendarLines(employees, vehicles, new DateOnly(2026, 10, 17)).Count);
        Assert.Empty(StartSectionViewModel.BuildResourceCalendarLines(employees, vehicles, new DateOnly(2026, 10, 19)));
    }
}
