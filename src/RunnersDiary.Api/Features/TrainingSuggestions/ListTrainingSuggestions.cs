using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.Goals;
using RunnersDiary.Api.Features.Races;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.TrainingSuggestions;

public static class ListTrainingSuggestions
{
    internal static void Map(IEndpointRouteBuilder suggestions) =>
        suggestions.MapGet("/", HandleAsync)
            .WithName("listTrainingSuggestions")
            .WithSummary("The caller's training suggestions in an inclusive date range (at most 92 days).")
            .WithDescription(
                "from and to are required (1950-2100, to >= from, at most 92 days inclusive). Pending suggestions only unless includeDismissed is true. " +
                "Ordered by date, then goal. The response is never cached.")
            .Produces<TrainingSuggestionListResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool? includeDismissed,
        [FromServices] TrainingSuggestionService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // Same range rules as the race proposals (1950-2100, to >= from, at most 92 days inclusive).
        var errors = ListRaceProposals.ValidateRange(from, to);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        GoalsEndpoints.NoStore(context);
        var items = await service.ListAsync(from, to, includeDismissed ?? false, cancellationToken);
        return TypedResults.Ok(new TrainingSuggestionListResponse { From = from, To = to, Items = [.. items.Select(TrainingSuggestionMapping.ToDto)] });
    }
}
