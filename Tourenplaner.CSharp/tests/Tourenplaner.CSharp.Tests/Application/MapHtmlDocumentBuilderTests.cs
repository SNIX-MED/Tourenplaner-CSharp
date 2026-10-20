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
    public void AddressFanOut_DoesNotUseRemovedLegacyClusterImplementation()
    {
        var html = MapHtmlDocumentBuilder.Build(null, true, true, null, false, true, true, false, false, true, true, 1d, 0);

        Assert.DoesNotContain("groupMarkersByScreenCollision", html, StringComparison.Ordinal);
        Assert.DoesNotContain("gawela-marker-cluster", html, StringComparison.Ordinal);
        Assert.DoesNotContain("collisionClusterMarkers", html, StringComparison.Ordinal);
        Assert.DoesNotContain("buildOverlapCoordinateKey", html, StringComparison.Ordinal);
        Assert.DoesNotContain("return `coord:${coordinateKey}`", html, StringComparison.Ordinal);
        Assert.Contains("const groups = groupOverlappingMarkers(markers)", html, StringComparison.Ordinal);
        Assert.Contains("const key = buildOverlapGroupKey(marker)", html, StringComparison.Ordinal);
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

    [Fact]
    public void RouteRendering_DoesNotDrawStraightLinesBeforeGeometryIsAvailable()
    {
        var html = MapHtmlDocumentBuilder.Build(null, true, true, null, false, true, true, false, false, true, true, 1d, 0);

        Assert.Contains(
            "Do not connect route pins with temporary straight lines",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ": routeStops.map(toMapCoordinate).filter(p => Array.isArray(p));",
            html,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "speed-limit-badge hidden", "<span></span>")]
    [InlineData(80, "speed-limit-badge ", "<span>80</span>")]
    public void SpeedLimitBadge_CanBeUpdatedWithoutReloadingMapDocument(
        int maxSpeedKmh,
        string expectedBadgeMarkup,
        string expectedValueMarkup)
    {
        var html = MapHtmlDocumentBuilder.Build(null, true, true, null, false, true, true, false, false, true, true, 1d, maxSpeedKmh);

        Assert.Contains(expectedBadgeMarkup, html, StringComparison.Ordinal);
        Assert.Contains(expectedValueMarkup, html, StringComparison.Ordinal);
        Assert.Contains("window.gawelaSetSpeedLimit = function(maxSpeedKmh)", html, StringComparison.Ordinal);
        Assert.Contains("badge.classList.toggle('hidden', speed <= 0)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void NearbyPins_AreClusteredByScreenDistanceAndSpiderfiedAtHighZoom()
    {
        var html = MapHtmlDocumentBuilder.Build(null, true, true, null, false, true, true, false, false, true, true, 1d, 0);

        Assert.Contains("const applyScreenCollisionLayout = (markers, collisionMarkers = markers) =>", html, StringComparison.Ordinal);
        Assert.Contains("const collisionDistance = Math.max(16, Math.min(23, 18 * markerScale))", html, StringComparison.Ordinal);
        Assert.Contains("__screenClusterCount = group.length", html, StringComparison.Ordinal);
        Assert.Contains("currentZoom) && currentZoom >= 15.5", html, StringComparison.Ordinal);
        Assert.Contains("if (group.length === 2)", html, StringComparison.Ordinal);
        Assert.Contains("hoveredOverlapGroupKey === screenGroupKey", html, StringComparison.Ordinal);
        Assert.Contains("? Math.max(12, 14 * markerScale)", html, StringComparison.Ordinal);
        Assert.Contains("result[entry.index].__overlapGroupKey = screenGroupKey", html, StringComparison.Ordinal);
        Assert.Contains("const zoomToScreenCluster = marker =>", html, StringComparison.Ordinal);
        Assert.Contains("gawela-pin-cluster-badge", html, StringComparison.Ordinal);
        Assert.Contains("map.on('zoomend', scheduleMarkerFanOutRecompute)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderAndRoutePins_OpenTheirInfoCardWhileHovered()
    {
        var html = MapHtmlDocumentBuilder.Build(null, true, true, null, false, true, true, false, false, true, true, 1d, 0);

        Assert.Contains("const openMarkerPopupOnHover = marker =>", html, StringComparison.Ordinal);
        Assert.Contains("const closeMarkerPopupAfterHover = marker =>", html, StringComparison.Ordinal);
        Assert.Contains("if (!markerPopupHoverEnabled) return;", html, StringComparison.Ordinal);
        Assert.Contains("window.gawelaSetMarkerPopupHoverEnabled = function(enabled)", html, StringComparison.Ordinal);
        Assert.Contains("mapMarkers.forEach(closeMarkerPopupAfterHover);", html, StringComparison.Ordinal);
        Assert.Contains("routeMarkers.forEach(closeMarkerPopupAfterHover);", html, StringComparison.Ordinal);
        Assert.Contains("createScaledPopupHtml(buildPinPopupHtml(stop))", html, StringComparison.Ordinal);
        Assert.DoesNotContain("buildPinPopupHtml(matchingOrderMarker || stop)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("createScaledPopupHtml('')", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, "openMarkerPopupOnHover(marker);"));
        Assert.Equal(2, CountOccurrences(html, "closeMarkerPopupAfterHover(marker);"));
        Assert.Contains("if (!marker || routePopupVisible", html, StringComparison.Ordinal);
        Assert.Contains("if (stickyPopupOrderId && markerId === stickyPopupOrderId) return;", html, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string searchValue)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(searchValue, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += searchValue.Length;
        }

        return count;
    }

    [Theory]
    [InlineData(0, -18)]
    [InlineData(36, -4.5)]
    [InlineData(250, 0)]
    [InlineData(464, 4.5)]
    [InlineData(500, 18)]
    public void RouteDragAutoScroll_UsesPointerDistanceFromViewportEdges(
        double pointerY,
        double expectedDelta)
    {
        var delta = KarteSectionView.CalculateRouteDragAutoScrollDelta(
            pointerY,
            viewportHeight: 500,
            scrollableHeight: 1000);

        Assert.Equal(expectedDelta, delta, precision: 6);
    }
}
