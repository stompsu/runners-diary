namespace RunnersDiary.Domain.Gpx;

/// <summary>A GeoJSON position (longitude first), rounded to 6 decimals (about 0.1 m).</summary>
public readonly record struct LonLat(double Longitude, double Latitude);

/// <summary>
/// Iterative (stack-based) Douglas–Peucker on an equirectangular projection in metres (ADR 0021 §3). One pass computes each
/// point's significance; the tolerance (3 m, doubled until the vertex cap holds) is then only a threshold on those values.
/// A hard budget on distance evaluations bounds adversarial input (alternating zigzags are quadratic for Douglas–Peucker); when it
/// is exhausted the line is decimated uniformly instead.
/// </summary>
public static class DouglasPeucker
{
    public const double StartToleranceMeters = 3.0;
    public const int MaxVertices = 4_000;

    /// <summary>Point-to-chord distance evaluations allowed per track (a few hundred milliseconds of CPU at worst).</summary>
    public const long MaxDistanceEvaluations = 50_000_000;

    /// <summary>
    /// Simplifies each segment (first and last point always kept). The tolerance starts at 3 m and doubles until the whole
    /// line has at most <paramref name="maxVertices"/> vertices. Segments with a single point are not lines and are dropped.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<LonLat>> Simplify(GpxDocument document, double toleranceMeters = StartToleranceMeters, int maxVertices = MaxVertices)
        => Simplify(new TrackGeometry(document), toleranceMeters, maxVertices, CancellationToken.None);

    internal static IReadOnlyList<IReadOnlyList<LonLat>> Simplify(TrackGeometry g, double toleranceMeters, int maxVertices, CancellationToken cancellationToken)
        => Simplify(g, toleranceMeters, maxVertices, MaxDistanceEvaluations, cancellationToken, out _, out _);

    internal static IReadOnlyList<IReadOnlyList<LonLat>> Simplify(
        TrackGeometry g, double toleranceMeters, int maxVertices, long evaluationBudget, CancellationToken cancellationToken,
        out long evaluations, out bool decimated)
    {
        var lat0 = (g.Lat.Min() + g.Lat.Max()) / 2;
        var lon0 = (g.Lon.Min() + g.Lon.Max()) / 2;
        var k = Math.PI / 180 * Haversine.EarthRadiusMeters;
        var cosLat = Math.Cos(lat0 * Math.PI / 180);
        var x = new double[g.Count];
        var y = new double[g.Count];
        for (var i = 0; i < g.Count; i++)
        {
            x[i] = (g.Lon[i] - lon0) * cosLat * k;
            y[i] = (g.Lat[i] - lat0) * k;
        }

        var segments = Enumerable.Range(0, g.SegmentStart.Length - 1)
            .Where(s => g.SegmentStart[s + 1] - g.SegmentStart[s] >= 2)
            .ToList();

        // significance[i]: the largest tolerance at which point i is still kept (0 = never, +Inf = segment end).
        var significance = new double[g.Count];
        var remaining = evaluationBudget;
        var withinBudget = true;
        foreach (var s in segments)
        {
            if (!Mark(x, y, significance, g.SegmentStart[s], g.SegmentStart[s + 1] - 1, toleranceMeters, ref remaining, cancellationToken))
            {
                withinBudget = false;
                break;
            }
        }

        evaluations = evaluationBudget - remaining;
        decimated = !withinBudget;
        var keep = new bool[g.Count];
        if (withinBudget)
        {
            var tolerance = toleranceMeters;
            while (CountKept(significance, tolerance) > maxVertices && tolerance <= 1e7)
            {
                tolerance *= 2;
            }

            for (var i = 0; i < keep.Length; i++)
            {
                keep[i] = significance[i] > tolerance;
            }
        }
        else
        {
            DecimateUniformly(g, segments, keep, maxVertices);
        }

        var result = new List<IReadOnlyList<LonLat>>();
        foreach (var s in segments)
        {
            var line = new List<LonLat>();
            for (var i = g.SegmentStart[s]; i < g.SegmentStart[s + 1]; i++)
            {
                if (keep[i])
                {
                    line.Add(new LonLat(Math.Round(g.Lon[i], 6), Math.Round(g.Lat[i], 6)));
                }
            }

            result.Add(line);
        }

        return result;
    }

    private static int CountKept(double[] significance, double tolerance)
    {
        var count = 0;
        foreach (var value in significance)
        {
            if (value > tolerance)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Every n-th point of each segment plus its ends, with n chosen so the total stays within the cap.</summary>
    private static void DecimateUniformly(TrackGeometry g, List<int> segments, bool[] keep, int maxVertices)
    {
        long total = 0;
        foreach (var s in segments)
        {
            total += g.SegmentStart[s + 1] - g.SegmentStart[s];
        }

        var budget = Math.Max(1, maxVertices - 2L * segments.Count);
        var stride = (int)Math.Min(int.MaxValue, (total + budget - 1) / budget);
        foreach (var s in segments)
        {
            var first = g.SegmentStart[s];
            var last = g.SegmentStart[s + 1] - 1;
            for (var i = first; i < last; i += stride)
            {
                keep[i] = true;
            }

            keep[last] = true;
        }
    }

    /// <summary>
    /// Records significances for one segment (ends get +Infinity). A split below <paramref name="floor"/> can never survive the
    /// smallest tolerance, so it is not followed. Returns false when the evaluation budget is exhausted.
    /// </summary>
    private static bool Mark(
        double[] x, double[] y, double[] significance, int first, int last, double floor, ref long remaining, CancellationToken cancellationToken)
    {
        significance[first] = double.PositiveInfinity;
        significance[last] = double.PositiveInfinity;
        var stack = new Stack<(int Start, int End, double Parent)>();
        stack.Push((first, last, double.PositiveInfinity));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (a, b, parent) = stack.Pop();
            if (b - a < 2)
            {
                continue;
            }

            if (remaining < b - a - 1)
            {
                return false;
            }

            remaining -= b - a - 1;
            var (maxIndex, maxDist) = Farthest(x, y, a, b);

            if (maxDist > floor)
            {
                // A point survives a tolerance only if every split on its way down did.
                var effective = Math.Min(maxDist, parent);
                significance[maxIndex] = effective;
                stack.Push((a, maxIndex, effective));
                stack.Push((maxIndex, b, effective));
            }
        }

        return true;
    }

    /// <summary>The point strictly between a and b farthest from the chord (the infinite line, as in classic Douglas–Peucker).</summary>
    private static (int Index, double Distance) Farthest(double[] x, double[] y, int a, int b)
    {
        var ax = x[a];
        var ay = y[a];
        var dx = x[b] - ax;
        var dy = y[b] - ay;
        var len2 = dx * dx + dy * dy;
        var index = -1;
        double best = -1;
        if (len2 == 0)
        {
            // A closed loop: the chord is degenerate, so use the distance to its single point.
            for (var i = a + 1; i < b; i++)
            {
                var d2 = (x[i] - ax) * (x[i] - ax) + (y[i] - ay) * (y[i] - ay);
                if (d2 > best)
                {
                    best = d2;
                    index = i;
                }
            }

            return (index, Math.Sqrt(best));
        }

        // The cross product is proportional to the distance, so the loop needs neither a square root nor a division.
        for (var i = a + 1; i < b; i++)
        {
            var cross = Math.Abs(dx * (ay - y[i]) - dy * (ax - x[i]));
            if (cross > best)
            {
                best = cross;
                index = i;
            }
        }

        return (index, best / Math.Sqrt(len2));
    }
}
