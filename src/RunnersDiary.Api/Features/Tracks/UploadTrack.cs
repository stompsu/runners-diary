using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Identity;

namespace RunnersDiary.Api.Features.Tracks;

public static class UploadTrack
{
    private const string FileField = "file";
    private const string KindField = "kind";
    private const string ReplaceField = "replace";

    internal static void Map(IEndpointRouteBuilder tracks, GpxLimitsOptions limits) =>
        tracks.MapPost("/", HandleAsync)
            .WithName("uploadTrack")
            .WithSummary("Uploads a GPX file as the run's planned course or recorded track; it is parsed, measured and stored synchronously.")
            .Accepts<UploadTrackForm>("multipart/form-data")
            .Produces<TrackDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitingSetup.GpxUploadPolicy)
            // Bearer tokens only, no cookies: there is nothing for a forged cross-site form post to ride on (ADR 0020 §3).
            .DisableAntiforgery()
            .WithMetadata(new EndpointBodyLimit(Math.Max(limits.MaxFileBytes, limits.GuestMaxFileBytes) + TracksEndpoints.MultipartOverheadBytes));

    private static async Task<IResult> HandleAsync(
        Guid runId,
        HttpContext context,
        [FromServices] ICurrentUser currentUser,
        [FromServices] TrackIngestionService ingestion,
        [FromServices] Microsoft.Extensions.Options.IOptions<GpxLimitsOptions> limits,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var maxBytes = limits.Value.MaxFileBytesFor(currentUser.IsGuest);

        // The caller-specific limit is applied before the body is read (ADR 0020 §1.3); the file length is checked again below.
        if (request.ContentLength > maxBytes + TracksEndpoints.MultipartOverheadBytes)
        {
            return FileTooLarge(maxBytes);
        }

        ApplyBodyLimit(context, maxBytes);

        if (request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Unsupported media type", detail: "Send the file as multipart/form-data.");
        }

        // Ownership, rest day and an already reached cap are answered before any multipart byte is buffered (the kind and replace
        // fields live in the body, so the kind-conflict check has to wait for it).
        if (await ingestion.PreflightAsync(runId, cancellationToken) is { } early)
        {
            return Respond(early, runId, maxBytes, context);
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Invalid(FileField, "The multipart body is malformed or exceeds the allowed size.");
        }

        var errors = new Dictionary<string, string[]>();
        var kind = default(GpxTrackKind);
        var kindText = form[KindField].ToString();
        if (kindText.Length == 0 || !Enum.TryParse(kindText, ignoreCase: false, out kind) || !Enum.IsDefined(kind) || char.IsDigit(kindText[0]))
        {
            errors[KindField] = ["Must be PlannedCourse or RecordedTrack."];
        }

        var replace = false;
        var replaceText = form[ReplaceField].ToString();
        if (replaceText.Length > 0 && !bool.TryParse(replaceText, out replace))
        {
            errors[ReplaceField] = ["Must be true or false."];
        }

        IFormFile? file = null;
        if (form.Files.Count != 1 || form.Files.GetFile(FileField) is not { } single)
        {
            errors[FileField] = ["Exactly one file in the 'file' field is required."];
        }
        else
        {
            file = single;
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (file!.Length > maxBytes)
        {
            return FileTooLarge(maxBytes);
        }

        if (!GpxFileName.TryNormalize(file.FileName, out var fileName))
        {
            return InvalidGpx(GpxRejection.For(GpxRejectionCode.WrongExtension));
        }

        TrackIngestResult result;
        await using (var content = file.OpenReadStream())
        {
            result = await ingestion.IngestAsync(runId, kind, fileName, content, replace, cancellationToken);
        }

        return Respond(result, runId, maxBytes, context);
    }

    /// <summary>
    /// Caps the request body of this request at the caller's own limit plus multipart framing, so a chunked guest body stops at the
    /// guest limit rather than the larger user limit the route is mapped with. A no-op when the server does not allow changing it.
    /// </summary>
    internal static void ApplyBodyLimit(HttpContext context, long maxFileBytes)
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maxFileBytes + TracksEndpoints.MultipartOverheadBytes;
        }
    }

    private static IResult Respond(TrackIngestResult result, Guid runId, long maxBytes, HttpContext context)
    {
        switch (result.Outcome)
        {
            case TrackIngestOutcome.Created:
                var dto = TrackMapping.ToDto(result.Track!, result.TimeZoneId!);
                return TypedResults.Created($"/api/runs/{runId}/tracks/{dto.Id}", dto);
            case TrackIngestOutcome.Rejected:
                return InvalidGpx(result.Rejection!);
            case TrackIngestOutcome.RunNotFound:
                return RunsEndpoints.RunNotFound();
            case TrackIngestOutcome.RestDay:
                return Invalid("runId", "Rest days have no tracks.");
            case TrackIngestOutcome.Exists:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Track already exists",
                    detail: "This run already has a track of that kind. Set replace=true to replace it.",
                    type: ProblemTypes.TrackExists);
            case TrackIngestOutcome.LimitReached:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Track limit reached",
                    detail: $"An account can hold at most {result.Limit} tracks. Delete some tracks before adding more.",
                    type: ProblemTypes.TrackLimit);
            case TrackIngestOutcome.Busy:
                context.Response.Headers.RetryAfter = "10";
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Processing is busy",
                    detail: "Other uploads are being processed. Retry shortly.",
                    type: ProblemTypes.ProcessingBusy);
            case TrackIngestOutcome.TooLarge:
                return FileTooLarge(maxBytes);
            case TrackIngestOutcome.StorageUnavailable:
                return TracksEndpoints.StorageUnavailable(context);
            case TrackIngestOutcome.TrackNotFound:
                return TracksEndpoints.NotFound();
            default:
                throw new InvalidOperationException("Unhandled ingestion outcome.");
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ValidationProblem Invalid(string field, string message) => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>400 <c>invalid-gpx</c> with the fixed message and the machine-readable <c>code</c>; parser text is never echoed.</summary>
    internal static IResult InvalidGpx(GpxRejection rejection) => TypedResults.ValidationProblem(
        new Dictionary<string, string[]> { [FileField] = [rejection.Message] },
        title: "Invalid GPX file",
        type: ProblemTypes.InvalidGpx,
        extensions: new Dictionary<string, object?> { ["code"] = rejection.CodeName });

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult FileTooLarge(long maxBytes) => TypedResults.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "File too large",
        detail: string.Create(CultureInfo.InvariantCulture, $"GPX files can be at most {maxBytes / (1024 * 1024)} MB."));
}
