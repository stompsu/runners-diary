namespace RunnersDiary.Domain.Goals.Planning;

public enum CommitmentKind : byte
{
    /// <summary>A planned (or, rarely, already completed) run on a future date, including runs accepted from this goal's suggestions.</summary>
    Planned = 0,

    /// <summary>A race-category planned run (also past the plan horizon, so the model knows an event is coming).</summary>
    Race = 1,

    /// <summary>The target date of another Active goal; it has no run type.</summary>
    OtherGoal = 2,
}

/// <summary>
/// Something that is already in the diary and that a plan must work around (ADR 0040 §2). No title, notes or location: only what the planning needs.
/// </summary>
/// <param name="RunTypeId">Null for <see cref="CommitmentKind.OtherGoal"/>.</param>
/// <param name="FromThisGoal">The run was accepted from this goal's own suggestions (<c>Runs.GoalId</c>).</param>
public sealed record CommitmentFact(
    DateOnly Date,
    short? RunTypeId,
    double? DistanceMeters,
    int? DurationSeconds,
    CommitmentKind Kind,
    bool FromThisGoal);

/// <summary>A date and run type the user declined in this goal's previous plan (a dismissed suggestion): a hint for one generation.</summary>
public sealed record DismissedHint(DateOnly Date, short RunTypeId);
