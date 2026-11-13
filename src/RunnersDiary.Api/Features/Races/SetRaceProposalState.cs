using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Http;
using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Features.Races;

public static class SetRaceProposalState
{
    internal static void Map(IEndpointRouteBuilder races) =>
        races.MapPut("/{raceId:guid}/state", HandleAsync)
            .WithName("setRaceProposalState")
            .WithSummary("Marks a race Interested or Dismissed for the caller, or clears the mark (None).")
            .WithDescription(
                "SignedUp is derived from a run and is rejected with 400. A removed or duplicate race can only be cleared (anything else is 404). " +
                "409 proposal-state-limit past 2,000 marks per user (200 per guest). The response state is SignedUp while the caller has a linked run.")
            .Produces<RaceUserStateDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid raceId,
        SetRaceProposalStateRequest request,
        [FromServices] RaceProposalService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.State))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["state"] = ["Must be None, Interested or Dismissed."] });
        }

        RaceProposalMark? mark = request.State switch
        {
            RaceStateChoice.Interested => RaceProposalMark.Interested,
            RaceStateChoice.Dismissed => RaceProposalMark.Dismissed,
            _ => null,
        };

        RacesEndpoints.NoStore(context);
        var result = await service.SetStateAsync(raceId, mark, cancellationToken);
        return result.Status switch
        {
            SetRaceStateStatus.Ok => TypedResults.Ok(new RaceUserStateDto
            {
                RaceId = raceId,
                State = result.User!.State,
                LinkedRunId = result.User.LinkedRunId,
                DateChanged = result.User.DateChanged,
            }),
            SetRaceStateStatus.LimitReached => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Too many marked races",
                detail: "An account can mark a limited number of races. Clear some marks before adding more.",
                type: ProblemTypes.ProposalStateLimit),
            _ => RacesEndpoints.RaceNotFound(),
        };
    }
}
