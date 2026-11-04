using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Api.Features.Insights;

public static class GetInsightsSummary
{
    public const string DefaultLanguage = "en";

    internal static void Map(IEndpointRouteBuilder insights) =>
        insights.MapGet("/summary", HandleAsync)
            .WithName("getInsightsSummary")
            .WithTags("Insights")
            .WithSummary("The stored, example or automatic narrative of a period, if any, and whether a new one can be generated. Never calls the model.")
            .WithDescription(
                "Same from/to/compare rules as the report, plus lang (en). State is none, fresh or stale (a run, the prompt or the model changed since it was written). " +
                "generation.availability says whether Generate works: available, notConfigured, quotaExhausted or notNeeded. The response is never cached.")
            .Produces<InsightsSummaryResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? compare,
        [FromQuery] string? lang,
        [FromServices] InsightsSummaryService service,
        [FromServices] PromptCatalog prompts,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = GetInsightsReport.ValidateRange(from, to);
        if (!GetInsightsReport.TryParseCompare(compare, out var kind))
        {
            errors["compare"] = [$"Must be one of: {GetInsightsReport.CompareNone}, {GetInsightsReport.ComparePrevious}, {GetInsightsReport.ComparePreviousYear}."];
        }

        var language = lang ?? DefaultLanguage;
        if (!prompts.Languages.Contains(language, StringComparer.Ordinal))
        {
            errors["lang"] = [$"Must be one of: {string.Join(", ", prompts.Languages)}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var view = await service.GetAsync(new SummaryKey(from, to, kind, language), cancellationToken);

        // Per-user and always recomputed from the current runs.
        context.Response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(InsightsSummaryMapping.ToResponse(view));
    }
}
