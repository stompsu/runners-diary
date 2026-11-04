using System.Text;
using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Features.Tracks;

public static class DownloadTrackGpx
{
    private const string ContentType = "application/gpx+xml";

    internal static void Map(IEndpointRouteBuilder tracks) =>
        tracks.MapGet("/{trackId:guid}/gpx", HandleAsync)
            .WithName("downloadTrackGpx")
            .WithSummary("The original uploaded GPX file as an attachment.")
            .Produces(StatusCodes.Status200OK, contentType: ContentType)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid runId,
        Guid trackId,
        HttpContext context,
        [FromServices] RunnersDiaryDbContext db,
        [FromServices] IBlobStore blobs,
        CancellationToken cancellationToken)
    {
        if (await TracksEndpoints.FindAsync(db, runId, trackId, cancellationToken) is not { } found)
        {
            return TracksEndpoints.NotFound();
        }

        // The blob name comes from the row (server-generated at upload), never from the request.
        var stream = await blobs.OpenReadAsync(found.Track.RawBlobName, cancellationToken);
        if (stream is null)
        {
            return TracksEndpoints.NotFound();
        }

        var headers = context.Response.Headers;
        headers.ContentDisposition = AttachmentHeader(found.Track.OriginalFileName);
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "sandbox";
        headers.CacheControl = "private, no-store";
        return TypedResults.Stream(stream, ContentType);
    }

    /// <summary>
    /// <c>attachment; filename="ascii"; filename*=UTF-8''percent-encoded</c> (RFC 6266 / 8187). The stored name was normalized at upload
    /// (no control characters, quotes or path separators); the fallback additionally keeps only printable ASCII without <c>\ % ;</c>.
    /// </summary>
    internal static string AttachmentHeader(string fileName)
    {
        var fallback = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            fallback.Append(c is >= ' ' and <= '~' and not ('"' or '\\' or '%' or ';') ? c : '_');
        }

        return $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}
