using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Api.Features.Runs;

public static class CreateRun
{
    internal static void Map(IEndpointRouteBuilder runs) =>
        runs.MapPost("/", HandleAsync)
            .WithName("createRun")
            .WithSummary("Creates a planned or completed run for the caller.")
            .Produces<RunDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        RunWriteRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] RunnersDiaryDbContext db,
        [FromServices] TimeProvider clock,
        [FromServices] IOptions<RunLimitsOptions> limits,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (await RunLimitGuard.CheckCreateAsync(db, currentUser, limits.Value, request.Date, cancellationToken) is { } limitProblem)
        {
            return limitProblem;
        }

        var zone = request.TimeZoneId ?? await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.TimeZoneId).FirstAsync(cancellationToken);

        // The owner is always the caller (never from the request); the domain factory re-checks every rule.
        var run = Run.Create(userId, request.ToValues(zone), clock.GetUtcNow());
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/runs/{run.Id}", RunMapping.ToDto(run));
    }
}
