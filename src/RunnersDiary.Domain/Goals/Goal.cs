using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Goals;

/// <summary>The metadata a plan generation writes on its goal (ADR 0040 §1 step 10, ADR 0041 §2). The demo plan uses <see cref="Goal.DemoModelDeployment"/> and no prompt version.</summary>
public sealed record GoalPlanRecord(
    Guid GenerationId,
    DateTimeOffset GeneratedAt,
    string? Notes,
    short? PromptVersion,
    string ModelDeployment,
    string? ModelId,
    DateOnly HorizonEnd,
    bool HorizonCapped);

/// <summary>
/// A training goal (ADR 0039). One row per goal, owned by one user. Links to runs (<see cref="RaceRunId"/>) are plain ids whose
/// database FKs are composite and NO ACTION; the application clears them on delete (ADR 0039 §3).
/// </summary>
public sealed class Goal : IUserOwned
{
    /// <summary><see cref="PlanModelDeployment"/> of the seeded example plan (ADR 0041 §5).</summary>
    public const string DemoModelDeployment = "demo";

    private Goal()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public GoalType Type { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public DateOnly TargetDate { get; private set; }

    public double DistanceMeters { get; private set; }

    public int? TargetDurationSeconds { get; private set; }

    public double? ElevationGainMeters { get; private set; }

    public Surface? Surface { get; private set; }

    public string? EventName { get; private set; }

    public RunLocation? Location { get; private set; }

    /// <summary>The caller's planned race run this goal points at (same owner by composite FK); cleared by the application when that run is deleted.</summary>
    public Guid? RaceRunId { get; private set; }

    public string? Notes { get; private set; }

    public GoalStatus Status { get; private set; }

    public DateTime? PlanGeneratedAt { get; private set; }

    public Guid? PlanGenerationId { get; private set; }

    public string? PlanNotes { get; private set; }

    public short? PlanPromptVersion { get; private set; }

    public string? PlanModelDeployment { get; private set; }

    public string? PlanModelId { get; private set; }

    public DateOnly? PlanHorizonEnd { get; private set; }

    public bool PlanHorizonCapped { get; private set; }

    /// <summary>True when a plan-relevant field changed after the plan was generated (ADR 0039 §1); the suggestions stay and the UI offers "Regenerate".</summary>
    public bool PlanOutdated { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Concurrency token (SQL rowversion); exposed to the API as <c>version</c>.</summary>
#pragma warning disable CA1819 // Justification: rowversion is a byte[] by EF/SQL Server contract.
    public byte[] RowVersion { get; private set; } = [];
#pragma warning restore CA1819

    public bool HasPlan => PlanGeneratedAt is not null;

    public bool IsDemoPlan => PlanModelDeployment == DemoModelDeployment;

    /// <param name="today">The caller's local today (the target-date window is checked against it).</param>
    /// <param name="raceRun">The caller's run named by <c>values.RaceRunId</c>, or null.</param>
    public static Goal Create(Guid userId, GoalValues values, DateOnly today, LinkedRaceRun? raceRun, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException("A goal needs an owner.");
        }

        GoalRules.EnsureValid(values, today, previousTargetDate: null, raceRun);
        var goal = new Goal { Id = Guid.CreateVersion7(), UserId = userId, CreatedAt = Utc(now) };
        goal.Apply(values, now);
        return goal;
    }

    /// <summary>Full replace. Sets <see cref="PlanOutdated"/> when a plan-relevant field changed on a goal that has a plan.</summary>
    public void Update(GoalValues values, DateOnly today, LinkedRaceRun? raceRun, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);
        GoalRules.EnsureValid(values, today, TargetDate, raceRun);

        var planFieldChanged = Type != values.Type
            || TargetDate != values.TargetDate
            || DistanceMeters != values.DistanceMeters
            || ElevationGainMeters != values.ElevationGainMeters
            || TargetDurationSeconds != values.TargetDurationSeconds
            || Surface != values.Surface;
        Apply(values, now);
        if (planFieldChanged && HasPlan)
        {
            PlanOutdated = true;
        }
    }

    /// <summary>Stores the metadata of a fresh generation and clears <see cref="PlanOutdated"/>.</summary>
    public void RecordPlan(GoalPlanRecord plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Notes is { Length: > 600 })
        {
            throw new DomainRuleException("Plan notes are at most 600 characters.");
        }

        PlanGeneratedAt = Utc(plan.GeneratedAt);
        PlanGenerationId = plan.GenerationId;
        PlanNotes = plan.Notes;
        PlanPromptVersion = plan.PromptVersion;
        PlanModelDeployment = plan.ModelDeployment;
        PlanModelId = plan.ModelId is { Length: > 100 } id ? id[..100] : plan.ModelId;
        PlanHorizonEnd = plan.HorizonEnd;
        PlanHorizonCapped = plan.HorizonCapped;
        PlanOutdated = false;
        UpdatedAt = Utc(now);
    }

    public void MarkPlanOutdated(DateTimeOffset now)
    {
        if (HasPlan && !PlanOutdated)
        {
            PlanOutdated = true;
            UpdatedAt = Utc(now);
        }
    }

    /// <summary>Clears the race-run link (the linked run was deleted).</summary>
    public void ClearRaceRun() => RaceRunId = null;

    private void Apply(GoalValues v, DateTimeOffset now)
    {
        Type = v.Type;
        Title = v.Title;
        TargetDate = v.TargetDate;
        DistanceMeters = v.DistanceMeters;
        ElevationGainMeters = v.ElevationGainMeters;
        TargetDurationSeconds = v.TargetDurationSeconds;
        Surface = v.Surface;
        EventName = v.EventName;
        Location = v.Location;
        RaceRunId = v.RaceRunId;
        Notes = v.Notes;
        Status = v.Status;
        UpdatedAt = Utc(now);
    }

    private static DateTime Utc(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }
}
