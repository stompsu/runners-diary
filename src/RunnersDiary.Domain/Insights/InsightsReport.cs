using RunnersDiary.Domain.Calendar;

namespace RunnersDiary.Domain.Insights;

public sealed record PeriodInfo(DateOnly From, DateOnly To, DateOnly AsOf, bool IsPartial, int Days, int ElapsedDays);

public sealed record PlannedSummary(int RunCount, double DistanceMeters, long DurationSeconds, int UpcomingCount, int MissedCount);

public sealed record ComparisonResult(ComparisonKind Kind, DateOnly From, DateOnly To, PeriodTotals Totals, Deltas Deltas);

public sealed record BandDays(int Low, int Optimal, int High, int Unavailable);

public sealed record DailyLoadPoint(DateOnly Date, double Load, double Acute, double? Chronic, double? Acwr, AcwrBand? Band);

public sealed record LoadSection(
    DateOnly? HistoryStart,
    AcwrPoint AtAsOf,
    AcwrPoint? Peak,
    BandDays BandDays,
    IReadOnlyList<DailyLoadPoint> Daily);

public sealed record WeekBucket(
    int IsoYear,
    int IsoWeek,
    DateOnly Start,
    DateOnly End,
    Totals Completed,
    Totals Planned,
    double Load,
    double? Monotony,
    double? Strain,
    double? AcwrAtEnd,
    bool Complete);

public sealed record MonthBucket(int Year, int Month, DateOnly Start, DateOnly End, Totals Completed, Totals Planned, double Load);

public sealed record DataQuality(
    int CompletedSessions,
    int SessionsWithEstimatedRpe,
    double EstimatedLoadShare,
    int SessionsWithoutDuration,
    int RunningSessionsWithoutDistance);

/// <summary>The deterministic metrics of one period (ADR 0024/0025). The API DTO and the M6 tool payloads are mappings of it.</summary>
public sealed record InsightsReport(
    int MetricsVersion,
    DateOnly Today,
    PeriodInfo Period,
    InsightsThresholds Thresholds,
    PeriodTotals Totals,
    PlannedSummary Planned,
    ComparisonResult? Comparison,
    LoadSection Load,
    IReadOnlyList<WeekBucket> Weeks,
    IReadOnlyList<MonthBucket> Months,
    IReadOnlyList<TypeShare> TypeDistribution,
    PaceTrend PaceTrend,
    IReadOnlyList<RaceResult> Races,
    DataQuality DataQuality);
