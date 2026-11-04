using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Features.Maps;

/// <summary>A short-lived Azure Maps SAS token for the browser SDK. Shared by all callers and renewed by the server.</summary>
public sealed record MapsTokenResponse
{
    public required string Token { get; init; }

    /// <summary>When the token stops working; the SPA asks again shortly before.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

public static partial class GetMapsToken
{
    public static IEndpointRouteBuilder MapMapsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/maps/token", HandleAsync)
            .WithName("getMapsToken")
            .WithTags("Maps")
            .WithSummary("Issues a short-lived Azure Maps SAS token for the browser SDK. 503 feature-unavailable unless Azure Maps display is enabled, or when the token cannot be issued.")
            .Produces<MapsTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitingSetup.MapsTokenPolicy);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync([FromServices] IMapsTokenIssuer issuer, [FromServices] ILoggerFactory loggerFactory, HttpContext context, CancellationToken cancellationToken)
    {
        // The token is a bearer credential: never stored by the browser cache or an intermediary.
        context.Response.Headers.CacheControl = "private, no-store";
        if (!issuer.IsEnabled)
        {
            return Unavailable("Azure Maps display is not enabled.");
        }

        try
        {
            var token = await issuer.GetTokenAsync(cancellationToken);
            return TypedResults.Ok(new MapsTokenResponse { Token = token.Token, ExpiresAt = token.ExpiresAt });
        }
#pragma warning disable CA1031 // A provider failure of any kind is an outage for the caller (the SPA falls back to MapLibre), never a 500 with details.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // The exception type only: provider messages can carry resource ids or URLs.
            LogIssueFailed(loggerFactory.CreateLogger(typeof(GetMapsToken)), ex.GetType().Name);
            return Unavailable("The map token is temporarily unavailable.");
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Unavailable(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Feature unavailable",
        detail: detail,
        type: ProblemTypes.FeatureUnavailable);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure Maps token issuance failed ({ExceptionType}).")]
    private static partial void LogIssueFailed(ILogger logger, string exceptionType);
}
