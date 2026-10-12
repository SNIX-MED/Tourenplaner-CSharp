using Tourenplaner.CSharp.App.Views.Sections;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class MapHtmlDocumentBuilderTests
{
    [Fact]
    public void CoincidentPartialDeliveryPins_AreOrderedByDeliveryNumberFromTheLeft()
    {
        var html = MapHtmlDocumentBuilder.Build(null, true, true, null, false, true, true, false, false, true, true, 1d, 0);

        Assert.Contains(
            "Number(left.marker.deliveryNumber) - Number(right.marker.deliveryNumber)",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "const angleOffset = orderedGroup.length === 2 ? Math.PI",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RouteRendering_CanExplicitlySuppressAutomaticCentering()
    {
        var html = MapHtmlDocumentBuilder.Build(
            null,
            true,
            true,
            null,
            false,
            true,
            true,
            false,
            false,
            true,
            true,
            1d,
            0);

        Assert.Contains(
            "window.gawelaSetRoute = function(routeStops, geometryPoints, routeColor, trafficSegments, allowAutoCenter)",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (allowAutoCenter !== false && routeKey && routeKey !== lastAutoCenteredRouteKey)",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.gawelaSetRoute(last.routeStops, last.geometryPoints, last.routeColor, last.trafficSegments, false)",
            html,
            StringComparison.Ordinal);
    }
}
