using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class TomTomRoutingServiceTests
{
    [Fact]
    public void RoutingProfile_AvoidsFerriesByDefault_AndCanBeDisabled()
    {
        Assert.True(TomTomRoutingProfile.Default.AvoidFerries);

        var profile = TomTomRoutingProfile.Default with { AvoidFerries = false };

        Assert.False(profile.AvoidFerries);
    }

    [Fact]
    public void ApplyAvoidOptions_AddsFerriesOnlyWhenEnabled()
    {
        var defaultService = new TomTomRoutingService("test-key");
        var ferriesAllowedService = new TomTomRoutingService(
            "test-key",
            TomTomRoutingProfile.Default with { AvoidFerries = false });

        Assert.Equal("https://route.test?traffic=true&avoid=ferries", defaultService.ApplyAvoidOptions("https://route.test?traffic=true"));
        Assert.Equal("https://route.test?traffic=true", ferriesAllowedService.ApplyAvoidOptions("https://route.test?traffic=true"));
    }
}
