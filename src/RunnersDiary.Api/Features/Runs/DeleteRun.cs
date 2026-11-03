using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Runs;

public static class DeleteRun
{
    internal static void Map(IEndpointRouteBuilder runs) =>
        runs.MapDelete("/{id:guid}", HandleAsync)
            .WithName("deleteRun")
            .WithSummary("Deletes one of the caller's runs (and its track records).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, [FromServices] RunnersDiaryDbContext db, CancellationToken cancellationToken)
    {
        // ExecuteDelete honours the "Owner" filter, so another user's run deletes zero rows and looks like a missing one.
        // TODO(M4): delete the run's GPX blobs here once uploads exist; demo blobs are removed by guest cleanup (prefix delete).
        var deleted = await db.Runs.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? RunsEndpoints.RunNotFound() : TypedResults.NoContent();
    }
}
