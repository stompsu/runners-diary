using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Goals;

/// <summary>The writable part of a goal: the single input shape for <see cref="GoalRules"/>, <see cref="Goal.Create"/> and <see cref="Goal.Update"/>.</summary>
public sealed record GoalValues(
    GoalType Type,
    string Title,
    DateOnly TargetDate,
    double DistanceMeters,
    double? ElevationGainMeters,
    int? TargetDurationSeconds,
    Surface? Surface,
    string? EventName,
    RunLocation? Location,
    Guid? RaceRunId,
    string? Notes,
    GoalStatus Status);

/// <summary>The run a goal's <c>RaceRunId</c> points at, as the caller's own data shows it; null when the id is unknown or foreign.</summary>
public sealed record LinkedRaceRun(Guid Id, DateOnly Date, RunStatus Status, bool IsRaceCategory);
