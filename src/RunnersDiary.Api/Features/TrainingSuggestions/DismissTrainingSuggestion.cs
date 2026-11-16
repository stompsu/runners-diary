using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.Goals;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.TrainingSuggestions;

public static class DismissTrainingSuggestion
{
    internal static void Map(IEndpointRouteBuilder suggestions) =>
        suggestions.MapPost("/{suggestionId:guid}/dismiss", HandleAsync)
            .WithName("dismissTrainingSuggestion")
            .WithSummary("Marks a suggestion as dismissed (idempotent). It stays listed with includeDismissed and can still be accepted.")
            .Produces<TrainingSuggestionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid suggestionId, [FromServices] TrainingSuggestionService service, HttpContext context, CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);
        return await service.DismissAsync(suggestionId, cancellationToken) is { } suggestion
            ? TypedResults.Ok(TrainingSuggestionMapping.ToDto(suggestion))
            : AcceptTrainingSuggestion.TrainingSuggestionsNotFound();
    }
}
