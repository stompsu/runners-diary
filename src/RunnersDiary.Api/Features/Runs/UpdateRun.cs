using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Api.Features.Runs;

public static class UpdateRun
{
    internal static void Map(IEndpointRouteBuilder runs) =>
        runs.MapPut("/{id:guid}", HandleAsync)
            .WithName("updateRun")
            .WithSummary("Replaces one of the caller's runs. 'version' must match the current one, otherwise 409.")
            .Produces<RunDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id,
        RunWriteRequest request,
        [FromServices] RunnersDiaryDbContext db,
        [FromServices] TimeProvider clock,
        [FromServices] IOptions<RunLimitsOptions> limits,
        CancellationToken cancellationToken)
    {
        if (!RunMapping.TryDecodeVersion(request.Version, out var version))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Required: send the 'version' of the run being replaced."] });
        }

        var run = await db.Runs.Include(r => r.Tracks).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return RunsEndpoints.RunNotFound();
        }

        // Only a date change can break the per-date cap; an update in place never adds a run.
        if (request.Date != run.Date
            && await RunLimitGuard.CheckDateAsync(db, limits.Value, request.Date, excludeRunId: run.Id, cancellationToken) is { } limitProblem)
        {
            return limitProblem;
        }

        // An omitted zone keeps the run's own zone (it may differ from the profile zone); only creation falls back to the profile.
        var zone = request.TimeZoneId ?? run.TimeZoneId;

        run.Update(request.ToValues(zone), clock.GetUtcNow());
        db.Entry(run).Property(r => r.RowVersion).OriginalValue = version;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The run was changed by someone else.",
                detail: "Reload the run and apply your changes to the current version.",
                type: ProblemTypes.ConcurrencyConflict);
        }

        return TypedResults.Ok(RunMapping.ToDto(run));
    }
}
