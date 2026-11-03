using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Api.Features.Runs;

/// <summary>
/// Per-account run caps (abuse and cost bound): total runs (lower for guests) and runs per date. Counts go through the owner-filtered
/// <c>Runs</c> set, so only the caller's own runs are counted. The check and the insert are not atomic; concurrent requests of one
/// account can overshoot a cap by the number of simultaneous writes, which the general rate limiter keeps small.
/// </summary>
internal static class RunLimitGuard
{
    /// <summary>Null when a new run on <paramref name="date"/> is allowed, otherwise the 409 problem to return.</summary>
    public static async Task<IResult?> CheckCreateAsync(
        RunnersDiaryDbContext db, ICurrentUser currentUser, RunLimitsOptions limits, DateOnly date, CancellationToken cancellationToken)
    {
        var maxTotal = currentUser.IsGuest ? limits.GuestMaxRuns : limits.UserMaxRuns;
        if (await db.Runs.CountAsync(cancellationToken) >= maxTotal)
        {
            return Problem($"An account can hold at most {maxTotal} runs. Delete some runs before adding more.");
        }

        return await CheckDateAsync(db, limits, date, excludeRunId: null, cancellationToken);
    }

    /// <summary>Null when one more run (other than <paramref name="excludeRunId"/>) may be on <paramref name="date"/>.</summary>
    public static async Task<IResult?> CheckDateAsync(
        RunnersDiaryDbContext db, RunLimitsOptions limits, DateOnly date, Guid? excludeRunId, CancellationToken cancellationToken)
    {
        var onDate = await db.Runs.CountAsync(r => r.Date == date && r.Id != excludeRunId, cancellationToken);
        return onDate >= limits.MaxRunsPerDate
            ? Problem($"At most {limits.MaxRunsPerDate} runs can be recorded on one date.")
            : null;
    }

    private static ProblemHttpResult Problem(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Run limit reached",
        detail: detail,
        type: ProblemTypes.RunLimit);
}
