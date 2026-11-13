using Microsoft.EntityFrameworkCore;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Runs;

/// <summary>
/// Per-account run caps (abuse and cost bound): total runs (lower for guests) and runs per date. Counts go through the owner-filtered
/// <c>Runs</c> set, so only the caller's own runs are counted. Shared by <c>createRun</c> and <c>signUpForRace</c>; the API maps a
/// violation to 409 <c>run-limit</c>. The check and the insert are not atomic; concurrent requests of one account can overshoot a cap by
/// the number of simultaneous writes, which the general rate limiter keeps small.
/// </summary>
public static class RunCaps
{
    /// <summary>Null when a new run on <paramref name="date"/> is allowed, otherwise the problem detail text.</summary>
    public static async Task<string?> CheckCreateAsync(
        RunnersDiaryDbContext db, ICurrentUser currentUser, RunLimitsOptions limits, DateOnly date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(limits);
        var maxTotal = currentUser.IsGuest ? limits.GuestMaxRuns : limits.UserMaxRuns;
        if (await db.Runs.CountAsync(cancellationToken) >= maxTotal)
        {
            return $"An account can hold at most {maxTotal} runs. Delete some runs before adding more.";
        }

        return await CheckDateAsync(db, limits, date, excludeRunId: null, cancellationToken);
    }

    /// <summary>Null when one more run (other than <paramref name="excludeRunId"/>) may be on <paramref name="date"/>.</summary>
    public static async Task<string?> CheckDateAsync(
        RunnersDiaryDbContext db, RunLimitsOptions limits, DateOnly date, Guid? excludeRunId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(limits);
        var onDate = await db.Runs.CountAsync(r => r.Date == date && r.Id != excludeRunId, cancellationToken);
        return onDate >= limits.MaxRunsPerDate ? $"At most {limits.MaxRunsPerDate} runs can be recorded on one date." : null;
    }
}
