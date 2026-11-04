using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Http;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Tracks;

public static class TracksEndpoints
{
    /// <summary>Multipart framing allowance on top of the file limit (boundaries, the small <c>kind</c>/<c>replace</c> fields).</summary>
    internal const long MultipartOverheadBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapTrackEndpoints(this IEndpointRouteBuilder endpoints, GpxLimitsOptions limits)
    {
        var tracks = endpoints.MapGroup("/runs/{runId:guid}/tracks").WithTags("Tracks");
        UploadTrack.Map(tracks, limits);
        GetTrack.Map(tracks);
        GetTrackGeoJson.Map(tracks);
        DownloadTrackGpx.Map(tracks);
        DeleteTrack.Map(tracks);
        return endpoints;
    }

    internal static IResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Track not found.");

    /// <summary>
    /// The caller's track on the caller's run plus the run's zone. A foreign run, a foreign track, a track of another run and a missing one
    /// are all null (the owner filter applies to both sets), hence 404.
    /// </summary>
    internal static async Task<(GpxTrack Track, string TimeZoneId)?> FindAsync(
        RunnersDiaryDbContext db, Guid runId, Guid trackId, CancellationToken cancellationToken)
    {
        var run = await db.Runs.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => new { r.TimeZoneId, Track = r.Tracks.FirstOrDefault(t => t.Id == trackId) })
            .FirstOrDefaultAsync(cancellationToken);
        return run?.Track is { } track ? (track, run.TimeZoneId) : null;
    }

    internal static IResult StorageUnavailable(HttpContext context)
    {
        context.Response.Headers.RetryAfter = "5";
        return TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Storage temporarily unavailable",
            detail: "The file storage could not be reached. Nothing was changed; retry shortly.",
            type: ProblemTypes.StorageUnavailable);
    }
}
