using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Features.Tracks;

public static class GetTrackGeoJson
{
    internal static void Map(IEndpointRouteBuilder tracks) =>
        tracks.MapGet("/{trackId:guid}/geojson", HandleAsync)
            .WithName("getTrackGeoJson")
            .WithSummary("The processed track (simplified line, start/finish, elevation profile, splits) as GeoJSON. Immutable per track id.")
            .Produces<TrackGeoJson>(StatusCodes.Status200OK, TrackGeoJsonWriter.ContentType)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid runId,
        Guid trackId,
        HttpContext context,
        [FromServices] RunnersDiaryDbContext db,
        [FromServices] IBlobStore blobs,
        CancellationToken cancellationToken)
    {
        if (await TracksEndpoints.FindAsync(db, runId, trackId, cancellationToken) is not { Track: { Status: GpxTrackStatus.Processed, ProcessedBlobName: { } blobName } track })
        {
            return TracksEndpoints.NotFound();
        }

        // A track id never changes its content (a replace creates a new id), so the ETag always validates and a 304 saves the body.
        // "no-cache" (not max-age) makes every use revalidate through the authenticated API, so the geometry does not stay usable
        // from the browser cache after sign-out. Ownership was checked above, so a 304 reveals nothing.
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(track.Sha256.AsSpan(0, 8))}-v{track.ProcessorVersion}\"");
        var response = context.Response;
        response.Headers.CacheControl = "private, no-cache";
        response.Headers.ETag = etag.ToString();
        if (context.Request.GetTypedHeaders().IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false) || t.Equals(EntityTagHeaderValue.Any)))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        var stream = await blobs.OpenReadAsync(blobName, cancellationToken);
        return stream is null ? TracksEndpoints.NotFound() : TypedResults.Stream(stream, TrackGeoJsonWriter.ContentType);
    }
}
