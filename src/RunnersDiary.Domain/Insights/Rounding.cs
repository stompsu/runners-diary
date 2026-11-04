namespace RunnersDiary.Domain.Insights;

/// <summary>Output rounding (ADR 0024 §9): midpoints away from zero, applied only at the boundary; never returns negative zero.</summary>
internal static class Rounding
{
    public static double Round(double value, int decimals)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    public static double Round(decimal value, int decimals)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : (double)rounded;
    }

    /// <summary>AU (0.1) from an exact RPE·seconds integer.</summary>
    public static double Au(long rpeSeconds) => Round(rpeSeconds / 60m, 1);
}
