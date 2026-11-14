using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Api.Http;
using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Features.Races;

public static class SignUpForRace
{
    internal static void Map(IEndpointRouteBuilder races) =>
        races.MapPost("/{raceId:guid}/signup", HandleAsync)
            .WithName("signUpForRace")
            .WithSummary("Creates a planned race run for the caller from a catalogue race (\"I signed up\").")
            .WithDescription(
                "The body is optional. distanceMeters must equal one of the race's distances and is required when it lists more than one; runTypeId must be a race run type; " +
                "startTime is HH:mm; targetTimeSeconds 1-604,800. A race dated before the caller's today is a 400 on raceId. 404 for an unknown, removed or duplicate race. " +
                "409 race-already-linked (runId = the caller's own run) or run-limit.")
            .Produces<RunDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid raceId,
        SignUpForRaceRequest? request,
        [FromServices] RaceSignupService service,
        CancellationToken cancellationToken)
    {
        var choice = request is null
            ? RaceSignupChoice.None
            : new RaceSignupChoice(request.DistanceMeters, request.StartTime, request.RunTypeId, request.TargetTimeSeconds);

        var result = await service.SignUpAsync(raceId, choice, cancellationToken);
        return result.Status switch
        {
            RaceSignupStatus.Created => TypedResults.Created($"/api/runs/{result.Run!.Id}", RunMapping.ToDto(result.Run)),
            RaceSignupStatus.Invalid => TypedResults.ValidationProblem(RaceMapping.ToErrors(result.Errors!)),
            RaceSignupStatus.AlreadyLinked => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Already signed up",
                detail: "You already have a run for this race.",
                type: ProblemTypes.RaceAlreadyLinked,
                extensions: new Dictionary<string, object?> { ["runId"] = result.ExistingRunId }),
            RaceSignupStatus.RunLimit => RunLimitGuard.Problem(result.LimitDetail!),
            _ => RacesEndpoints.RaceNotFound(),
        };
    }
}
