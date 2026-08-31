using System.Xml.Linq;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class GpxRouteExportServiceTests
{
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";

    [Fact]
    public void TryBuild_WritesWaypointsAndTrackGeometryWithoutStraightRouteLegs()
    {
        var snapshot = BuildSnapshot();

        var success = GpxRouteExportService.TryBuild(snapshot, out var gpx, out var error);

        Assert.True(success);
        Assert.Equal(string.Empty, error);

        var document = XDocument.Parse(gpx);
        var root = Assert.IsType<XElement>(document.Root);
        Assert.Equal(Gpx + "gpx", root.Name);
        Assert.Equal("1.1", root.Attribute("version")?.Value);

        var waypoints = root.Elements(Gpx + "wpt").ToList();
        Assert.Equal(3, waypoints.Count);
        Assert.Contains(waypoints, x => x.Element(Gpx + "name")?.Value == "Zentrale");
        Assert.Contains(waypoints, x => x.Element(Gpx + "name")?.Value == "A - Kunde Eins");
        Assert.Contains(waypoints, x => x.Element(Gpx + "desc")?.Value.Contains("Auftrag: A-100") == true);

        Assert.Null(root.Element(Gpx + "rte"));

        var trackPoints = root.Element(Gpx + "trk")?
            .Element(Gpx + "trkseg")?
            .Elements(Gpx + "trkpt")
            .ToList();
        Assert.NotNull(trackPoints);
        Assert.Equal(3, trackPoints.Count);
        Assert.Equal("47.1005", trackPoints[1].Attribute("lat")?.Value);
    }

    [Fact]
    public void TryBuild_DoesNotUseStopOrderAsStraightTrackFallbackWhenGeometryIsMissing()
    {
        var snapshot = BuildSnapshot(geometryPoints: []);

        var success = GpxRouteExportService.TryBuild(snapshot, out var gpx, out _);

        Assert.True(success);
        var document = XDocument.Parse(gpx);
        Assert.Null(document.Root?.Element(Gpx + "rte"));
        Assert.Null(document.Root?.Element(Gpx + "trk"));
    }

    [Fact]
    public void TryBuild_RejectsRoutesWithFewerThanTwoValidPoints()
    {
        var snapshot = BuildSnapshot(routePoints: [new GeoPoint(47.1, 8.1)]);

        var success = GpxRouteExportService.TryBuild(snapshot, out var gpx, out var error);

        Assert.False(success);
        Assert.Equal(string.Empty, gpx);
        Assert.Contains("mindestens zwei", error);
    }

    private static RouteExportSnapshot BuildSnapshot(
        IReadOnlyList<GeoPoint>? routePoints = null,
        IReadOnlyList<GeoPoint>? geometryPoints = null)
    {
        return new RouteExportSnapshot(
            "Tour Test",
            "31.08.2026",
            "07:30",
            "Sprinter",
            null,
            [
                new RouteExportStopInfo(
                    1,
                    "A",
                    "Kunde Eins",
                    "Hauptstrasse 1, 8000 Zuerich",
                    "Lieferung",
                    "A-100",
                    47.1,
                    8.1,
                    "08:00-09:00",
                    "08:15",
                    string.Empty,
                    "120 kg",
                    "Max",
                    0),
                new RouteExportStopInfo(
                    2,
                    "B",
                    "Kunde Zwei",
                    "Nebenstrasse 2, 8001 Zuerich",
                    "Montage",
                    "B-200",
                    47.2,
                    8.2,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0)
            ],
            routePoints ??
            [
                new GeoPoint(47.0, 8.0),
                new GeoPoint(47.1, 8.1),
                new GeoPoint(47.2, 8.2),
                new GeoPoint(47.0, 8.0)
            ],
            geometryPoints ??
            [
                new GeoPoint(47.0, 8.0),
                new GeoPoint(47.1005, 8.1005),
                new GeoPoint(47.2, 8.2)
            ],
            new RouteExportCompanyInfo("Zentrale", "Industriestrasse 5, 6300 Zug", 47.0, 8.0));
    }
}
