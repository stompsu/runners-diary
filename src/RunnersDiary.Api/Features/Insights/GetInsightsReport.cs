using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Infrastructure.Insights;

namespace RunnersDiary.Api.Features.Insights;

public static class GetInsightsReport
{
    public const int MinYear = 1950;
    public const int MaxYear = 2100;

    public const string CompareNone = "none";
    public const string ComparePrevious = "previous";
    public const string ComparePreviousYear = "previousYear";

    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        GetInsightsSummary.Map(endpoints.MapGroup("/insights"));
        GenerateInsightsSummary.Map(endpoints.MapGroup("/insights"));
        endpoints.MapGet("/insights/report", HandleAsync)
            .WithName("getInsightsReport")
            .WithTags("Insights")
            .WithSummary("Deterministic training metrics of a date range (at most 366 days) with an optional comparison period.")
            .WithDescription("compare is one of none, previous (default) or previousYear. Load is in AU (RPE x minutes), pace in s/km, distances in metres. The response is never cached.")
            .Produces<InsightsReportResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? compare,
        [FromServices] IInsightsReportService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = ValidateRange(from, to);

        if (!TryParseCompare(compare, out var kind))
        {
            errors["compare"] = [$"Must be one of: {CompareNone}, {ComparePrevious}, {ComparePreviousYear}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var report = await service.GetReportAsync(from, to, kind, cancellationToken);

        // Per-user and always recomputed: nothing may be stored by a shared cache or reused after a run changes.
        context.Response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(InsightsMapping.ToResponse(report));
    }

    /// <summary>The shared range rules of the report and the summary: dates 1950-2100, <c>to</c> not before <c>from</c>, at most 366 days.</summary>
    internal static Dictionary<string, string[]> ValidateRange(DateOnly from, DateOnly to)
    {
        var errors = new Dictionary<string, string[]>();
        var min = new DateOnly(MinYear, 1, 1);
        var max = new DateOnly(MaxYear, 12, 31);

        if (from < min || from > max)
        {
            errors["from"] = [$"Must be between {min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} and {max.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."];
        }

        if (to < min || to > max)
        {
            errors["to"] = [$"Must be between {min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} and {max.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."];
        }

        if (errors.Count == 0 && to < from)
        {
            errors["to"] = ["Must not be before 'from'."];
        }
        else if (errors.Count == 0 && to.DayNumber - from.DayNumber + 1 > InsightsPeriod.MaxDays)
        {
            errors["to"] = [$"The range must not exceed {InsightsPeriod.MaxDays} days."];
        }

        return errors;
    }

    internal static bool TryParseCompare(string? value, out ComparisonKind kind)
    {
        switch (value)
        {
            case null or ComparePrevious:
                kind = ComparisonKind.Previous;
                return true;
            case CompareNone:
                kind = ComparisonKind.None;
                return true;
            case ComparePreviousYear:
                kind = ComparisonKind.PreviousYear;
                return true;
            default:
                kind = ComparisonKind.None;
                return false;
        }
    }
}
