namespace RunnersDiary.Domain.Gpx;

/// <summary>[distance m (int), elevation m (0.1), longitude, latitude] as in ADR 0021 §4.</summary>
public readonly record struct ProfileSample(int DistanceMeters, double ElevationMeters, double Longitude, double Latitude);

public static class ElevationProfile
{
    public const int MaxSamples = 601;
    public const double MinStepMeters = 10;
    public const int TargetIntervals = 600;

    /// <summary>Null when no point has elevation.</summary>
    public static IReadOnlyList<ProfileSample>? Build(GpxDocument document) => Build(new TrackGeometry(document));

    internal static IReadOnlyList<ProfileSample>? Build(TrackGeometry g)
    {
        var sampler = g.Elevations();
        if (sampler is null)
        {
            return null;
        }

        var total = g.TotalDistance;
        var step = Math.Max(MinStepMeters, total / TargetIntervals);
        var samples = new List<ProfileSample> { Sample(g, sampler, 0) };
        for (var k = 1; k * step < total - 1e-6; k++)
        {
            samples.Add(Sample(g, sampler, k * step));
        }

        var end = Sample(g, sampler, total);
        if (samples.Count > 1 && samples[^1].DistanceMeters == end.DistanceMeters)
        {
            samples[^1] = end;
        }
        else if (total > 0)
        {
            samples.Add(end);
        }

        return samples;
    }

    private static ProfileSample Sample(TrackGeometry g, ElevationSampler sampler, double d)
    {
        var (lat, lon, _) = g.At(d);
        return new ProfileSample((int)Math.Round(d), TrackMetrics.Round1(sampler.At(d)), Math.Round(lon, 6), Math.Round(lat, 6));
    }
}
