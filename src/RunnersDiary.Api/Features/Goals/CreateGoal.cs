using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

public static class CreateGoal
{
    internal static void Map(IEndpointRouteBuilder goals) =>
        goals.MapPost("/", HandleAsync)
            .WithName("createGoal")
            .WithSummary("Creates a goal for the caller.")
            .WithDescription(
                "distanceMeters 1,000-250,000; elevationGainMeters 0-10,000; targetDate today+7 to today+730 (the caller's zone); Race needs eventName, Performance needs " +
                "targetDurationSeconds, Distance must not have it; the implied pace must be 150-900 s/km; raceRunId = the caller's own planned race run dated on targetDate " +
                "(else 400). status defaults to Active. 409 goal-limit past 50 goals per user (10 per guest).")
            .Produces<GoalDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        GoalWriteRequest request, [FromServices] GoalService service, HttpContext context, CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);
        var result = await service.CreateAsync(request.ToValues(), cancellationToken);
        return result.Status switch
        {
            GoalWriteStatus.Ok => TypedResults.Created($"/api/goals/{result.View!.Goal.Id}", GoalMapping.ToDto(result.View)),
            GoalWriteStatus.Invalid => TypedResults.ValidationProblem(GoalMapping.ToErrors(result.Errors!)),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Goal limit reached",
                detail: "An account can hold a limited number of goals. Delete some goals before adding more.",
                type: ProblemTypes.GoalLimit),
        };
    }
}
