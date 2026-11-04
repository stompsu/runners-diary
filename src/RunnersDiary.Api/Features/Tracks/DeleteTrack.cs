using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Gpx;

namespace RunnersDiary.Api.Features.Tracks;

public static class DeleteTrack
{
    internal static void Map(IEndpointRouteBuilder tracks) =>
        tracks.MapDelete("/{trackId:guid}", HandleAsync)
            .WithName("deleteTrack")
            .WithSummary("Deletes a track and its stored files. If the files cannot be deleted nothing is removed (503).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> HandleAsync(
        Guid runId, Guid trackId, HttpContext context, [FromServices] TrackDeletionService deletion, CancellationToken cancellationToken) =>
        await deletion.DeleteTrackAsync(runId, trackId, cancellationToken) switch
        {
            TrackDeleteOutcome.Deleted => TypedResults.NoContent(),
            TrackDeleteOutcome.NotFound => TracksEndpoints.NotFound(),
            _ => TracksEndpoints.StorageUnavailable(context),
        };
}
