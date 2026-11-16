using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

public static class ListGoals
{
    internal static void Map(IEndpointRouteBuilder goals) =>
        goals.MapGet("/", HandleAsync)
            .WithName("listGoals")
            .WithSummary("All of the caller's goals (at most 50): Active first, then by target date.")
            .WithDescription("Each goal carries its derived term, the current date of its linked race run and the state of its plan. The response is never cached.")
            .Produces<GoalListResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> HandleAsync([FromServices] GoalService service, HttpContext context, CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);
        var views = await service.ListAsync(cancellationToken);
        return TypedResults.Ok(new GoalListResponse { Items = [.. views.Select(GoalMapping.ToDto)] });
    }
}
