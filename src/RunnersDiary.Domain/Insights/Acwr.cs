namespace RunnersDiary.Domain.Insights;

public enum AcwrBand
{
    Low = 0,
    Optimal = 1,
    High = 2,
}

public enum AcwrUnavailableReason
{
    InsufficientHistory = 0,
    NoChronicLoad = 1,
}

/// <summary>ACWR on one date. <see cref="Chronic"/> is null while the history is insufficient.</summary>
public sealed record AcwrPoint(
    DateOnly Date,
    double? Value,
    AcwrBand? Band,
    double Acute,
    double? Chronic,
    AcwrUnavailableReason? UnavailableReason);

/// <summary>The ratio and its bands (ADR 0024 §3).</summary>
public static class Acwr
{
    /// <summary>
    /// ACWR from exact RPE·s sums: acute = last 7 days, chronic = the 28 days before them, expressed per 7 days
    /// (so the ratio is 4 × acute / chronic). Rounded to 2 decimals, midpoints away from zero. Null when the chronic sum is 0.
    /// </summary>
    public static decimal? Ratio(long acuteSum, long chronicSum) =>
        chronicSum <= 0 ? null : Math.Round((decimal)(4 * acuteSum) / chronicSum, 2, MidpointRounding.AwayFromZero);

    /// <summary>Band of an already rounded ratio: low &lt; 0.80 ≤ optimal ≤ 1.30 &lt; high.</summary>
    public static AcwrBand BandOf(decimal roundedRatio) =>
        roundedRatio < InsightsThresholds.AcwrLowValue ? AcwrBand.Low
        : roundedRatio > InsightsThresholds.AcwrHighValue ? AcwrBand.High
        : AcwrBand.Optimal;
}
