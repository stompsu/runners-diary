namespace RunnersDiary.Domain.Gpx;

/// <summary>The stored summary of a processed track (ADR 0021). Rounded at this boundary only: metres to 0.1, seconds to whole.</summary>
public sealed record TrackSummary(
    int PointCount,
    double DistanceMeters,
    double? ElevationGainMeters,
    double? ElevationLossMeters,
    double? MinElevationMeters,
    double? MaxElevationMeters,
    int? ElapsedSeconds,
    int? MovingSeconds,
    DateTime? StartUtc,
    DateTime? EndUtc,
    double StartLatitude,
    double StartLongitude,
    double FinishLatitude,
    double FinishLongitude,
    TrackWarnings Warnings);

public static class TrackMetrics
{
    /// <summary>A moving interval has speed at least this (m/s).</summary>
    public const double MovingSpeedThreshold = 0.5;

    public static TrackSummary Compute(GpxDocument document) => Compute(new TrackGeometry(document));

    internal static TrackSummary Compute(TrackGeometry g)
    {
        double? gain = null, loss = null, min = null, max = null;
        var sampler = g.Elevations();
        if (sampler is not null)
        {
            double gSum = 0, lSum = 0;
            for (var s = 0; s < g.SegmentStart.Length - 1; s++)
            {
                var values = new List<double>();
                for (var i = g.SegmentStart[s]; i < g.SegmentStart[s + 1]; i++)
                {
                    if (!double.IsNaN(g.Ele[i]))
                    {
                        values.Add(g.Ele[i]);
                    }
                }

                var (sg, sl) = ElevationHysteresis.Compute(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values));
                gSum += sg;
                lSum += sl;
            }

            gain = Round1(gSum);
            loss = Round1(lSum);
            min = Round1(sampler.Min);
            max = Round1(sampler.Max);
        }

        int? elapsed = null, moving = null;
        DateTime? start = null, end = null;
        if (g.TimeSeconds is not null)
        {
            elapsed = (int)Math.Round(g.TimeSeconds[^1]);
            moving = (int)Math.Round(MovingTime.Seconds(g));
            start = g.Document.Segments[0].Points[0].TimeUtc;
            var lastSegment = g.Document.Segments[^1].Points;
            end = lastSegment[^1].TimeUtc;
        }

        return new TrackSummary(
            g.Count, Round1(g.TotalDistance), gain, loss, min, max, elapsed, moving, start, end,
            g.Lat[0], g.Lon[0], g.Lat[^1], g.Lon[^1], g.Document.Warnings);
    }

    internal static double Round1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
}

/// <summary>Moving time: sum of in-segment intervals with dt &gt; 0 and speed at least 0.5 m/s.</summary>
public static class MovingTime
{
    public static double Seconds(GpxDocument document) => Seconds(new TrackGeometry(document));

    internal static double Seconds(TrackGeometry g)
    {
        if (g.TimeSeconds is null)
        {
            return 0;
        }

        double total = 0;
        for (var s = 0; s < g.SegmentStart.Length - 1; s++)
        {
            for (var i = g.SegmentStart[s] + 1; i < g.SegmentStart[s + 1]; i++)
            {
                var dt = g.TimeSeconds[i] - g.TimeSeconds[i - 1];
                if (dt > 0 && (g.Cum[i] - g.Cum[i - 1]) / dt >= TrackMetrics.MovingSpeedThreshold)
                {
                    total += dt;
                }
            }
        }

        return total;
    }
}
