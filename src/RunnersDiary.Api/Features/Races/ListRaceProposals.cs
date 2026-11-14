using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Features.Races;

public static class ListRaceProposals
{
    public const int MinYear = 1950;
    public const int MaxYear = 2100;

    /// <summary>The window may span at most this many days, counting both ends (a 6-week calendar grid is 42).</summary>
    public const int MaxSpanDays = 92;

    internal static void Map(IEndpointRouteBuilder races) =>
        races.MapGet("/proposals", HandleAsync)
            .WithName("listRaceProposals")
            .WithSummary("Catalogue races in a date window that match the caller's interests, with the caller's state (signed-up races always show).")
            .WithDescription(
                "from and to are required (1950-2100, to >= from, at most 92 days inclusive). The window is clamped to [today, today + horizonDays] in the caller's zone " +
                "(effectiveFrom/effectiveTo); signed-up races also show outside the horizon. Without saved interests: profileConfigured false and no items. " +
                "includeDismissed (default false) also returns dismissed races. Ordered by date, then signed up, interested, proximity, name. The response is never cached.")
            .Produces<RaceProposalListResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool? includeDismissed,
        [FromServices] RaceProposalService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = ValidateRange(from, to);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        RacesEndpoints.NoStore(context);
        var list = await service.ListAsync(from, to, includeDismissed ?? false, cancellationToken);
        return TypedResults.Ok(new RaceProposalListResponse
        {
            From = from,
            To = to,
            EffectiveFrom = list.EffectiveFrom,
            EffectiveTo = list.EffectiveTo,
            ProfileConfigured = list.ProfileConfigured,
            CatalogueUpdatedUtc = list.CatalogueUpdatedUtc,
            Truncated = list.Truncated,
            Items = [.. list.Items.Select(RaceMapping.ToDto)],
        });
    }

    internal static Dictionary<string, string[]> ValidateRange(DateOnly from, DateOnly to)
    {
        var errors = new Dictionary<string, string[]>();
        var min = new DateOnly(MinYear, 1, 1);
        var max = new DateOnly(MaxYear, 12, 31);
        var message = $"Must be between {min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} and {max.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.";

        if (from < min || from > max)
        {
            errors["from"] = [message];
        }

        if (to < min || to > max)
        {
            errors["to"] = [message];
        }

        if (errors.Count == 0 && to < from)
        {
            errors["to"] = ["Must not be before 'from'."];
        }
        else if (errors.Count == 0 && to.DayNumber - from.DayNumber + 1 > MaxSpanDays)
        {
            errors["to"] = [$"The range must not exceed {MaxSpanDays} days."];
        }

        return errors;
    }
}
