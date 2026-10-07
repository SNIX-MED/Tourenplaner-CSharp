using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebfleetUserSettingsServiceTests
{
    [Fact]
    public void MigrateLegacyDefaultRefreshInterval_ChangesPreviousDefaultToThirtySeconds()
    {
        var settings = new WebfleetConnectionSettings { PositionRefreshSeconds = 60 };

        WebfleetUserSettingsService.MigrateLegacyDefaultRefreshInterval(settings);

        Assert.Equal(30, settings.PositionRefreshSeconds);
    }

    [Fact]
    public void MigrateLegacyDefaultRefreshInterval_PreservesCustomizedInterval()
    {
        var settings = new WebfleetConnectionSettings { PositionRefreshSeconds = 45 };

        WebfleetUserSettingsService.MigrateLegacyDefaultRefreshInterval(settings);

        Assert.Equal(45, settings.PositionRefreshSeconds);
    }
}
