namespace Tourenplaner.CSharp.Application.Services;

public sealed class RouteOptimizationService
{
    public IReadOnlyList<TPoint> OptimizeNearestNeighbor<TPoint>(
        IReadOnlyList<TPoint> points,
        Func<TPoint, double> latitude,
        Func<TPoint, double> longitude)
    {
        if (points.Count <= 2)
        {
            return points.ToList();
        }

        var remaining = points.ToList();
        var optimized = new List<TPoint>();
        var current = remaining[0];
        optimized.Add(current);
        remaining.RemoveAt(0);

        while (remaining.Count > 0)
        {
            var next = remaining
                .OrderBy(x => HaversineKm(latitude(current), longitude(current), latitude(x), longitude(x)))
                .First();

            optimized.Add(next);
            remaining.Remove(next);
            current = next;
        }

        return optimized;
    }

    public double ComputeTotalDistanceKm<TPoint>(
        IReadOnlyList<TPoint> points,
        Func<TPoint, double> latitude,
        Func<TPoint, double> longitude)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            total += HaversineKm(
                latitude(points[i - 1]),
                longitude(points[i - 1]),
                latitude(points[i]),
                longitude(points[i]));
        }

        return Math.Round(total, 2);
    }

    public IReadOnlyList<TPoint> OptimizeWithFixedEndpoints<TPoint>(
        TPoint start,
        IReadOnlyList<TPoint> middleStops,
        TPoint end,
        Func<TPoint, TPoint, double> travelCost)
    {
        if (middleStops.Count <= 1)
        {
            return middleStops.ToList();
        }

        var remaining = middleStops.ToList();
        var route = new List<TPoint>(remaining.Count);
        var current = start;

        while (remaining.Count > 0)
        {
            var next = remaining
                .OrderBy(x => SafeCost(current, x, travelCost))
                .First();
            route.Add(next);
            remaining.Remove(next);
            current = next;
        }

        // Lightweight local search: keep endpoints fixed and improve by swapping stop positions.
        ImproveByPairSwaps(route, start, end, travelCost);
        return route;
    }

    private static void ImproveByPairSwaps<TPoint>(
        List<TPoint> route,
        TPoint start,
        TPoint end,
        Func<TPoint, TPoint, double> travelCost)
    {
        if (route.Count <= 2)
        {
            return;
        }

        var improved = true;
        while (improved)
        {
            improved = false;
            var bestCost = ComputePathCost(start, route, end, travelCost);

            for (var i = 0; i < route.Count - 1; i++)
            {
                for (var j = i + 1; j < route.Count; j++)
                {
                    (route[i], route[j]) = (route[j], route[i]);
                    var cost = ComputePathCost(start, route, end, travelCost);
                    if (cost + 0.0001d < bestCost)
                    {
                        bestCost = cost;
                        improved = true;
                        continue;
                    }

                    (route[i], route[j]) = (route[j], route[i]);
                }
            }
        }
    }

    private static double ComputePathCost<TPoint>(
        TPoint start,
        IReadOnlyList<TPoint> middle,
        TPoint end,
        Func<TPoint, TPoint, double> travelCost)
    {
        var total = 0d;
        var current = start;
        for (var i = 0; i < middle.Count; i++)
        {
            total += SafeCost(current, middle[i], travelCost);
            current = middle[i];
        }

        total += SafeCost(current, end, travelCost);
        return total;
    }

    private static double SafeCost<TPoint>(TPoint from, TPoint to, Func<TPoint, TPoint, double> travelCost)
    {
        var value = travelCost(from, to);
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return double.MaxValue / 4d;
        }

        return Math.Max(0d, value);
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double radiusKm = 6371.0;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return radiusKm * c;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0);
    }
}
