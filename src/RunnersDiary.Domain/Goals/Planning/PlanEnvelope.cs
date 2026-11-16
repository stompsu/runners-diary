namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// One ISO week of the plan horizon. <paramref name="Start"/> is the Monday, also for a partial first week; the plannable days are
/// <c>[max(Start, planStart), min(Start + 6, planEnd)]</c>.
/// </summary>
/// <param name="MaxDistanceMeters">The weekly cap for running distance, including commitments (scaled by days/7 in a partial first week), rounded down to 500 m.</param>
/// <param name="FixedDistanceMeters">Distance of commitments in the week that count as running (Race and Training categories).</param>
/// <param name="AvailableDistanceMeters"><c>max(0, cap - fixed)</c>, rounded down to 500 m.</param>
/// <param name="MaxLongRunMeters">The longest single session allowed in the week, rounded down to 500 m.</param>
/// <param name="FixedRunningSessions">Commitments in the week that count as running sessions (for the frequency rule).</param>
public sealed record PlanWeek(
    DateOnly Start,
    TrainingPhase Phase,
    double MaxDistanceMeters,
    double FixedDistanceMeters,
    double AvailableDistanceMeters,
    double MaxLongRunMeters,
    int FixedRunningSessions);

/// <summary>The deterministic bounds a generated plan must stay inside (ADR 0040 §2). Everything here is computed in code, never by the model.</summary>
/// <param name="TotalWeeks"><c>W</c>: ISO weeks from the week of <see cref="PlanStart"/> to the week of the goal, inclusive (the phases span all of them).</param>
public sealed record PlanEnvelope(
    int Version,
    DateOnly PlanStart,
    DateOnly PlanEnd,
    bool HorizonCapped,
    int TotalWeeks,
    int TaperWeeks,
    int MaxSessionsPerWeek,
    int? ReferencePaceSecondsPerKm,
    double BaseVolumeMeters,
    IReadOnlyList<PlanWeek> Weeks,
    IReadOnlyList<string> AllowedRunTypes)
{
    /// <summary>False when no day is left to plan (the goal is on or before the first plannable day).</summary>
    public bool HasPlannableDays => PlanEnd >= PlanStart;
}
