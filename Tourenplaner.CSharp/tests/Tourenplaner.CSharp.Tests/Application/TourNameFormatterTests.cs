using System.Globalization;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourNameFormatterTests
{
    [Theory]
    [InlineData("Tour 03.11.2026", true, "Dienstag 03.11.2026", "Mittwoch 04.11.2026")]
    [InlineData("Dienstag 03.11.2026", true, "Dienstag 03.11.2026", "Mittwoch 04.11.2026")]
    [InlineData("Tour 03.11.2026", false, "Tour 03.11.2026", "Tour 03.11.2026")]
    [InlineData("Tour Zürich", true, "Tour Zürich", "Tour Zürich")]
    [InlineData("Tour 02.11.2026", true, "Tour 02.11.2026", "Tour 02.11.2026")]
    public void IncomingNamesAreHandledForCreationAndEditing(string incomingName, bool normalize,
        string expectedInitialName, string expectedAfterDateChange)
    {
        var model = new CreateTourDialogViewModel("03.11.2026", incomingName, "07", "30", [], [], [],
            normalizeLegacyDefaultName: normalize);
        Assert.Equal(expectedInitialName, model.Name);
        model.DateText = "04.11.2026";
        Assert.Equal(expectedAfterDateChange, model.Name);
    }

    [Theory]
    [InlineData("02.11.2026", "Montag")]
    [InlineData("03.11.2026", "Dienstag")]
    [InlineData("04.11.2026", "Mittwoch")]
    [InlineData("05.11.2026", "Donnerstag")]
    [InlineData("06.11.2026", "Freitag")]
    [InlineData("07.11.2026", "Samstag")]
    [InlineData("08.11.2026", "Sonntag")]
    public void WeekdayIsGermanRegardlessOfCurrentCulture(string date, string weekday)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal($"{weekday} {date}", TourNameFormatter.BuildDefault(date));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void DateChangesUpdateAutomaticNamesAndPreserveCustomNames()
    {
        var model = new CreateTourDialogViewModel("03.11.2026", "", "07", "00", [], [], []);
        Assert.Equal("Dienstag 03.11.2026", model.Name);
        model.DateText = "04.11.2026";
        Assert.Equal("Mittwoch 04.11.2026", model.Name);
        model.Name = "Sonderfahrt Zürich";
        model.DateText = "05.11.2026";
        Assert.Equal("Sonderfahrt Zürich", model.Name);
    }
}
