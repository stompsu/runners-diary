using System.ComponentModel.DataAnnotations;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Api.Features.Goals;

/// <summary>
/// Body of <c>POST /api/goals</c> and <c>PUT /api/goals/{goalId}</c> (full replace). Never carries a user id; unknown members are rejected.
/// The rules of ADR 0039 §2 need the caller's "today" and linked run, so the handler applies them (400 with camelCase paths).
/// </summary>
public sealed record GoalWriteRequest
{
    public required GoalType Type { get; init; }

    [StringLength(120)]
    public required string Title { get; init; }

    public required DateOnly TargetDate { get; init; }

    public required double DistanceMeters { get; init; }

    public double? ElevationGainMeters { get; init; }

    /// <summary>Required for a Performance goal, not allowed for a Distance goal; the implied pace must be 150-900 s/km.</summary>
    public int? TargetDurationSeconds { get; init; }

    public Surface? Surface { get; init; }

    /// <summary>Required for a Race goal.</summary>
    [StringLength(150)]
    public string? EventName { get; init; }

    public LocationDto? Location { get; init; }

    /// <summary>The caller's own planned race run dated on the target date.</summary>
    public Guid? RaceRunId { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    /// <summary>On create the default is Active; required on update.</summary>
    public GoalStatus? Status { get; init; }

    /// <summary>Required on update: the <c>version</c> of the <c>GoalDto</c> being replaced.</summary>
    public string? Version { get; init; }

    internal GoalValues ToValues() => new(
        Type,
        Title,
        TargetDate,
        DistanceMeters,
        ElevationGainMeters,
        TargetDurationSeconds,
        Surface,
        EventName,
        Location is { } l && GeoPoint.IsValid(l.Latitude, l.Longitude) ? new RunLocation(new GeoPoint(l.Latitude, l.Longitude), l.Name) : null,
        RaceRunId,
        Notes,
        Status ?? GoalStatus.Active);
}

public enum GoalPlanSource
{
    Model = 0,
    Demo = 1,
}

/// <summary>The metadata of a goal's current plan; present once a plan was generated (or seeded for the demo).</summary>
public sealed record GoalPlanDto
{
    public required GoalPlanSource Source { get; init; }

    public required DateTime GeneratedAt { get; init; }

    public required Guid GenerationId { get; init; }

    /// <summary>Model-written text: plain text only, never markup.</summary>
    public required string? Notes { get; init; }

    public required int? PromptVersion { get; init; }

    public required string? Model { get; init; }

    public required DateOnly HorizonEnd { get; init; }

    public required bool HorizonCapped { get; init; }

    /// <summary>A plan-relevant field of the goal changed after this plan was generated.</summary>
    public required bool Outdated { get; init; }

    public required int PendingCount { get; init; }

    public required int DismissedCount { get; init; }

    /// <summary>Runs with <c>goalId</c> = this goal.</summary>
    public required int AcceptedCount { get; init; }
}

public sealed record GoalDto
{
    public required Guid Id { get; init; }

    public required GoalType Type { get; init; }

    public required string Title { get; init; }

    public required DateOnly TargetDate { get; init; }

    public required double DistanceMeters { get; init; }

    public required double? ElevationGainMeters { get; init; }

    public required int? TargetDurationSeconds { get; init; }

    public required Surface? Surface { get; init; }

    public required string? EventName { get; init; }

    public required LocationDto? Location { get; init; }

    public required Guid? RaceRunId { get; init; }

    public required string? Notes { get; init; }

    public required GoalStatus Status { get; init; }

    /// <summary>Derived, never stored: short-term when the goal is at most 84 days away in the caller's zone.</summary>
    public required GoalTerm Term { get; init; }

    /// <summary>The current date of the linked race run (it may differ from <see cref="TargetDate"/> when the run was moved).</summary>
    public required DateOnly? RaceRunDate { get; init; }

    public required GoalPlanDto? Plan { get; init; }

    /// <summary>Opaque concurrency token (base64 rowversion); send it back as <c>version</c> on update.</summary>
    public required string Version { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public sealed record GoalListResponse
{
    public required IReadOnlyList<GoalDto> Items { get; init; }
}

/// <summary>The planning fields of a goal for the realistic-target estimate (no title, notes or links).</summary>
public sealed record EstimateGoalRequest
{
    public required GoalType Type { get; init; }

    public required double DistanceMeters { get; init; }

    public double? ElevationGainMeters { get; init; }

    public Surface? Surface { get; init; }

    public required DateOnly TargetDate { get; init; }

    public int? TargetDurationSeconds { get; init; }

    internal GoalSpec ToSpec() => new(Type, DistanceMeters, ElevationGainMeters, TargetDate, TargetDurationSeconds, Surface);
}

public sealed record EstimateBasisDto
{
    public required Guid RunId { get; init; }

    public required DateOnly Date { get; init; }

    public required EffortKind Kind { get; init; }

    public required double DistanceMeters { get; init; }

    public required int DurationSeconds { get; init; }

    public required double? ElevationGainMeters { get; init; }
}

public sealed record EstimatePredictionDto
{
    public required int DurationSeconds { get; init; }

    public required int PaceSecondsPerKm { get; init; }

    public required PredictionConfidence Confidence { get; init; }

    public required EstimateBasisDto Basis { get; init; }
}

public sealed record EstimateWarningDto
{
    /// <summary>One of <c>noRecentEffort</c>, <c>targetMuchFasterThanPredicted</c>, <c>tooLittleTime</c>, <c>lowWeeklyVolume</c>, <c>beyondPlanHorizon</c> (lowerCamel codes, like the invalid-gpx codes).</summary>
    public required string Code { get; init; }

    /// <summary>The figure behind the warning: percent faster, weeks short, or the weekly mean in metres; null when the code has none.</summary>
    public required double? Value { get; init; }
}

public sealed record GoalEstimateResponse
{
    public required DateOnly Today { get; init; }

    public required int DaysToGoal { get; init; }

    public required int WeeksToGoal { get; init; }

    public required GoalTerm Term { get; init; }

    public required int MinimumWeeks { get; init; }

    /// <summary>Mean running distance of the last four complete ISO weeks.</summary>
    public required double RecentWeeklyDistanceMeters { get; init; }

    public required EstimatePredictionDto? Prediction { get; init; }

    public required IReadOnlyList<EstimateWarningDto> Warnings { get; init; }
}
