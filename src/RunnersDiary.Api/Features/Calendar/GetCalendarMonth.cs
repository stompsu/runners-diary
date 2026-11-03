using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Calendar;

public sealed record TotalsDto
{
    public required int RunCount { get; init; }

    public required double DistanceMeters { get; init; }

    public required long DurationSeconds { get; init; }

    public required double ElevationGainMeters { get; init; }
}

public sealed record CalendarWeekDto
{
    public required int IsoYear { get; init; }

    public required int IsoWeek { get; init; }

    public required DateOnly Start { get; init; }

    public required DateOnly End { get; init; }

    public required TotalsDto Completed { get; init; }

    public required TotalsDto Planned { get; init; }
}

public sealed record CalendarMonthResponse
{
    public required int Year { get; init; }

    public required int Month { get; init; }

    /// <summary>The Monday on or before the 1st.</summary>
    public required DateOnly GridStart { get; init; }

    /// <summary>The Sunday on or after the last day of the month.</summary>
    public required DateOnly GridEnd { get; init; }

    public required IReadOnlyList<RunDto> Runs { get; init; }

    public required IReadOnlyList<CalendarWeekDto> Weeks { get; init; }
}

public static class GetCalendarMonth
{
    public const int MinYear = 1950;
    public const int MaxYear = 2100;

    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/calendar/months/{year:int}/{month:int}", HandleAsync)
            .WithName("getCalendarMonth")
            .WithTags("Calendar")
            .WithSummary("The month grid (Monday-first) with the caller's runs and ISO-week totals.")
            .Produces<CalendarMonthResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        int year,
        int month,
        [FromServices] RunnersDiaryDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (year is < MinYear or > MaxYear)
        {
            errors["year"] = [$"Must be between {MinYear} and {MaxYear}."];
        }

        if (month is < 1 or > 12)
        {
            errors["month"] = ["Must be between 1 and 12."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var grid = CalendarGrid.ForMonth(year, month);
        var runs = await db.Runs.AsNoTracking()
            .Include(r => r.Tracks)
            .Where(r => r.Date >= grid.Start && r.Date <= grid.End)
            .OrderBy(r => r.Date).ThenBy(r => r.StartUtc == null).ThenBy(r => r.StartUtc).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var weeks = WeeklyTotals.Compute(
            grid,
            runs.Select(r => new RunSummary(r.Date, r.Status, r.RunTypeId, r.DistanceMeters, r.DurationSeconds, r.ElevationGainMeters)));

        return TypedResults.Ok(new CalendarMonthResponse
        {
            Year = year,
            Month = month,
            GridStart = grid.Start,
            GridEnd = grid.End,
            Runs = [.. runs.Select(RunMapping.ToDto)],
            Weeks = [.. weeks.Select(w => new CalendarWeekDto
            {
                IsoYear = w.Week.Year,
                IsoWeek = w.Week.Week,
                Start = w.Start,
                End = w.End,
                Completed = ToDto(w.Completed),
                Planned = ToDto(w.Planned),
            })],
        });
    }

    private static TotalsDto ToDto(Totals t) => new()
    {
        RunCount = t.RunCount,
        DistanceMeters = t.DistanceMeters,
        DurationSeconds = t.DurationSeconds,
        ElevationGainMeters = t.ElevationGainMeters,
    };
}
