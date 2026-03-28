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

    [Fact]
    public void OptimizeWithFixedEndpoints_ReducesTotalTravelCost()
    {
        var service = new RouteOptimizationService();
        var start = "start";
        var end = "end";
        var middle = new[] { "A", "B", "C" };

        double Cost(string from, string to)
        {
            if (from == to)
            {
                return 0;
            }

            return (from, to) switch
            {
                ("start", "A") => 1,
                ("start", "B") => 2,
                ("start", "C") => 2,
                ("A", "B") => 100,
                ("A", "C") => 100,
                ("A", "end") => 1,
                ("B", "A") => 1,
                ("B", "C") => 1,
                ("B", "end") => 10,
                ("C", "A") => 1,
                ("C", "B") => 1,
                ("C", "end") => 10,
                _ => 10
            };
        }

        var originalCost = ComputePathCost(start, middle, end, Cost);
        var optimized = service.OptimizeWithFixedEndpoints(start, middle, end, Cost);
        var optimizedCost = ComputePathCost(start, optimized, end, Cost);

        Assert.Equal(middle.Length, optimized.Count);
        Assert.Equal(middle.Length, optimized.Distinct().Count());
        Assert.True(optimizedCost < originalCost);
        Assert.Equal(5d, optimizedCost);
    }

    private static double ComputePathCost<T>(
        T start,
        IReadOnlyList<T> middle,
        T end,
        Func<T, T, double> cost)
    {
        var total = 0d;
        var current = start;
        for (var i = 0; i < middle.Count; i++)
        {
            total += cost(current, middle[i]);
            current = middle[i];
        }

        total += cost(current, end);
        return total;
    }

    private sealed record Pt(double Lat, double Lon);
}
