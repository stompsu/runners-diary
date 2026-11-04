using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Api.Features.Insights;

public static class GenerateInsightsSummary
{
    /// <summary>Retry-After for a busy replica (seconds).</summary>
    private const int BusyRetryAfterSeconds = 10;

    internal static void Map(IEndpointRouteBuilder insights) =>
        insights.MapPost("/summary", HandleAsync)
            .WithName("generateInsightsSummary")
            .WithTags("Insights")
            .WithSummary("Generates (or returns the fresh stored) AI summary of a period from its computed metrics.")
            .WithDescription(
                "Template periods (no sessions, planned only, very few sessions) get an automatic note without a model call or quota. A fresh stored summary is returned as is unless regenerate is true. " +
                "Otherwise the account and global quotas are checked and the model is called (at most two attempts); failed attempts count. " +
                "409 summary-in-progress: a generation of this account is already running. " +
                "429 ai-quota carries scope (account or global), limit and resetsAt (with Retry-After for the account scope of a user); 429 rate-limited is the burst limiter. " +
                "503 feature-unavailable: no model configured, switched off, or the model endpoint cannot be authenticated. " +
                "503 ai-busy (Retry-After): every generation slot is taken or the model provider is throttling; it costs no quota. " +
                "502 summary-failed carries reason (invalidOutput, timeout, provider).")
            .Produces<InsightsSummaryResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            // Replaces the group's general policy for this route: bursts per caller on top of the durable quotas.
            .RequireRateLimiting(RateLimitingSetup.AiSummaryPolicy);

    private static async Task<IResult> HandleAsync(
        GenerateSummaryRequest request,
        [FromServices] InsightsSummaryService service,
        [FromServices] PromptCatalog prompts,
        [FromServices] TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>();
        if (request.From is null)
        {
            errors["from"] = ["Required."];
        }

        if (request.To is null)
        {
            errors["to"] = ["Required."];
        }

        if (request.From is { } from && request.To is { } to)
        {
            foreach (var (field, messages) in GetInsightsReport.ValidateRange(from, to))
            {
                errors[field] = messages;
            }
        }

        if (!GetInsightsReport.TryParseCompare(request.Compare, out var kind))
        {
            errors["compare"] = [$"Must be one of: {GetInsightsReport.CompareNone}, {GetInsightsReport.ComparePrevious}, {GetInsightsReport.ComparePreviousYear}."];
        }

        var language = request.Lang ?? GetInsightsSummary.DefaultLanguage;
        if (!prompts.Languages.Contains(language, StringComparer.Ordinal))
        {
            errors["lang"] = [$"Must be one of: {string.Join(", ", prompts.Languages)}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        context.Response.Headers.CacheControl = "private, no-store";
        var key = new SummaryKey(request.From!.Value, request.To!.Value, kind, language);

        // A client that goes away cancels the model call (a ledger row is still written); the framework answers nothing to it.
        var outcome = await service.GenerateAsync(key, request.Regenerate, cancellationToken);
        return outcome.Status switch
        {
            GenerateStatus.Ok => TypedResults.Ok(InsightsSummaryMapping.ToResponse(outcome.View!)),
            GenerateStatus.Unavailable => TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Feature unavailable",
                detail: "AI summaries are not available in this environment.",
                type: ProblemTypes.FeatureUnavailable),
            GenerateStatus.InProgress => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Summary already in progress",
                detail: "A summary is already being generated for your account. Wait for it to finish.",
                type: ProblemTypes.SummaryInProgress),
            GenerateStatus.QuotaExceeded => QuotaProblem(outcome.Quota!, clock, context),
            GenerateStatus.Busy => Busy(context, outcome.RetryAfter),
            _ => Failed(outcome.Failure),
        };
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult QuotaProblem(QuotaStatus quota, TimeProvider clock, HttpContext context)
    {
        var global = quota.Block == QuotaBlock.Global;
        var extensions = new Dictionary<string, object?>
        {
            ["scope"] = global ? "global" : "account",
            ["limit"] = quota.BlockLimit,
            ["resetsAt"] = global ? null : quota.ResetsAt,
        };

        if (!global && quota.ResetsAt is { } resetsAt)
        {
            var seconds = (int)Math.Ceiling(Math.Max(1, (resetsAt - clock.GetUtcNow()).TotalSeconds));
            context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "AI summary quota reached",
            detail: global
                ? "The daily AI summary budget of this service is used up. Try again later."
                : quota.Window == QuotaWindow.Account
                    ? "The AI summaries of this demo session are used up."
                    : "You reached the daily limit of AI summaries.",
            type: ProblemTypes.AiQuota,
            extensions: extensions);
    }

    /// <summary>
    /// 503 ai-busy. <paramref name="providerRetryAfter"/> is the model provider's own hint when it throttled; otherwise (a busy replica,
    /// or a throttle without a hint) the fixed <see cref="BusyRetryAfterSeconds"/>.
    /// </summary>
    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Busy(HttpContext context, TimeSpan? providerRetryAfter)
    {
        var seconds = providerRetryAfter is { } wait ? (int)Math.Ceiling(Math.Max(1, wait.TotalSeconds)) : BusyRetryAfterSeconds;
        context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Summaries are busy",
            detail: "Summaries are busy right now. Retry shortly.",
            type: ProblemTypes.AiBusy);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Failed(GenerationFailure? failure) => TypedResults.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "Summary could not be generated",
        detail: "The summary could not be generated. Try again later.",
        type: ProblemTypes.SummaryFailed,
        extensions: new Dictionary<string, object?>
        {
            ["reason"] = failure switch
            {
                GenerationFailure.InvalidOutput => "invalidOutput",
                GenerationFailure.Timeout => "timeout",
                _ => "provider",
            },
        });
}
