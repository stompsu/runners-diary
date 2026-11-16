using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

public static class UpdateGoal
{
    internal static void Map(IEndpointRouteBuilder goals) =>
        goals.MapPut("/{goalId:guid}", HandleAsync)
            .WithName("updateGoal")
            .WithSummary("Replaces one of the caller's goals. 'version' must match the current one, otherwise 409.")
            .WithDescription(
                "Full replace; status and version are required. The rules of createGoal apply, except that an unchanged targetDate is not re-checked against the 7-day minimum. " +
                "Leaving Active deletes the goal's suggestions; changing type, targetDate, distanceMeters, elevationGainMeters, targetDurationSeconds or surface marks an existing plan outdated.")
            .Produces<GoalDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid goalId, GoalWriteRequest request, [FromServices] GoalService service, HttpContext context, CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);
        var errors = new Dictionary<string, string[]>();
        if (request.Status is null)
        {
            errors["status"] = ["Required on update."];
        }

        if (!RunMapping.TryDecodeVersion(request.Version, out var version))
        {
            errors["version"] = ["Required: send the 'version' of the goal being replaced."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await service.UpdateAsync(goalId, request.ToValues(), version, cancellationToken);
        return result.Status switch
        {
            GoalWriteStatus.Ok => TypedResults.Ok(GoalMapping.ToDto(result.View!)),
            GoalWriteStatus.Invalid => TypedResults.ValidationProblem(GoalMapping.ToErrors(result.Errors!)),
            GoalWriteStatus.Conflict => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The goal was changed by someone else.",
                detail: "Reload the goal and apply your changes to the current version.",
                type: ProblemTypes.ConcurrencyConflict),
            _ => GoalsEndpoints.GoalNotFound(),
        };
    }
}
