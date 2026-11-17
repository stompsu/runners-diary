using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Features.TrainingSuggestions;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Api.Features.Goals;

/// <summary>Body of <c>POST /api/goals/{goalId}/plan</c>; the body is optional.</summary>
public sealed record GenerateTrainingPlanRequest
{
    /// <summary>True replaces the suggestions of the caller's other goals; without it, another goal's pending suggestions answer 409 <c>plan-other-goal-active</c>.</summary>
    public bool? ReplaceOtherGoals { get; init; }

    /// <summary>Language of the plan texts: <c>en</c> (default) or <c>pl</c> (the languages of the plan prompt manifest); anything else answers 400 <c>errors.lang</c>.</summary>
    public string? Lang { get; init; }
}

/// <summary>The stored plan: the goal with its new plan metadata and the new suggestion set.</summary>
public sealed record TrainingPlanResponse
{
    public required GoalDto Goal { get; init; }

    /// <summary>The new suggestions of the goal, by date.</summary>
    public required IReadOnlyList<TrainingSuggestionDto> Suggestions { get; init; }

    /// <summary>Sessions left out because the day already has a running or rest entry or another goal's date.</summary>
    public required int DroppedCount { get; init; }

    /// <summary>Other goals whose suggestions were removed (<c>replaceOtherGoals</c>).</summary>
    public required IReadOnlyList<Guid> ReplacedGoalIds { get; init; }
}

public static class GenerateTrainingPlan
{
    /// <summary>Retry-After for a busy replica (seconds).</summary>
    private const int BusyRetryAfterSeconds = 10;

    internal static void Map(IEndpointRouteBuilder goals) =>
        goals.MapPost("/{goalId:guid}/plan", HandleAsync)
            .WithName("generateTrainingPlan")
            .WithSummary("Generates (or regenerates) the AI-suggested trainings up to an active goal, at most 16 weeks ahead.")
            .WithDescription(
                "The body is optional; lang (en default, or pl) is the language of the plan texts and is stored as plan.language. The model suggests sessions inside bounds computed from the caller's own history (weekly and long-run caps, phases, " +
                "taper, sessions per week, hard-day rules); every number is validated before anything is stored. Regenerating replaces the goal's pending " +
                "and dismissed suggestions; runs (also accepted suggestions) are never changed. Can take up to about three minutes. " +
                "400 errors.lang: unsupported language. 400 errors.goalId: the goal is not Active or is less than two days away. " +
                "422 insufficient-history: fewer than 3 completed runs in the last 84 days. " +
                "409 plan-other-goal-active (goalId: the goal holding pending suggestions; send replaceOtherGoals true to replace them), " +
                "409 plan-in-progress, 409 concurrency-conflict (the goal changed meanwhile). " +
                "429 ai-quota carries scope (account or global), limit and resetsAt (with Retry-After for the account scope of a user); 429 rate-limited is the burst limiter. " +
                "503 feature-unavailable: no model, plans switched off, or the model endpoint cannot be authenticated. 503 ai-busy (Retry-After). " +
                "502 plan-failed carries reason (invalidOutput, timeout, provider). The response is never cached.")
            .Produces<TrainingPlanResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            // Replaces the group's general policy for this route: bursts per caller on top of the durable plan quotas.
            .RequireRateLimiting(RateLimitingSetup.AiPlanPolicy);

    private static async Task<IResult> HandleAsync(
        Guid goalId,
        GenerateTrainingPlanRequest? request,
        [FromServices] TrainingPlanService service,
        [FromServices] PlanPromptCatalog prompts,
        [FromServices] TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        GoalsEndpoints.NoStore(context);

        var language = request?.Lang ?? TrainingPlanService.DefaultLanguage;
        if (!prompts.Languages.Contains(language, StringComparer.Ordinal))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["lang"] = [$"Must be one of: {string.Join(", ", prompts.Languages)}."] });
        }

        // A client that goes away cancels the model call (a ledger row is still written); the framework answers nothing to it.
        var outcome = await service.GenerateAsync(goalId, request?.ReplaceOtherGoals == true, language, cancellationToken);
        return outcome.Status switch
        {
            PlanGenerateStatus.Ok => TypedResults.Ok(new TrainingPlanResponse
            {
                Goal = GoalMapping.ToDto(outcome.Goal!),
                Suggestions = [.. outcome.Suggestions!.Select(TrainingSuggestionMapping.ToDto)],
                DroppedCount = outcome.DroppedCount,
                ReplacedGoalIds = outcome.ReplacedGoalIds ?? [],
            }),
            PlanGenerateStatus.NotFound => GoalsEndpoints.GoalNotFound(),
            PlanGenerateStatus.Invalid => TypedResults.ValidationProblem(GoalMapping.ToErrors(outcome.Errors!)),
            PlanGenerateStatus.Unavailable => TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Feature unavailable",
                detail: "AI training suggestions are not available in this environment.",
                type: ProblemTypes.FeatureUnavailable),
            PlanGenerateStatus.InsufficientHistory => TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Not enough recent training",
                detail: $"Log at least {TrainingPlanService.MinRecentSessions} completed runs in the last 12 weeks so the plan can start from your current training.",
                type: ProblemTypes.InsufficientHistory),
            PlanGenerateStatus.OtherGoalActive => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Another goal has suggested trainings",
                detail: "Generating replaces the suggestions of your other goal. Send replaceOtherGoals true to continue.",
                type: ProblemTypes.PlanOtherGoalActive,
                extensions: new Dictionary<string, object?> { ["goalId"] = outcome.OtherGoalId }),
            PlanGenerateStatus.InProgress => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Plan already in progress",
                detail: "Suggested trainings are already being generated for your account. Wait for them to finish.",
                type: ProblemTypes.PlanInProgress),
            PlanGenerateStatus.Conflict => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The goal was changed by someone else.",
                detail: "The goal changed while the suggestions were generated. Reload the goal and generate again.",
                type: ProblemTypes.ConcurrencyConflict),
            PlanGenerateStatus.QuotaExceeded => QuotaProblem(outcome.Quota!, clock, context),
            PlanGenerateStatus.Busy => Busy(context, outcome.RetryAfter),
            _ => Failed(outcome.Failure),
        };
    }

    private static ProblemHttpResult QuotaProblem(QuotaStatus quota, TimeProvider clock, HttpContext context)
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
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "AI plan quota reached",
            detail: global
                ? "The daily AI budget of this service is used up. Try again later."
                : quota.Window == QuotaWindow.Account
                    ? "The AI training suggestions of this demo session are used up."
                    : "You reached the daily limit of AI training suggestions.",
            type: ProblemTypes.AiQuota,
            extensions: extensions);
    }

    /// <summary>503 ai-busy with the provider's Retry-After when it throttled, else the fixed <see cref="BusyRetryAfterSeconds"/>.</summary>
    private static ProblemHttpResult Busy(HttpContext context, TimeSpan? providerRetryAfter)
    {
        var seconds = providerRetryAfter is { } wait ? (int)Math.Ceiling(Math.Max(1, wait.TotalSeconds)) : BusyRetryAfterSeconds;
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "AI is busy",
            detail: "The AI is busy right now. Retry shortly.",
            type: ProblemTypes.AiBusy);
    }

    private static ProblemHttpResult Failed(GenerationFailure? failure) => TypedResults.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "Suggestions could not be generated",
        detail: "The AI could not produce suggested trainings that fit your plan limits. Try again later.",
        type: ProblemTypes.PlanFailed,
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
