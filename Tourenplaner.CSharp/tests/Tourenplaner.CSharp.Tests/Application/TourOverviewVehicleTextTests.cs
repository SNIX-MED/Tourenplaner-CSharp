using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourOverviewVehicleTextTests
{
    [Theory]
    [InlineData("Ford alt", "Hoher Anhänger 2020", "Ford alt + Hoher Anhänger 2020")]
    [InlineData("Ford alt", "", "Ford alt")]
    [InlineData(" Ford alt ", " ", "Ford alt")]
    [InlineData("Nicht zugewiesen", "", "Nicht zugewiesen")]
    public void VehicleCombinationOmitsEmptyTrailerAndExtraSeparators(string vehicle, string trailer, string expected)
    {
        var item = new SavedTourOverviewItem(1, "Tour", "", "", 0, "", "", "", "", "", 0, 0)
        {
            VehicleName = vehicle, TrailerName = trailer
        };
        Assert.Equal(expected, item.VehicleCombinationText);
    }
}
