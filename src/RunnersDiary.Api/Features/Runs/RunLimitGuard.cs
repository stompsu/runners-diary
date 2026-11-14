using Microsoft.AspNetCore.Http.HttpResults;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Api.Features.Runs;

/// <summary>Maps the shared run caps (<see cref="RunCaps"/>) to the 409 <c>run-limit</c> problem.</summary>
internal static class RunLimitGuard
{
    /// <summary>Null when a new run on <paramref name="date"/> is allowed, otherwise the 409 problem to return.</summary>
    public static async Task<IResult?> CheckCreateAsync(
        RunnersDiaryDbContext db, ICurrentUser currentUser, RunLimitsOptions limits, DateOnly date, CancellationToken cancellationToken) =>
        await RunCaps.CheckCreateAsync(db, currentUser, limits, date, cancellationToken) is { } detail ? Problem(detail) : null;

    /// <summary>Null when one more run (other than <paramref name="excludeRunId"/>) may be on <paramref name="date"/>.</summary>
    public static async Task<IResult?> CheckDateAsync(
        RunnersDiaryDbContext db, RunLimitsOptions limits, DateOnly date, Guid? excludeRunId, CancellationToken cancellationToken) =>
        await RunCaps.CheckDateAsync(db, limits, date, excludeRunId, cancellationToken) is { } detail ? Problem(detail) : null;

    internal static ProblemHttpResult Problem(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Run limit reached",
        detail: detail,
        type: ProblemTypes.RunLimit);
}
