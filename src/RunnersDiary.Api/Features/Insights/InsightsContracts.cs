using System.Text.Json.Serialization;
using RunnersDiary.Api.Features.Calendar;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Api.Features.Insights;

// Wire contract of GET /api/insights/report (M5 plan section 3.2). Units: SI (metres, seconds), load in AU, pace in s/km.
// Names that the contract spells in lower camel case are enum members with an explicit JSON name.

public enum BandDto
{
    [JsonStringEnumMemberName("low")]
    Low,

    [JsonStringEnumMemberName("optimal")]
    Optimal,

    [JsonStringEnumMemberName("high")]
    High,
}

public enum AcwrUnavailableReasonDto
{
    [JsonStringEnumMemberName("insufficientHistory")]
    InsufficientHistory,

    [JsonStringEnumMemberName("noChronicLoad")]
    NoChronicLoad,
}

public enum ComparisonKindDto
{
    [JsonStringEnumMemberName("previous")]
    Previous,

    [JsonStringEnumMemberName("previousYear")]
    PreviousYear,
}

public sealed record InsightsPeriodDto
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    /// <summary>min(to, today): everything "done" is cut here.</summary>
    public required DateOnly AsOf { get; init; }

    /// <summary>True when the period runs past today.</summary>
    public required bool IsPartial { get; init; }

    public required int Days { get; init; }

    /// <summary>Days of the period that have started; a partial period is compared over this many days.</summary>
    public required int ElapsedDays { get; init; }
}

public sealed record InsightsThresholdsDto
{
    public required double AcwrLow { get; init; }

    public required double AcwrHigh { get; init; }

    public required double MonotonyHigh { get; init; }

    public required int AcwrMinHistoryDays { get; init; }
}

public sealed record PeriodTotalsDto
{
    public required int RunCount { get; init; }

    public required double DistanceMeters { get; init; }

    public required long DurationSeconds { get; init; }

    public required double ElevationGainMeters { get; init; }

    public required double? AveragePaceSecondsPerKm { get; init; }

    public required double? LongestRunMeters { get; init; }

    /// <summary>Session load in AU (RPE x minutes), cross-training included.</summary>
    public required double Load { get; init; }

    public required int LoadSessionCount { get; init; }

    public required int ActiveDays { get; init; }

    public required int RestDays { get; init; }
}

public sealed record PlannedSummaryDto
{
    public required int RunCount { get; init; }

    public required double DistanceMeters { get; init; }

    public required long DurationSeconds { get; init; }

    public required int UpcomingCount { get; init; }

    public required int MissedCount { get; init; }
}

public sealed record DeltaDto
{
    public required double Absolute { get; init; }

    /// <summary>Null when the previous value is 0.</summary>
    public required double? Percent { get; init; }
}

public sealed record DeltasDto
{
    public required DeltaDto RunCount { get; init; }

    public required DeltaDto DistanceMeters { get; init; }

    public required DeltaDto DurationSeconds { get; init; }

    public required DeltaDto ElevationGainMeters { get; init; }

    public required DeltaDto Load { get; init; }

    public required DeltaDto ActiveDays { get; init; }
}

public sealed record InsightsComparisonDto
{
    public required ComparisonKindDto Kind { get; init; }

    public required DateOnly From { get; init; }

    /// <summary>Already aligned to the elapsed days when the current period is partial.</summary>
    public required DateOnly To { get; init; }

    public required PeriodTotalsDto Totals { get; init; }

    public required DeltasDto Deltas { get; init; }
}

public sealed record AcwrPointDto
{
    public required DateOnly Date { get; init; }

    public required double? Value { get; init; }

    public required BandDto? Band { get; init; }

    public required double Acute { get; init; }

    public required double? Chronic { get; init; }

    public required AcwrUnavailableReasonDto? UnavailableReason { get; init; }
}

public sealed record BandDaysDto
{
    public required int Low { get; init; }

    public required int Optimal { get; init; }

    public required int High { get; init; }

    public required int Unavailable { get; init; }
}

public sealed record DailyLoadDto
{
    public required DateOnly Date { get; init; }

    public required double Load { get; init; }

    public required double Acute { get; init; }

    public required double? Chronic { get; init; }

    public required double? Acwr { get; init; }

    public required BandDto? Band { get; init; }
}

public sealed record InsightsLoadDto
{
    public required DateOnly? HistoryStart { get; init; }

    public required AcwrPointDto AtAsOf { get; init; }

    public required AcwrPointDto? Peak { get; init; }

    public required BandDaysDto BandDays { get; init; }

    public required IReadOnlyList<DailyLoadDto> Daily { get; init; }
}

public sealed record InsightsWeekDto
{
    public required int IsoYear { get; init; }

    public required int IsoWeek { get; init; }

    public required DateOnly Start { get; init; }

    public required DateOnly End { get; init; }

    public required TotalsDto Completed { get; init; }

    public required TotalsDto Planned { get; init; }

    public required double Load { get; init; }

    public required double? Monotony { get; init; }

    public required double? Strain { get; init; }

    public required double? AcwrAtEnd { get; init; }

    /// <summary>True when the week's Sunday is not after the as-of date (only complete weeks have monotony).</summary>
    public required bool Complete { get; init; }
}

public sealed record InsightsMonthDto
{
    public required int Year { get; init; }

    public required int Month { get; init; }

    public required DateOnly Start { get; init; }

    public required DateOnly End { get; init; }

    public required TotalsDto Completed { get; init; }

    public required TotalsDto Planned { get; init; }

    public required double Load { get; init; }
}

public sealed record TypeShareDto
{
    public required int RunTypeId { get; init; }

    public required string Code { get; init; }

    public required RunTypeCategory Category { get; init; }

    public required int RunCount { get; init; }

    public required double DistanceMeters { get; init; }

    public required long DurationSeconds { get; init; }

    public required double Load { get; init; }
}

public sealed record PaceWeekDto
{
    public required DateOnly Start { get; init; }

    public required int RunCount { get; init; }

    public required double DistanceMeters { get; init; }

    public required long DurationSeconds { get; init; }

    public required double PaceSecondsPerKm { get; init; }

    public required double? AvgHeartRate { get; init; }
}

public sealed record PaceTrendDto
{
    public required IReadOnlyList<string> RunTypeCodes { get; init; }

    /// <summary>Negative = getting faster. Null with fewer than 4 weekly points.</summary>
    public required double? SlopeSecondsPerKmPerWeek { get; init; }

    public required int ExcludedRunCount { get; init; }

    public required IReadOnlyList<PaceWeekDto> Weeks { get; init; }
}

public sealed record RaceResultDto
{
    public required Guid RunId { get; init; }

    public required DateOnly Date { get; init; }

    public required RunStatus Status { get; init; }

    public required string RunTypeCode { get; init; }

    public required string Name { get; init; }

    public required RaceDistanceCategory DistanceCategory { get; init; }

    public required double? OfficialDistanceMeters { get; init; }

    public required double? DistanceMeters { get; init; }

    public required int? ResultTimeSeconds { get; init; }

    public required int? TargetTimeSeconds { get; init; }

    public required double? PaceSecondsPerKm { get; init; }

    public required int? OverallPlace { get; init; }

    public required int? CategoryPlace { get; init; }

    public required string? CategoryName { get; init; }

    public required int? RatingStars { get; init; }

    /// <summary>A personal best at the time of the race (fastest completed result in the distance on or before its date).</summary>
    public required bool IsPersonalBest { get; init; }
}

public sealed record DataQualityDto
{
    public required int CompletedSessions { get; init; }

    public required int SessionsWithEstimatedRpe { get; init; }

    /// <summary>Share of the period's load that comes from default-RPE estimates, 0..1 (2 decimals).</summary>
    public required double EstimatedLoadShare { get; init; }

    public required int SessionsWithoutDuration { get; init; }

    public required int RunningSessionsWithoutDistance { get; init; }
}

/// <summary>Deterministic training metrics of one period (metrics v1, ADR 0024). The M6 summary narrates exactly these numbers.</summary>
public sealed record InsightsReportResponse
{
    public required int MetricsVersion { get; init; }

    /// <summary>Today in the caller's profile zone.</summary>
    public required DateOnly Today { get; init; }

    public required InsightsPeriodDto Period { get; init; }

    public required InsightsThresholdsDto Thresholds { get; init; }

    public required PeriodTotalsDto Totals { get; init; }

    public required PlannedSummaryDto Planned { get; init; }

    public required InsightsComparisonDto? Comparison { get; init; }

    public required InsightsLoadDto Load { get; init; }

    public required IReadOnlyList<InsightsWeekDto> Weeks { get; init; }

    public required IReadOnlyList<InsightsMonthDto> Months { get; init; }

    public required IReadOnlyList<TypeShareDto> TypeDistribution { get; init; }

    public required PaceTrendDto PaceTrend { get; init; }

    public required IReadOnlyList<RaceResultDto> Races { get; init; }

    public required DataQualityDto DataQuality { get; init; }
}
