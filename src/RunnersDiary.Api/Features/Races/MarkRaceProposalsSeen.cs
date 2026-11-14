using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Features.Races;

public static class MarkRaceProposalsSeen
{
    internal static void Map(IEndpointRouteBuilder races) =>
        races.MapPost("/proposals/seen", HandleAsync)
            .WithName("markRaceProposalsSeen")
            .WithSummary("Records that the caller opened the proposals list, so races imported later show as new. A no-op without saved interests.")
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync([FromServices] RaceProposalService service, CancellationToken cancellationToken)
    {
        await service.MarkSeenAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
