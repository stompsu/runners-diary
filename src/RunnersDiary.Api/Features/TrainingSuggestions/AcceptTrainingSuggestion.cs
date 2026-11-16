using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.Goals;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.TrainingSuggestions;

public static class AcceptTrainingSuggestion
{
    internal static void Map(IEndpointRouteBuilder suggestions) =>
        suggestions.MapPost("/{suggestionId:guid}/accept", HandleAsync)
            .WithName("acceptTrainingSuggestion")
            .WithSummary("Turns a suggestion into a planned run linked to its goal, and removes the suggestion.")
            .WithDescription(
                "The body is optional. Without run: a planned run on the suggestion's date with its run type, distance and duration, the description as notes, no start time and " +
                "the caller's profile zone. With run: exactly that run (validated like createRun; errors are keyed run.*). Works on dismissed suggestions too (the undo path). " +
                "404 for an unknown or foreign suggestion; 409 run-limit.")
            .Produces<RunDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid suggestionId,
        AcceptTrainingSuggestionRequest? request,
        [FromServices] TrainingSuggestionService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);
        var overrides = request?.Run is { } run ? run.ToValues(run.TimeZoneId ?? string.Empty) : null;
        var result = await service.AcceptAsync(suggestionId, overrides, cancellationToken);
        return result.Status switch
        {
            AcceptSuggestionStatus.Created => TypedResults.Created($"/api/runs/{result.Run!.Id}", RunMapping.ToDto(result.Run)),
            AcceptSuggestionStatus.Invalid => TypedResults.ValidationProblem(GoalMapping.ToErrors(result.Errors!, prefix: "run.")),
            AcceptSuggestionStatus.RunLimit => RunLimitGuard.Problem(result.LimitDetail!),
            _ => TrainingSuggestionsNotFound(),
        };
    }

    internal static IResult TrainingSuggestionsNotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Training suggestion not found.");
}
