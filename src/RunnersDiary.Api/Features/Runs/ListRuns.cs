using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Runs;

public static class ListRuns
{
    public const int MaxSpanDays = 92;

    internal static void Map(IEndpointRouteBuilder runs) =>
        runs.MapGet("/", HandleAsync)
            .WithName("listRuns")
            .WithSummary("The caller's runs in an inclusive date range (at most 92 days).")
            .Produces<RunListResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] RunnersDiaryDbContext db,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Must not be before 'from'."] });
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxSpanDays)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["to"] = [$"The range must not exceed {MaxSpanDays} days."] });
        }

        // The "Owner" query filter scopes this to the caller (ADR 0010).
        var runs = await db.Runs.AsNoTracking()
            .Include(r => r.Tracks)
            .Where(r => r.Date >= from && r.Date <= to)
            .OrderBy(r => r.Date).ThenBy(r => r.StartUtc == null).ThenBy(r => r.StartUtc).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new RunListResponse { From = from, To = to, Items = [.. runs.Select(RunMapping.ToDto)] });
    }
}
