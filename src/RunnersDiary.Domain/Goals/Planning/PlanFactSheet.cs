using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>The goal as the model sees it: planning fields only. Never the title, event name, notes or location (ADR 0040 §3).</summary>
public sealed record PlanGoalFacts(
    GoalType Type,
    double DistanceMeters,
    double? ElevationGainMeters,
    Surface? Surface,
    DateOnly TargetDate,
    int? TargetDurationSeconds,
    int? TargetPaceSecondsPerKm,
    int? PredictedDurationSeconds,
    int WeeksToGoal);

public sealed record PlanHistoryWeek(
    DateOnly Start,
    double DistanceMeters,
    long DurationSeconds,
    int Sessions,
    double LongestRunMeters,
    int HardSessions,
    double Load,
    double? AcwrAtEnd,
    AcwrBand? AcwrBand);

public sealed record PlanAcwrFact(double? Value, AcwrBand? Band);

public sealed record PlanRaceFact(DateOnly Date, double DistanceMeters, int DurationSeconds, string RunType);

public sealed record PlanHistoryFacts(
    IReadOnlyList<PlanHistoryWeek> Weeks,
    IReadOnlyList<DayOfWeek> UsualWeekdays,
    double SessionsPerWeek,
    PlanAcwrFact LatestAcwr,
    IReadOnlyList<PlanRaceFact> Races);

/// <summary>
/// The plan fact sheet (v1 ADR 0040 §3; v2 = caps rounded down to 500 m, ADR 0040 amendment "plan reliability"): the only data the plan model sees. Metrics, dates and codes only: no title, event name, notes,
/// location, run titles, gear, weather, heart rate, ids or names. A change to the projection or to the envelope constants bumps
/// <see cref="CurrentVersion"/>.
/// </summary>
public sealed record PlanFactSheet(
    int FactSheetVersion,
    DateOnly Today,
    string Language,
    PlanGoalFacts Goal,
    PlanHistoryFacts History,
    IReadOnlyList<CommitmentFact> Commitments,
    IReadOnlyList<DismissedHint> Dismissed,
    PlanEnvelope Envelope)
{
    /// <summary>v2: the envelope caps are rounded down to 500 m (<see cref="PlanEnvelopeRules.CapStepMeters"/>); the shape is unchanged.</summary>
    public const int CurrentVersion = 2;
    public const int MaxCommitments = 200;
    public const int MaxDismissed = 60;
    public const int MaxRaces = 6;
}
