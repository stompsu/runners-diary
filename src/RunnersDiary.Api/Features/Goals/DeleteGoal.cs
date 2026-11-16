using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

public static class DeleteGoal
{
    internal static void Map(IEndpointRouteBuilder goals) =>
        goals.MapDelete("/{goalId:guid}", HandleAsync)
            .WithName("deleteGoal")
            .WithSummary("Deletes one of the caller's goals and its suggestions. Runs accepted from it stay, with goalId cleared.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid goalId, [FromServices] GoalService service, CancellationToken cancellationToken) =>
        await service.DeleteAsync(goalId, cancellationToken) ? TypedResults.NoContent() : GoalsEndpoints.GoalNotFound();
}
