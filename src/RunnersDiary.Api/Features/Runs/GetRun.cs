using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Runs;

public static class GetRun
{
    internal static void Map(IEndpointRouteBuilder runs) =>
        runs.MapGet("/{id:guid}", HandleAsync)
            .WithName("getRun")
            .WithSummary("One of the caller's runs. Other users' runs are indistinguishable from missing ones (404).")
            .Produces<RunDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, [FromServices] RunnersDiaryDbContext db, CancellationToken cancellationToken)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.Tracks).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return run is null ? RunsEndpoints.RunNotFound() : TypedResults.Ok(RunMapping.ToDto(run));
    }
}
