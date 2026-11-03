using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Auth;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Api.Features.GuestSessions;

public sealed record GuestSessionResponse
{
    public required string AccessToken { get; init; }

    public required string TokenType { get; init; }

    /// <summary>UTC; equals the token's <c>exp</c> and the guest account's expiry (24 h, no refresh).</summary>
    public required DateTime ExpiresAt { get; init; }

    public required Guid UserId { get; init; }
}

/// <summary>
/// <c>POST /api/guest-sessions</c>: the only anonymous write. Creates a guest account with its own demo diary and returns its token.
/// Guarded by the per-IP <c>guest-create</c> limiter, the active-guest cap and the seeding concurrency limit (ADR 0011).
/// </summary>
public static class CreateGuestSession
{
    internal static void Map(IEndpointRouteBuilder routes) =>
        routes.MapPost("/guest-sessions", HandleAsync)
            .WithName("createGuestSession")
            .WithTags("GuestSessions")
            .WithSummary("Creates a 24 h guest account with demo data and returns its bearer token.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.GuestCreatePolicy)
            .Produces<GuestSessionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromServices] GuestAccountService guests,
        [FromServices] GuestTokenIssuer tokens,
        [FromServices] GuestCreateGlobalLimiter globalLimiter,
        CancellationToken cancellationToken)
    {
        // After the per-client policy (so one client cannot drain this budget) and before any database work.
        if (globalLimiter.TryAcquire() is { } wait)
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return RateLimitingSetup.RateLimitedProblem();
        }

        var result = await guests.CreateAsync(cancellationToken);
        if (result.Account is not { } account)
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: result.Failure == GuestCreationFailure.AtCapacity ? "Guest capacity reached" : "Guest sessions are unavailable",
                detail: result.Failure switch
                {
                    GuestCreationFailure.AtCapacity => "The demo is full right now. Try again in a few minutes.",
                    GuestCreationFailure.Busy => "Too many demo accounts are being prepared. Try again in a few seconds.",
                    _ => "The demo account could not be prepared. Try again in a few seconds.",
                },
                type: ProblemTypes.GuestCapacity);
        }

        // A bearer token must never be cached by a browser or proxy.
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Json(
            new GuestSessionResponse
            {
                AccessToken = tokens.Issue(account.UserId, account.ExpiresAt),
                TokenType = "Bearer",
                ExpiresAt = account.ExpiresAt.UtcDateTime,
                UserId = account.UserId,
            },
            statusCode: StatusCodes.Status201Created);
    }
}
