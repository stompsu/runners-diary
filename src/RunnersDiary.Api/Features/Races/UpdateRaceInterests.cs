using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Races;

public static class UpdateRaceInterests
{
    internal static void Map(IEndpointRouteBuilder races) =>
        races.MapPut("/interests", HandleAsync)
            .WithName("updateRaceInterests")
            .WithSummary("Replaces the caller's race interests (creating them on first save).")
            .WithDescription("Full replace. kinds >= 1; regionCodes <= 16 known ISO 3166-2:PL codes without duplicates; center and radiusMeters (5,000-300,000) together; min/max distance 1,000-250,000 with min <= max; horizonDays 7-365; keywords <= 100 characters, <= 5 terms of 2-30 characters. Unknown members are rejected.")
            .Produces<RaceInterestsResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        UpdateRaceInterestsRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] RunnersDiaryDbContext db,
        [FromServices] TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        RacesEndpoints.NoStore(context);
        var values = request.ToValues();
        var now = clock.GetUtcNow();

        RaceInterestProfile profile;
        var existing = await db.RaceInterestProfiles.FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            profile = existing;
            profile.Update(values, now);
        }
        else
        {
            // The owner is always the caller (never from the request).
            profile = RaceInterestProfile.Create(currentUser.UserId, values, now);
            db.RaceInterestProfiles.Add(profile);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // A concurrent first save of the same caller won the primary key: replace its row instead.
            db.ChangeTracker.Clear();
            profile = await db.RaceInterestProfiles.FirstAsync(cancellationToken);
            profile.Update(values, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(RaceMapping.ToResponse(profile.ToValues(), configured: true, profile.UpdatedAt));
    }
}
