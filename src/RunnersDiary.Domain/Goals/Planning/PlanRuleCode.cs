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
    /// <summary>No longer produced: the repair step sets each session's phase from its week.</summary>
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
/// <para>
/// <see cref="Value"/> and <see cref="Limit"/> are integers computed by the validator (ADR 0040 amendment "plan reliability"), set for
/// <c>weeklyVolume</c> (the week's suggested running distance and its <c>availableDistanceMeters</c>), <c>longRun</c> (the session's distance and
/// the week's <c>maxLongRunMeters</c>) and a too-wide pace range (<c>schema</c> at <c>paceMaxSecondsPerKm</c>: the spread and
/// the run type's limit from <see cref="TrainingSuggestion.MaxPaceSpreadSeconds"/>), so the retry note can say by how much. Numbers only, never text.
/// </para>
/// </summary>
public sealed record PlanViolation(PlanRuleCode Rule, string Location, int? Value = null, int? Limit = null);

/// <summary>The outcome of <see cref="TrainingPlanValidator"/>.</summary>
/// <param name="Kept">The sessions that passed the per-session checks and the collision drop, by date (what would be stored).</param>
/// <param name="DroppedCount">Sessions dropped by the item-level rules: <c>collision</c> and the <c>taperIntensity</c>/<c>pace</c> repairs.</param>
/// <param name="Notes">The model's notes, trimmed; null when blank.</param>
public sealed record PlanValidation(IReadOnlyList<PlanViolation> Violations, IReadOnlyList<PlannedSession> Kept, int DroppedCount, string? Notes)
{
    /// <summary>What the repair step changed or dropped (<see cref="TrainingPlanRepair"/>); never model text. Empty for a plan that needed nothing.</summary>
    public IReadOnlyList<PlanRepair> Repairs { get; init; } = [];

    public bool IsValid => Violations.Count == 0;

    /// <summary>The distinct rule codes, in rule order.</summary>
    public IReadOnlyList<PlanRuleCode> Codes => [.. Violations.Select(v => v.Rule).Distinct().Order()];
}

/// <summary>What a repair did to a session or a week (<see cref="PlanRepair"/>).</summary>
public enum PlanRepairAction
{
    /// <summary>A value was replaced (the phase).</summary>
    Set,

    /// <summary>A value was cut down to its limit (pace spread, long run).</summary>
    Clamp,

    /// <summary>The session was left out (counted in the dropped count).</summary>
    Drop,

    /// <summary>A week's suggested distances were scaled down together.</summary>
    Scale,
}

/// <summary>
/// One repair, for diagnostics. <see cref="Rule"/> is the rule that would have rejected the plan, <see cref="Location"/> a session date, an envelope week
/// start or a fixed path (like <see cref="PlanViolation"/>), <see cref="From"/>/<see cref="To"/> app-computed integers (meters, seconds per km, phase
/// codes; for a <see cref="PlanRepairAction.Scale"/> the week's suggested meters before and after). Never model text.
/// </summary>
public sealed record PlanRepair(PlanRuleCode Rule, string Location, PlanRepairAction Action, int? From = null, int? To = null);
