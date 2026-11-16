namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// The plan-level rule codes of ADR 0040 §5 (any violation rejects the plan and triggers the one retry). <see cref="PlanRuleCodes.ToCode"/> gives
/// the wire/log names. The item-level <c>collision</c> rule is not here: it drops a session and counts it, it never rejects a plan.
/// </summary>
public enum PlanRuleCode
{
    Schema,
    DateRange,
    SessionsPerDay,
    Phase,
    WeeklyVolume,
    LongRun,
    Frequency,
    HardSessions,
    TaperIntensity,
    Pace,
    EmptyWeek,
    Markup,
    ForbiddenTopic,
    Empty,
}

public static class PlanRuleCodes
{
    public static string ToCode(this PlanRuleCode code) => code switch
    {
        PlanRuleCode.Schema => "schema",
        PlanRuleCode.DateRange => "dateRange",
        PlanRuleCode.SessionsPerDay => "sessionsPerDay",
        PlanRuleCode.Phase => "phase",
        PlanRuleCode.WeeklyVolume => "weeklyVolume",
        PlanRuleCode.LongRun => "longRun",
        PlanRuleCode.Frequency => "frequency",
        PlanRuleCode.HardSessions => "hardSessions",
        PlanRuleCode.TaperIntensity => "taperIntensity",
        PlanRuleCode.Pace => "pace",
        PlanRuleCode.EmptyWeek => "emptyWeek",
        PlanRuleCode.Markup => "markup",
        PlanRuleCode.ForbiddenTopic => "forbiddenTopic",
        PlanRuleCode.Empty => "empty",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown plan rule code."),
    };
}

/// <summary>
/// One plan violation. <see cref="Location"/> is never model text: a fixed path (<c>notes</c>, <c>sessions</c>, <c>sessions[3].distanceMeters</c>,
/// <c>draft</c>), a session date that parsed as <c>yyyy-MM-dd</c>, or an envelope week start. So it can be logged and sent back to the model safely.
/// </summary>
public sealed record PlanViolation(PlanRuleCode Rule, string Location);

/// <summary>The outcome of <see cref="TrainingPlanValidator"/>.</summary>
/// <param name="Kept">The sessions that passed the per-session checks and the collision drop, by date (what would be stored).</param>
/// <param name="DroppedCount">Sessions dropped by the item-level <c>collision</c> rule.</param>
/// <param name="Notes">The model's notes, trimmed; null when blank.</param>
public sealed record PlanValidation(IReadOnlyList<PlanViolation> Violations, IReadOnlyList<PlannedSession> Kept, int DroppedCount, string? Notes)
{
    public bool IsValid => Violations.Count == 0;

    /// <summary>The distinct rule codes, in rule order.</summary>
    public IReadOnlyList<PlanRuleCode> Codes => [.. Violations.Select(v => v.Rule).Distinct().Order()];
}
