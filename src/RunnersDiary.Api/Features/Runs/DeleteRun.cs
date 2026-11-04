using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.Tracks;
using RunnersDiary.Infrastructure.Gpx;

namespace RunnersDiary.Api.Features.Runs;

public static class DeleteRun
{
    internal static void Map(IEndpointRouteBuilder runs) =>
        runs.MapDelete("/{id:guid}", HandleAsync)
            .WithName("deleteRun")
            .WithSummary("Deletes one of the caller's runs and its tracks, files first. If the files cannot be deleted nothing is removed (503).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> HandleAsync(Guid id, HttpContext context, [FromServices] TrackDeletionService deletion, CancellationToken cancellationToken) =>
        // Blobs first, then rows (ADR 0023 §5). The owner filter makes another user's run look missing (404).
        await deletion.DeleteRunAsync(id, cancellationToken) switch
        {
            TrackDeleteOutcome.Deleted => TypedResults.NoContent(),
            TrackDeleteOutcome.NotFound => RunsEndpoints.RunNotFound(),
            _ => TracksEndpoints.StorageUnavailable(context),
        };
}
