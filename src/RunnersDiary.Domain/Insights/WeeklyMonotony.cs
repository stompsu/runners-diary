namespace RunnersDiary.Domain.Insights;

/// <summary>Monotony and strain of one complete ISO week (Foster 1998; ADR 0024 §4).</summary>
public static class WeeklyMonotony
{
    /// <summary>
    /// Monotony = mean / σ over the 7 daily loads (zeros included, population σ); strain = weekly load × monotony.
    /// Both null when σ = 0. The scale of the input cancels in the ratio, so exact RPE·s values are used.
    /// </summary>
    public static (double? Monotony, double? Strain) Compute(IReadOnlyList<long> dailyRpeSeconds)
    {
        ArgumentNullException.ThrowIfNull(dailyRpeSeconds);
        if (dailyRpeSeconds.Count != 7)
        {
            throw new ArgumentException("A week has seven daily loads.", nameof(dailyRpeSeconds));
        }

        var sum = dailyRpeSeconds.Sum();
        var sumSquares = dailyRpeSeconds.Sum(x => x * x);

        // 49 σ² = 7 Σx² − (Σx)², exactly; mean / σ = Σx / sqrt(7 Σx² − (Σx)²).
        var scaledVariance = (7 * sumSquares) - (sum * sum);
        if (scaledVariance <= 0)
        {
            return (null, null);
        }

        var monotony = sum / Math.Sqrt(scaledVariance);
        var strain = sum / 60.0 * monotony;
        return (Rounding.Round(monotony, 2), Rounding.Round(strain, 1));
    }
}
