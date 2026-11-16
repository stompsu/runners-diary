using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Infrastructure.Goals;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Ai;

public enum PlanGenerateStatus
{
    /// <summary>200: the new suggestion set is stored.</summary>
    Ok,

    /// <summary>404: unknown or foreign goal.</summary>
    NotFound,

    /// <summary>400 <c>errors.goalId</c>: the goal is not Active or no plannable day is left.</summary>
    Invalid,

    /// <summary>503 <c>feature-unavailable</c>: no model, <c>Ai:Enabled</c> or <c>Ai:Plan:Enabled</c> off, or no token for the model endpoint.</summary>
    Unavailable,

    /// <summary>422 <c>insufficient-history</c>.</summary>
    InsufficientHistory,

    /// <summary>409 <c>plan-other-goal-active</c> with <see cref="PlanGenerateOutcome.OtherGoalId"/>.</summary>
    OtherGoalActive,

    /// <summary>409 <c>plan-in-progress</c>.</summary>
    InProgress,

    /// <summary>429 <c>ai-quota</c> with <see cref="PlanGenerateOutcome.Quota"/>.</summary>
    QuotaExceeded,

    /// <summary>503 <c>ai-busy</c>, with the provider's <see cref="PlanGenerateOutcome.RetryAfter"/> when it throttled.</summary>
    Busy,

    /// <summary>502 <c>plan-failed</c> with <see cref="PlanGenerateOutcome.Failure"/>.</summary>
    Failed,

    /// <summary>409 <c>concurrency-conflict</c>: the goal changed (or left Active) while the model was working; the quota is spent.</summary>
    Conflict,
}

/// <summary>The outcome of <see cref="TrainingPlanService.GenerateAsync"/>; the members that belong to <see cref="Status"/> are set.</summary>
public sealed record PlanGenerateOutcome(
    PlanGenerateStatus Status,
    GoalView? Goal = null,
    IReadOnlyList<TrainingSuggestion>? Suggestions = null,
    int DroppedCount = 0,
    IReadOnlyList<Guid>? ReplacedGoalIds = null,
    Guid? OtherGoalId = null,
    QuotaStatus? Quota = null,
    GenerationFailure? Failure = null,
    TimeSpan? RetryAfter = null,
    IReadOnlyList<DomainError>? Errors = null);

/// <summary>
/// Plan generation for one of the caller's goals (ADR 0040 §1, ADR 0041 §2). Every read goes through the <c>Owner</c> filter (a foreign goal is
/// "not found"); the user id for quotas and rows is the caller's. The inputs (history, commitments, envelope, fact sheet) are deterministic and
/// metrics-only; the model chooses sessions inside the envelope and <see cref="TrainingPlanValidator"/> checks every number. The commit replaces
/// the goal's Pending and Dismissed suggestions (and with <c>replaceOtherGoals</c> every other goal's suggestions) in one transaction and never
/// touches a run.
/// </summary>
public sealed partial class TrainingPlanService(
    RunnersDiaryDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    TrainingHistoryReader reader,
    GoalService goals,
    TrainingPlanGenerator generator,
    ILogger<TrainingPlanService> logger)
{
    /// <summary>Completed running sessions needed in the last 84 days (ADR 0040 §1 step 3).</summary>
    public const int MinRecentSessions = 3;

    /// <summary>The goal must be at least this many days ahead: one plannable day between today and the goal day.</summary>
    public const int MinDaysAhead = 2;

    public const string Language = "en";

    public async Task<PlanGenerateOutcome> GenerateAsync(Guid goalId, bool replaceOtherGoals, CancellationToken cancellationToken)
    {
        // 1. The goal (404, then 400).
        var (_, today) = await CallerClock.GetAsync(db, currentUser, clock, cancellationToken);
        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == goalId, cancellationToken);
        if (goal is null)
        {
            return new PlanGenerateOutcome(PlanGenerateStatus.NotFound);
        }

        if (goal.Status != GoalStatus.Active)
        {
            return Invalid("Only an active goal can get suggested trainings.");
        }

        if (goal.TargetDate < today.AddDays(MinDaysAhead))
        {
            return Invalid("The goal is too close: no day is left to plan before it.");
        }

        // 2. The model and the switches (read per request).
        if (!generator.IsAvailable)
        {
            return new PlanGenerateOutcome(PlanGenerateStatus.Unavailable);
        }

        // 3. History: no model call and no quota below the minimum.
        var facts = await reader.ReadFactsAsync(today, cancellationToken);
        var history = TrainingHistory.From(facts, today);
        if (history.RunningSessionsLast84Days < MinRecentSessions)
        {
            return new PlanGenerateOutcome(PlanGenerateStatus.InsufficientHistory);
        }

        // 4. One goal with pending suggestions at a time.
        if (await OtherGoalWithPendingAsync(goalId, cancellationToken) is { } other && !replaceOtherGoals)
        {
            return new PlanGenerateOutcome(PlanGenerateStatus.OtherGoalActive, OtherGoalId: other);
        }

        // 5. The deterministic inputs; 6-9. guard, quotas, slot, attempts (the generator and the shared runner).
        var inputs = await BuildInputsAsync(goal, today, facts, history, cancellationToken);
        var run = await generator.GenerateAsync(new PlanGenerationRequest(currentUser.UserId, currentUser.IsGuest, inputs.Sheet), cancellationToken);
        switch (run.Status)
        {
            case GenerationStatus.Succeeded:
                break;
            case GenerationStatus.Unavailable:
                return new PlanGenerateOutcome(PlanGenerateStatus.Unavailable);
            case GenerationStatus.InProgress:
                return new PlanGenerateOutcome(PlanGenerateStatus.InProgress);
            case GenerationStatus.QuotaExceeded:
                return new PlanGenerateOutcome(PlanGenerateStatus.QuotaExceeded, Quota: run.Quota);
            case GenerationStatus.Busy:
                return new PlanGenerateOutcome(PlanGenerateStatus.Busy, RetryAfter: run.RetryAfter);
            default:
                return new PlanGenerateOutcome(PlanGenerateStatus.Failed, Failure: run.Failure);
        }

        // 10. Commit. The model's work is paid for: a client that went away does not discard it.
        var record = new GoalPlanRecord(
            Guid.CreateVersion7(), run.CreatedAt, run.Plan!.Notes, checked((short)run.PromptVersion), run.ModelDeployment!, run.ModelId,
            inputs.Envelope.PlanEnd, inputs.Envelope.HorizonCapped);
        var committed = await CommitAsync(goal, inputs, run.Plan, record, replaceOtherGoals, CancellationToken.None);
        if (committed.Status != PlanGenerateStatus.Ok)
        {
            return committed;
        }

        var view = await goals.GetAsync(goalId, CancellationToken.None);
        return committed with { Goal = view };
    }

    private sealed record PlanInputs(GoalSpec Spec, DateOnly Today, PlanEnvelope Envelope, PlanFactSheet Sheet);

    private async Task<PlanInputs> BuildInputsAsync(
        Goal goal, DateOnly today, List<RunFact> facts, TrainingHistory history, CancellationToken cancellationToken)
    {
        var spec = new GoalSpec(goal.Type, goal.DistanceMeters, goal.ElevationGainMeters, goal.TargetDate, goal.TargetDurationSeconds, goal.Surface);
        var races = await reader.ReadRacesAsync(today, cancellationToken);
        var estimate = GoalEstimator.Estimate(spec, today, facts, races);
        var reference = PlanEnvelopeBuilder.ReferencePace(spec, estimate.Prediction, history);

        // The horizon depends on today and the goal only, so it can bound the commitment read.
        var horizon = PlanEnvelopeBuilder.Build(spec, today, history, [], reference);
        var commitments = await reader.ReadCommitmentsAsync(goal.Id, horizon.PlanStart, horizon.PlanEnd, goal.TargetDate, cancellationToken);
        var dismissed = await reader.ReadDismissedAsync(goal.Id, cancellationToken);
        var envelope = PlanEnvelopeBuilder.Build(spec, today, history, commitments, reference);

        // The M5 metrics over the last 365 days (weekly load, ACWR, races), from the same projections: the 112 days of facts cover the ten
        // history weeks and their ACWR lead-in.
        var report = InsightsReportBuilder.Build(new InsightsPeriod(today.AddDays(-364), today), ComparisonKind.None, today, facts, facts, races, null);
        var sheet = PlanFactSheetBuilder.Build(spec, estimate, today, report, history, commitments, dismissed, envelope, Language);
        return new PlanInputs(spec, today, envelope, sheet);
    }

    private async Task<Guid?> OtherGoalWithPendingAsync(Guid goalId, CancellationToken cancellationToken) =>
        await db.TrainingSuggestions.AsNoTracking()
            .Where(s => s.GoalId != goalId && s.Status == SuggestionStatus.Pending)
            .OrderBy(s => s.Date)
            .Select(s => (Guid?)s.GoalId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<PlanGenerateOutcome> CommitAsync(
        Goal loaded, PlanInputs inputs, PlanValidation validation, GoalPlanRecord record, bool replaceOtherGoals, CancellationToken cancellationToken) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // The whole unit is re-run from a clean change tracker if the retrying execution strategy repeats it.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == loaded.Id, cancellationToken);
            if (goal is null || goal.Status != GoalStatus.Active || !goal.RowVersion.AsSpan().SequenceEqual(loaded.RowVersion))
            {
                return new PlanGenerateOutcome(PlanGenerateStatus.Conflict);
            }

            // Step 4 again: another goal may have got suggestions meanwhile.
            var others = await db.TrainingSuggestions
                .Where(s => s.GoalId != goal.Id)
                .Select(s => new { s.GoalId, s.Status, s.Date })
                .ToListAsync(cancellationToken);
            if (!replaceOtherGoals && others.Where(o => o.Status == SuggestionStatus.Pending).OrderBy(o => o.Date).FirstOrDefault() is { } pending)
            {
                return new PlanGenerateOutcome(PlanGenerateStatus.OtherGoalActive, OtherGoalId: pending.GoalId);
            }

            // The item-level drop again, against the diary as it is now.
            var fresh = await reader.ReadCommitmentsAsync(goal.Id, inputs.Envelope.PlanStart, inputs.Envelope.PlanEnd, goal.TargetDate, cancellationToken);
            var (kept, droppedNow) = TrainingPlanValidator.DropCollisions(validation.Kept, fresh);

            await db.TrainingSuggestions.Where(s => s.GoalId == goal.Id).ExecuteDeleteAsync(cancellationToken);
            List<Guid> replaced = [];
            if (replaceOtherGoals && others.Count > 0)
            {
                replaced = [.. others.Select(o => o.GoalId).Distinct().Order()];
                await db.TrainingSuggestions.Where(s => s.GoalId != goal.Id).ExecuteDeleteAsync(cancellationToken);
            }

            var now = clock.GetUtcNow();
            var rows = kept.Select(s => TrainingSuggestion.Create(currentUser.UserId, goal.Id, record.GenerationId, s, now)).ToList();
            db.TrainingSuggestions.AddRange(rows);
            goal.RecordPlan(record, now);
            db.Entry(goal).Property(g => g.RowVersion).OriginalValue = loaded.RowVersion;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new PlanGenerateOutcome(PlanGenerateStatus.Conflict);
            }

            await transaction.CommitAsync(cancellationToken);
            LogCommitted(rows.Count, validation.DroppedCount + droppedNow, replaced.Count);
            return new PlanGenerateOutcome(
                PlanGenerateStatus.Ok,
                Suggestions: [.. rows.OrderBy(r => r.Date)],
                DroppedCount: validation.DroppedCount + droppedNow,
                ReplacedGoalIds: replaced);
        });

    private static PlanGenerateOutcome Invalid(string message) =>
        new(PlanGenerateStatus.Invalid, Errors: [new DomainError("goalId", message)]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Training plan stored: {Suggestions} suggestion(s), {Dropped} dropped by collisions, {Replaced} other goal(s) replaced.")]
    private partial void LogCommitted(int suggestions, int dropped, int replaced);
}
