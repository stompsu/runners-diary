using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>The goal as the model sees it: planning fields only. Never the title, event name, notes or location (ADR 0040 §3).</summary>
/// <param name="PredictionBasis">v3 (M9.1): what the prediction came from (a race, a hard training run or the recent-training fallback); null without a prediction. Never the basis run id.</param>
/// <param name="PredictionConfidence">v3: the prediction's confidence; null without a prediction.</param>
/// <param name="FitnessPaceSecondsPerKm">v3: the prediction's pace (the pace current fitness supports); null without a prediction.</param>
/// <param name="AmbitiousTarget">v3: the target is more than 5 % faster than the prediction (<see cref="GoalEstimator.IsAmbitiousTarget"/>).</param>
public sealed record PlanGoalFacts(
    GoalType Type,
    double DistanceMeters,
    double? ElevationGainMeters,
    Surface? Surface,
    DateOnly TargetDate,
    int? TargetDurationSeconds,
    int? TargetPaceSecondsPerKm,
    int? PredictedDurationSeconds,
    int WeeksToGoal,
    EffortKind? PredictionBasis = null,
    PredictionConfidence? PredictionConfidence = null,
    int? FitnessPaceSecondsPerKm = null,
    bool AmbitiousTarget = false);

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

/// <param name="RecentEasyPaceSecondsPerKm">v3 (M9.1): <see cref="TrainingHistory.RecentEasyPaceSecondsPerKm"/>, the runner's recent easy pace.</param>
public sealed record PlanHistoryFacts(
    IReadOnlyList<PlanHistoryWeek> Weeks,
    IReadOnlyList<DayOfWeek> UsualWeekdays,
    double SessionsPerWeek,
    PlanAcwrFact LatestAcwr,
    IReadOnlyList<PlanRaceFact> Races,
    double? RecentEasyPaceSecondsPerKm = null);

/// <summary>
/// The plan fact sheet (v1 ADR 0040 §3; v2 = caps rounded down to 500 m, ADR 0040 amendment "plan reliability"; v3 = M9.1 fitness facts and the
/// easy-pace bound, ADR 0040 amendment 2026-11-21): the only data the plan model sees. Metrics, dates and codes only: no title, event name, notes,
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
    /// <summary>
    /// v3 (M9.1): <c>goal.predictionBasis</c>, <c>predictionConfidence</c>, <c>fitnessPaceSecondsPerKm</c>, <c>ambitiousTarget</c>;
    /// <c>history.recentEasyPaceSecondsPerKm</c>; <c>envelope.easyPaceMinSecondsPerKm</c> (envelope v3: the reference pace is the slower of target and
    /// fitness). v2: the envelope caps are rounded down to 500 m (<see cref="PlanEnvelopeRules.CapStepMeters"/>).
    /// </summary>
    public const int CurrentVersion = 3;
    public const int MaxCommitments = 200;
    public const int MaxDismissed = 60;
    public const int MaxRaces = 6;
}
