using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

public static class GetGoal
{
    internal static void Map(IEndpointRouteBuilder goals) =>
        goals.MapGet("/{goalId:guid}", HandleAsync)
            .WithName("getGoal")
            .WithSummary("One of the caller's goals. 404 for an unknown goal and for another user's.")
            .Produces<GoalDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid goalId, [FromServices] GoalService service, HttpContext context, CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);
        return await service.GetAsync(goalId, cancellationToken) is { } view ? TypedResults.Ok(GoalMapping.ToDto(view)) : GoalsEndpoints.GoalNotFound();
    }
}
