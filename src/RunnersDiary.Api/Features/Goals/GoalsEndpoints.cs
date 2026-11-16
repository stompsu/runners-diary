using Microsoft.Extensions.Options;
using RunnersDiary.Api.Features.TrainingSuggestions;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

public static class GoalsEndpoints
{
    /// <summary>
    /// The goal endpoints (M9) and the training-suggestion endpoints. Both groups answer 503 <c>feature-unavailable</c> while <c>Goals:Enabled</c> is
    /// false (read per request, like <c>Races:Enabled</c>); the flag is also surfaced as <c>client-config.goals.enabled</c>, so the SPA never calls them
    /// in that state.
    /// </summary>
    public static IEndpointRouteBuilder MapGoalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var goals = endpoints.MapGroup("/goals").WithTags("Goals").RequireGoalsEnabled();
        ListGoals.Map(goals);
        EstimateGoal.Map(goals);
        GetGoal.Map(goals);
        CreateGoal.Map(goals);
        UpdateGoal.Map(goals);
        DeleteGoal.Map(goals);

        var suggestions = endpoints.MapGroup("/training-suggestions").WithTags("TrainingSuggestions").RequireGoalsEnabled();
        ListTrainingSuggestions.Map(suggestions);
        AcceptTrainingSuggestion.Map(suggestions);
        DismissTrainingSuggestion.Map(suggestions);
        return endpoints;
    }

    private static RouteGroupBuilder RequireGoalsEnabled(this RouteGroupBuilder group) =>
        group
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AddEndpointFilter(async (context, next) =>
                context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<GoalsOptions>>().CurrentValue.Enabled
                    ? await next(context)
                    : TypedResults.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Feature unavailable",
                        detail: "Training goals are not enabled in this environment.",
                        type: ProblemTypes.FeatureUnavailable));

    /// <summary>Per-caller data: never stored by a browser cache or intermediary.</summary>
    internal static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "private, no-store";

    internal static IResult GoalNotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Goal not found.");
}
