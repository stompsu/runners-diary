using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>A goal with the derived figures the API shows: term, suggestion counts, accepted runs and the linked run's current date.</summary>
public sealed record GoalView(Goal Goal, GoalTerm Term, int PendingCount, int DismissedCount, int AcceptedCount, DateOnly? RaceRunDate);

public enum GoalWriteStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
    LimitReached,
}

/// <summary>Outcome of a goal write; exactly the member that belongs to <see cref="Status"/> is set.</summary>
public sealed record GoalWriteResult(GoalWriteStatus Status, GoalView? View = null, IReadOnlyList<DomainError>? Errors = null);

/// <summary>
/// Goal CRUD for the caller (ADR 0039). Every query goes through the <c>Owner</c> filter, so a foreign goal is "not found". The links to runs are
/// composite NO ACTION FKs (no second cascade path from <c>Users</c>), so a goal delete clears <c>Runs.GoalId</c> first, in one transaction.
/// </summary>
public sealed class GoalService(
    RunnersDiaryDbContext db, ICurrentUser currentUser, TimeProvider clock, IOptions<GoalsOptions> options)
{
    public async Task<IReadOnlyList<GoalView>> ListAsync(CancellationToken cancellationToken)
    {
        var (_, today) = await CallerClock.GetAsync(db, currentUser, clock, cancellationToken);
        var goals = await db.Goals.AsNoTracking()
            .OrderBy(g => g.Status).ThenBy(g => g.TargetDate).ThenBy(g => g.CreatedAt).ThenBy(g => g.Id)
            .ToListAsync(cancellationToken);
        return await ViewsAsync(goals, today, cancellationToken);
    }

    public async Task<GoalView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (_, today) = await CallerClock.GetAsync(db, currentUser, clock, cancellationToken);
        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        return goal is null ? null : (await ViewsAsync([goal], today, cancellationToken))[0];
    }

    public async Task<GoalWriteResult> CreateAsync(GoalValues values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var (_, today) = await CallerClock.GetAsync(db, currentUser, clock, cancellationToken);
        var linked = await FindRaceRunAsync(values.RaceRunId, cancellationToken);

        var errors = GoalRules.Validate(values, today, previousTargetDate: null, linked);
        if (errors.Count > 0)
        {
            return new GoalWriteResult(GoalWriteStatus.Invalid, Errors: errors);
        }

        var max = currentUser.IsGuest ? options.Value.MaxGoalsPerGuest : options.Value.MaxGoalsPerUser;

        // The count and the insert run under the per-user goal lock, so two concurrent creates at cap-1 cannot both pass: the cap is hard.
        var goal = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await GoalWriteLock.AcquireAsync(db, currentUser.UserId, cancellationToken);
            if (await db.Goals.CountAsync(cancellationToken) >= max)
            {
                return null;
            }

            // The owner is always the caller (never from the request).
            var created = Goal.Create(currentUser.UserId, values, today, linked, clock.GetUtcNow());
            db.Goals.Add(created);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        });

        if (goal is null)
        {
            return new GoalWriteResult(GoalWriteStatus.LimitReached);
        }

        return new GoalWriteResult(GoalWriteStatus.Ok, (await ViewsAsync([goal], today, cancellationToken))[0]);
    }

    /// <summary>
    /// Full replace under the <paramref name="version"/> check. Leaving <c>Active</c> deletes the goal's suggestions in the same transaction
    /// (ADR 0039 §1); a changed plan field marks the plan outdated (done by <see cref="Goal.Update"/>).
    /// </summary>
    public async Task<GoalWriteResult> UpdateAsync(Guid id, GoalValues values, byte[] version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(version);
        var (_, today) = await CallerClock.GetAsync(db, currentUser, clock, cancellationToken);
        var linked = await FindRaceRunAsync(values.RaceRunId, cancellationToken);

        // The whole unit is re-run from a clean change tracker if the retrying execution strategy repeats it.
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
            if (goal is null)
            {
                return new GoalWriteResult(GoalWriteStatus.NotFound);
            }

            var errors = GoalRules.Validate(values, today, goal.TargetDate, linked, goal.RaceRunId);
            if (errors.Count > 0)
            {
                return new GoalWriteResult(GoalWriteStatus.Invalid, Errors: errors);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            goal.Update(values, today, linked, clock.GetUtcNow());
            db.Entry(goal).Property(g => g.RowVersion).OriginalValue = version;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new GoalWriteResult(GoalWriteStatus.Conflict);
            }

            if (values.Status != GoalStatus.Active)
            {
                await db.TrainingSuggestions.Where(s => s.GoalId == id).ExecuteDeleteAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new GoalWriteResult(GoalWriteStatus.Ok, new GoalView(goal, GoalTerms.Of(goal.TargetDate, today), 0, 0, 0, null));
        });

        // Counts are read after the commit, so they reflect a deleted suggestion set.
        return result.Status == GoalWriteStatus.Ok
            ? result with { View = (await ViewsAsync([result.View!.Goal], today, cancellationToken))[0] }
            : result;
    }

    /// <summary>Deletes the goal; its suggestions cascade and the runs accepted from it stay with <c>goalId</c> cleared. False when it is not the caller's.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await GoalWriteLock.AcquireAsync(db, currentUser.UserId, cancellationToken);
            await db.Runs.Where(r => r.GoalId == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.GoalId, (Guid?)null), cancellationToken);
            var deleted = await db.Goals.Where(g => g.Id == id).ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0)
            {
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        });

    private async Task<LinkedRaceRun?> FindRaceRunAsync(Guid? runId, CancellationToken cancellationToken)
    {
        if (runId is not { } id)
        {
            return null;
        }

        // The Owner filter makes another user's run look unknown (a 400 on raceRunId, it is a body member).
        var row = await db.Runs.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Id, r.Date, r.Status, r.RunTypeId })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new LinkedRaceRun(row.Id, row.Date, row.Status, RunTypeCatalog.Find(row.RunTypeId)?.Category == RunTypeCategory.Race);
    }

    private async Task<IReadOnlyList<GoalView>> ViewsAsync(List<Goal> goals, DateOnly today, CancellationToken cancellationToken)
    {
        if (goals.Count == 0)
        {
            return [];
        }

        var ids = goals.Select(g => g.Id).ToList();
        var suggestionCounts = await db.TrainingSuggestions.AsNoTracking()
            .Where(s => ids.Contains(s.GoalId))
            .GroupBy(s => new { s.GoalId, s.Status })
            .Select(g => new { g.Key.GoalId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var acceptedCounts = await db.Runs.AsNoTracking()
            .Where(r => r.GoalId != null && ids.Contains(r.GoalId.Value))
            .GroupBy(r => r.GoalId)
            .Select(g => new { GoalId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var raceRunIds = goals.Where(g => g.RaceRunId is not null).Select(g => g.RaceRunId!.Value).ToList();
        var raceRunDates = raceRunIds.Count == 0
            ? []
            : await db.Runs.AsNoTracking().Where(r => raceRunIds.Contains(r.Id)).Select(r => new { r.Id, r.Date }).ToDictionaryAsync(r => r.Id, r => r.Date, cancellationToken);

        return
        [
            .. goals.Select(g => new GoalView(
                g,
                GoalTerms.Of(g.TargetDate, today),
                suggestionCounts.Where(c => c.GoalId == g.Id && c.Status == SuggestionStatus.Pending).Sum(c => c.Count),
                suggestionCounts.Where(c => c.GoalId == g.Id && c.Status == SuggestionStatus.Dismissed).Sum(c => c.Count),
                acceptedCounts.Where(c => c.GoalId == g.Id).Sum(c => c.Count),
                g.RaceRunId is { } runId && raceRunDates.TryGetValue(runId, out var date) ? date : null)),
        ];
    }
}
