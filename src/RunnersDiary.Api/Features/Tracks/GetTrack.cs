using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Tracks;

public static class GetTrack
{
    internal static void Map(IEndpointRouteBuilder tracks) =>
        tracks.MapGet("/{trackId:guid}", HandleAsync)
            .WithName("getTrack")
            .WithSummary("One track of one of the caller's runs, with its summary once processed. Anything else is 404.")
            .Produces<TrackDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid runId, Guid trackId, [FromServices] RunnersDiaryDbContext db, CancellationToken cancellationToken) =>
        await TracksEndpoints.FindAsync(db, runId, trackId, cancellationToken) is { } found
            ? TypedResults.Ok(TrackMapping.ToDto(found.Track, found.TimeZoneId))
            : TracksEndpoints.NotFound();
}
