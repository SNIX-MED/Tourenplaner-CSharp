using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class RouteOptimizationServiceTests
{
    [Fact]
    public void ComputeTotalDistanceKm_ReturnsZero_ForLessThanTwoPoints()
    {
        var service = new RouteOptimizationService();
        var points = new[] { new Pt(47.0, 8.0) };

        var distance = service.ComputeTotalDistanceKm(points, x => x.Lat, x => x.Lon);

        Assert.Equal(0, distance);
    }

    [Fact]
    public void OptimizeNearestNeighbor_KeepsFirstPoint_AndReturnsAllPoints()
    {
        var service = new RouteOptimizationService();
        var points = new[]
        {
            new Pt(47.3769, 8.5417), // start
            new Pt(47.45, 8.55),
            new Pt(47.38, 8.6),
            new Pt(47.2, 8.5)
        };

        var optimized = service.OptimizeNearestNeighbor(points, x => x.Lat, x => x.Lon);

        Assert.Equal(points[0], optimized[0]);
        Assert.Equal(points.Length, optimized.Count);
        Assert.Equal(points.Length, optimized.Distinct().Count());
    }

    [Fact]
    public void ComputeTotalDistanceKm_ReturnsPositiveDistance()
    {
        var service = new RouteOptimizationService();
        var points = new[]
        {
            new Pt(47.3769, 8.5417),
            new Pt(47.4979, 8.7241),
            new Pt(47.0502, 8.3093)
        };

        var distance = service.ComputeTotalDistanceKm(points, x => x.Lat, x => x.Lon);

        Assert.True(distance > 0);
    }

    private sealed record Pt(double Lat, double Lon);
}
