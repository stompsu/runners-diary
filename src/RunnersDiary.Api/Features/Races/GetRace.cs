using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Features.Races;

public static class GetRace
{
    internal static void Map(IEndpointRouteBuilder races) =>
        races.MapGet("/{raceId:guid}", HandleAsync)
            .WithName("getRace")
            .WithSummary("One catalogue race with the caller's state.")
            .WithDescription(
                "404 for an unknown or purged race, and for a removed one the caller is neither interested in nor signed up to. " +
                "A duplicate answers 200 with canonicalId set (the SPA navigates there). The response is never cached.")
            .Produces<RaceDetailsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid raceId, [FromServices] RaceProposalService service, HttpContext context, CancellationToken cancellationToken)
    {
        RacesEndpoints.NoStore(context);
        return await service.GetDetailsAsync(raceId, cancellationToken) is { } details
            ? TypedResults.Ok(RaceMapping.ToDto(details))
            : RacesEndpoints.RaceNotFound();
    }
}
