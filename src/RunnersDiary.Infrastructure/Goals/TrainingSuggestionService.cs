using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Infrastructure.Goals;

public enum AcceptSuggestionStatus
{
    Created,
    NotFound,
    Invalid,
    RunLimit,
}

/// <summary>Outcome of <see cref="TrainingSuggestionService.AcceptAsync"/>; exactly the member that belongs to <see cref="Status"/> is set.</summary>
public sealed record AcceptSuggestionResult(
    AcceptSuggestionStatus Status,
    Run? Run = null,
    IReadOnlyList<DomainError>? Errors = null,
    string? LimitDetail = null);

/// <summary>
/// Listing, accepting and dismissing training suggestions (ADR 0041 §2). Everything goes through the <c>Owner</c> filter, so a foreign suggestion is
/// "not found". Accepting creates a planned run linked to the goal and deletes the suggestion in one <c>SaveChanges</c> (one transaction).
/// </summary>
public sealed class TrainingSuggestionService(
    RunnersDiaryDbContext db, ICurrentUser currentUser, TimeProvider clock, IOptions<RunLimitsOptions> limits)
{
    public async Task<IReadOnlyList<TrainingSuggestion>> ListAsync(DateOnly from, DateOnly to, bool includeDismissed, CancellationToken cancellationToken) =>
        await db.TrainingSuggestions.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= to && (includeDismissed || s.Status == SuggestionStatus.Pending))
            .OrderBy(s => s.Date).ThenBy(s => s.GoalId).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Accepts a suggestion (Pending or Dismissed: accepting a dismissed one is the undo path). Without <paramref name="overrides"/> the default
    /// mapping applies; with them the run is exactly those values (validated by <see cref="RunRules"/>), and the goal link is set either way.
    /// Order of answers: not found, invalid, run cap.
    /// </summary>
    public async Task<AcceptSuggestionResult> AcceptAsync(Guid suggestionId, RunValues? overrides, CancellationToken cancellationToken)
    {
        var suggestion = await db.TrainingSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
        if (suggestion is null)
        {
            return new AcceptSuggestionResult(AcceptSuggestionStatus.NotFound);
        }

        var (zone, _) = await CallerClock.GetAsync(db, currentUser, clock, cancellationToken);
        var values = overrides is null
            ? suggestion.ToRunValues(zone)
            : overrides with { TimeZoneId = string.IsNullOrEmpty(overrides.TimeZoneId) ? zone : overrides.TimeZoneId };

        var errors = RunRules.Validate(values);
        if (errors.Count > 0)
        {
            return new AcceptSuggestionResult(AcceptSuggestionStatus.Invalid, Errors: errors);
        }

        if (await RunCaps.CheckCreateAsync(db, currentUser, limits.Value, values.Date, cancellationToken) is { } limitDetail)
        {
            return new AcceptSuggestionResult(AcceptSuggestionStatus.RunLimit, LimitDetail: limitDetail);
        }

        // The owner is always the caller (never from the request).
        var run = Run.Create(currentUser.UserId, values, clock.GetUtcNow());
        run.LinkToGoal(suggestion.GoalId);
        db.Runs.Add(run);
        db.TrainingSuggestions.Remove(suggestion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent accept (or a goal delete) removed the suggestion first: nothing was written, it is gone for this caller too.
            db.ChangeTracker.Clear();
            return new AcceptSuggestionResult(AcceptSuggestionStatus.NotFound);
        }

        return new AcceptSuggestionResult(AcceptSuggestionStatus.Created, Run: run);
    }

    /// <summary>Marks the suggestion dismissed (idempotent). Null when it is not the caller's.</summary>
    public async Task<TrainingSuggestion?> DismissAsync(Guid suggestionId, CancellationToken cancellationToken)
    {
        var suggestion = await db.TrainingSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
        if (suggestion is null)
        {
            return null;
        }

        suggestion.Dismiss();
        await db.SaveChangesAsync(cancellationToken);
        return suggestion;
    }
}
