using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

public static class EstimateGoal
{
    internal static void Map(IEndpointRouteBuilder goals) =>
        goals.MapPost("/estimate", HandleAsync)
            .WithName("estimateGoal")
            .WithSummary("A realistic-target hint for a goal's planning fields from the caller's recent runs. Nothing is stored; warnings never block saving.")
            .WithDescription(
                "Riegel prediction over km-effort from the caller's best-matching recent race, else a hard (RPE >= 8) training run; prediction is null without one. " +
                "Same distance, elevation and implied-pace limits as createGoal; targetDate from today to today+730 (no 7-day minimum, and no type-specific requirements, so the hint works while a form is incomplete). " +
                "Warning codes (lowerCamel): noRecentEffort, targetMuchFasterThanPredicted, tooLittleTime, lowWeeklyVolume, beyondPlanHorizon. The response is never cached.")
            .Produces<GoalEstimateResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        EstimateGoalRequest request, [FromServices] GoalEstimateService service, HttpContext context, CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);
        var (estimate, errors) = await service.EstimateAsync(request.ToSpec(), cancellationToken);
        return estimate is null
            ? TypedResults.ValidationProblem(GoalMapping.ToErrors(errors))
            : TypedResults.Ok(GoalMapping.ToResponse(estimate));
    }
}
