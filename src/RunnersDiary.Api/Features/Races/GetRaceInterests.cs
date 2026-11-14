using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Races;

public static class GetRaceInterests
{
    internal static void Map(IEndpointRouteBuilder races) =>
        races.MapGet("/interests", HandleAsync)
            .WithName("getRaceInterests")
            .WithSummary("The caller's race interests; configured is false with the defaults while none were saved.")
            .WithDescription("Without saved interests proposals are off (listRaceProposals answers profileConfigured: false). Distances and radius are metres. The response is never cached.")
            .Produces<RaceInterestsResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> HandleAsync([FromServices] RunnersDiaryDbContext db, HttpContext context, CancellationToken cancellationToken)
    {
        RacesEndpoints.NoStore(context);
        var profile = await db.RaceInterestProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(profile is null
            ? RaceMapping.ToResponse(Domain.Races.RaceInterestValues.Defaults, configured: false, updatedAt: null)
            : RaceMapping.ToResponse(profile.ToValues(), configured: true, profile.UpdatedAt));
    }
}
