namespace RunnersDiary.Domain.Insights;

/// <summary>Thresholds of metrics v1 (ADR 0024). They are returned in every report so neither the UI nor the AI hard-codes them.</summary>
public sealed record InsightsThresholds(double AcwrLow, double AcwrHigh, double MonotonyHigh, int AcwrMinHistoryDays)
{
    public const decimal AcwrLowValue = 0.80m;
    public const decimal AcwrHighValue = 1.30m;
    public const double MonotonyHighValue = 2.0;

    /// <summary>The chronic window ends 7 days before the date and reaches back 35 days in total (acute 7 + chronic 28).</summary>
    public const int AcwrMinHistoryDaysValue = 35;

    public static InsightsThresholds V1 { get; } = new((double)AcwrLowValue, (double)AcwrHighValue, MonotonyHighValue, AcwrMinHistoryDaysValue);
}
