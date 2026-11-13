using Microsoft.Extensions.Options;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Features.Races;

public static class RacesEndpoints
{
    /// <summary>
    /// The race proposal endpoints (M8). The group answers 503 <c>feature-unavailable</c> while <c>Races:Enabled</c> is false (read per request,
    /// like the AI kill switch), following the maps-token and AI-summary precedent for disabled features. The flag is also surfaced as
    /// <c>client-config.races.enabled</c>, so the SPA never calls them in that state.
    /// </summary>
    public static IEndpointRouteBuilder MapRaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var races = endpoints.MapGroup("/races")
            .WithTags("Races")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AddEndpointFilter(async (context, next) =>
                context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<RacesOptions>>().CurrentValue.Enabled
                    ? await next(context)
                    : TypedResults.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Feature unavailable",
                        detail: "Race proposals are not enabled in this environment.",
                        type: ProblemTypes.FeatureUnavailable));

        GetRaceInterests.Map(races);
        UpdateRaceInterests.Map(races);
        ListRaceProposals.Map(races);
        MarkRaceProposalsSeen.Map(races);
        GetRace.Map(races);
        SetRaceProposalState.Map(races);
        SignUpForRace.Map(races);
        return endpoints;
    }

    /// <summary>Per-caller data: never stored by a browser cache or intermediary.</summary>
    internal static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "private, no-store";

    internal static IResult RaceNotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Race not found.");
}
