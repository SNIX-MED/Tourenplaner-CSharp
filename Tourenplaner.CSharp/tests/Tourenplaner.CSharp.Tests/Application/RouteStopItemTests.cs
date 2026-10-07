using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class RouteStopItemTests
{
    [Theory]
    [InlineData("nicht avisiert", "Nicht avisiert")]
    [InlineData("informiert", "Kunde informiert")]
    [InlineData("Bestätigt", "Termin bestätigt")]
    public void DisplayAvisoStatus_UsesCustomerFriendlyLabel(string status, string expected)
    {
        var item = new RouteStopItem
        {
            OrderId = "220201",
            AvisoStatus = status
        };

        Assert.Equal(expected, item.DisplayAvisoStatus);
    }

    [Fact]
    public void DisplayAvisoStatus_IsEmptyForManualStop()
    {
        var item = new RouteStopItem
        {
            IsManualStop = true,
            AvisoStatus = "Bestätigt"
        };

        Assert.Empty(item.DisplayAvisoStatus);
    }
}
