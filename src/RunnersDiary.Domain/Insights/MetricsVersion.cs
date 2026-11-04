namespace RunnersDiary.Domain.Insights;

/// <summary>
/// The version of the metric definitions (ADR 0024). Any change to a definition, default RPE, threshold, window or rounding rule
/// bumps it; additive report fields do not.
/// </summary>
public static class MetricsVersion
{
    public const int Current = 1;
}
