namespace RunnersDiary.Domain.Gpx;

/// <summary>One split. <see cref="ElapsedSeconds"/> is null for untimed tracks, <see cref="ElevationDeltaMeters"/> when there is no elevation.</summary>
public sealed record TrackSplit(int Index, double DistanceMeters, int? ElapsedSeconds, double? ElevationDeltaMeters);

/// <summary>Per-kilometre splits plus the final partial split (≥ 0.1 m), at most <see cref="MaxSplits"/>.</summary>
public static class Splits
{
    public const double SplitMeters = 1000;
    public const int MaxSplits = 1000;

    public static IReadOnlyList<TrackSplit> Compute(GpxDocument document) => Compute(new TrackGeometry(document));

    internal static IReadOnlyList<TrackSplit> Compute(TrackGeometry g)
    {
        var sampler = g.Elevations();
        var total = g.TotalDistance;
        var result = new List<TrackSplit>();
        double previous = 0;
        var index = 1;
        while (result.Count < MaxSplits)
        {
            var boundary = index * SplitMeters;
            var isFull = boundary <= total;
            var end = isFull ? boundary : total;
            var length = end - previous;
            if (length < 0.1)
            {
                break;
            }

            int? elapsed = null;
            if (g.TimeSeconds is not null)
            {
                elapsed = (int)Math.Round(g.At(end).Time!.Value - g.At(previous).Time!.Value);
            }

            double? delta = sampler is null ? null : TrackMetrics.Round1(sampler.At(end) - sampler.At(previous));
            result.Add(new TrackSplit(index, TrackMetrics.Round1(length), elapsed, delta));
            if (!isFull)
            {
                break;
            }

            previous = end;
            index++;
        }

        return result;
    }
}
