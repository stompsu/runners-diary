namespace RunnersDiary.Domain.Gpx;

/// <summary>
/// Gain/loss with a pivot threshold (ADR 0021): a climb or descent counts only once it is at least
/// <see cref="ThresholdMeters"/> from the last pivot; the pivot then moves to that elevation. No other smoothing.
/// </summary>
public static class ElevationHysteresis
{
    public const double ThresholdMeters = 3.0;

    /// <summary>Accumulates over one continuous sequence (reset the pivot per segment by calling once per segment).</summary>
    public static (double Gain, double Loss) Compute(ReadOnlySpan<double> elevations, double threshold = ThresholdMeters)
    {
        if (elevations.IsEmpty)
        {
            return (0, 0);
        }

        double gain = 0, loss = 0;
        var pivot = elevations[0];
        for (var i = 1; i < elevations.Length; i++)
        {
            var e = elevations[i];
            if (e - pivot >= threshold)
            {
                gain += e - pivot;
                pivot = e;
            }
            else if (pivot - e >= threshold)
            {
                loss += pivot - e;
                pivot = e;
            }
        }

        return (gain, loss);
    }
}
