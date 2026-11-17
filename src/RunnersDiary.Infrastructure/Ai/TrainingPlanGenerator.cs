using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Goals.Planning;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>Who asks for a plan and its deterministic fact sheet. The user id is the caller's, never from a request body or the model.</summary>
public sealed record PlanGenerationRequest(Guid UserId, bool IsGuest, PlanFactSheet Sheet);

/// <summary>
/// The outcome of <see cref="TrainingPlanGenerator.GenerateAsync"/>; on success <see cref="Plan"/> is the valid validation result. A
/// <see cref="GenerationStatus.Failed"/> run also carries <see cref="Attempts"/>, the usage that came back and, in <see cref="AttemptDetails"/>,
/// what each answered attempt looked like. These are in-process diagnostics (the live eval prints them): <see cref="TrainingPlanService"/> maps
/// only the status and the failure reason to the API, and the ledger row is unchanged.
/// </summary>
public sealed record PlanGenerationResult(
    GenerationStatus Status,
    PlanValidation? Plan = null,
    int PromptVersion = 0,
    string? ModelDeployment = null,
    string? ModelId = null,
    int Attempts = 0,
    int InputTokens = 0,
    int OutputTokens = 0,
    DateTimeOffset CreatedAt = default,
    QuotaStatus? Quota = null,
    GenerationFailure? Failure = null,
    TimeSpan? RetryAfter = null,
    IReadOnlyList<PlanAttemptDetails>? AttemptDetails = null);

/// <summary>
/// One answered plan attempt, for diagnostics (ADR 0026/0040 limits: rule codes, counts, dates and fixed paths, never model text). Attempts that
/// timed out or failed at the provider return nothing, so <see cref="PlanGenerationResult.Attempts"/> can exceed the number of details.
/// </summary>
/// <param name="Number">1-based attempt number.</param>
/// <param name="FinishReason">The provider's finish reason (<c>stop</c>, <c>length</c>, …) when it gave one.</param>
/// <param name="Violations">The plan-level violations; empty for a valid attempt. Locations are dates or fixed paths (anything else becomes <c>?</c>).</param>
/// <param name="KeptSessions">Sessions that passed the per-session checks and the collision drop.</param>
/// <param name="DroppedCount">Sessions dropped by the item-level rules (<c>collision</c> and the <c>taperIntensity</c>/<c>pace</c> repairs).</param>
/// <param name="Repairs">What the deterministic repair step changed in this attempt's answer (rule code, location, counts; never model text).</param>
public sealed record PlanAttemptDetails(
    int Number,
    int InputTokens,
    int OutputTokens,
    string? FinishReason,
    IReadOnlyList<PlanViolation> Violations,
    int KeptSessions,
    int DroppedCount,
    IReadOnlyList<PlanRepair> Repairs)
{
    /// <summary>The provider stopped at the output token cap (Chat Completions <c>length</c>): the JSON is most likely cut off.</summary>
    public bool Truncated => string.Equals(FinishReason, "length", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The model part of a plan generation (ADR 0040 §1 steps 6-9) through the shared <see cref="AiGenerationRunner"/>: the plan feature's guard
/// and quotas, a replica slot, at most <c>Ai:Plan:MaxAttempts</c> attempts of <c>Ai:Plan:AttemptTimeout</c> within <c>Ai:Plan:TotalTimeout</c>;
/// each attempt runs <see cref="TrainingPlanAgent"/> and <see cref="TrainingPlanValidator"/>, and a rejected plan is retried once with the rule
/// codes and locations only. No database access: <see cref="TrainingPlanService"/> builds the fact sheet and stores the result.
/// </summary>
public sealed partial class TrainingPlanGenerator(
    TrainingPlanAgentProvider agents, AiGenerationRunner runner, IOptionsMonitor<AiOptions> options, ILogger<TrainingPlanGenerator> logger)
{
    /// <summary>A model is configured and <c>Ai:Enabled</c> and <c>Ai:Plan:Enabled</c> are on (read per call).</summary>
    public bool IsAvailable => options.CurrentValue.PlanGenerationAvailable && agents.Agent is not null && agents.Deployment is not null;

    public async Task<PlanGenerationResult> GenerateAsync(PlanGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.CurrentValue;
        if (!settings.PlanGenerationAvailable || agents.Agent is not { } agent || agents.Deployment is not { } deployment)
        {
            return new PlanGenerationResult(GenerationStatus.Unavailable);
        }

        var factSheetJson = PlanFactSheetJson.ToJson(request.Sheet);
        if (factSheetJson.Length > settings.Plan.MaxFactSheetChars)
        {
            LogFactSheetTooLarge(factSheetJson.Length, settings.Plan.MaxFactSheetChars);
            return new PlanGenerationResult(GenerationStatus.Failed, Failure: GenerationFailure.Provider);
        }

        // The rules of the plan's language (ADR 0043 §3): the en ∪ pl deny list for Polish texts.
        var policy = agent.Prompt.Policy.ForLanguage(request.Sheet.Language);
        IReadOnlyList<PlanViolation>? rejected = null;
        var details = new List<PlanAttemptDetails>();
        var run = await runner.RunAsync<PlanValidation>(
            new AiRunRequest(AiFeature.Plan, request.UserId, request.IsGuest),
            new AiRunSettings(settings.Plan.MaxAttempts, settings.Plan.AttemptTimeout, settings.Plan.TotalTimeout, agent.Prompt.Version, deployment),
            async ct =>
            {
                var attempt = await agent.RunAsync(factSheetJson, request.Sheet.Language, rejected, ct);
                var validation = TrainingPlanValidator.Validate(attempt.Text, request.Sheet, policy);
                details.Add(Details(details.Count + 1, attempt, validation));
                if (validation.IsValid)
                {
                    return new AiAttemptResult<PlanValidation>(attempt.InputTokens, attempt.OutputTokens, attempt.ModelId, validation, []);
                }

                rejected = validation.Violations;
                return new AiAttemptResult<PlanValidation>(
                    attempt.InputTokens, attempt.OutputTokens, attempt.ModelId, null, [.. validation.Codes.Select(c => c.ToCode())]);
            },
            cancellationToken);

        return run.Status == GenerationStatus.Succeeded
            ? new PlanGenerationResult(
                GenerationStatus.Succeeded, run.Value, agent.Prompt.Version, deployment, EfAiSummaryStore.TruncateModelId(run.ModelId),
                run.Attempts, run.InputTokens, run.OutputTokens, run.CreatedAt, AttemptDetails: details)
            : new PlanGenerationResult(
                run.Status, PromptVersion: agent.Prompt.Version, ModelDeployment: deployment, ModelId: EfAiSummaryStore.TruncateModelId(run.ModelId),
                Attempts: run.Attempts, InputTokens: run.InputTokens, OutputTokens: run.OutputTokens, Quota: run.Quota, Failure: run.Failure,
                RetryAfter: run.RetryAfter, AttemptDetails: details);
    }

    /// <summary>One attempt's diagnostics; a location that is not a date or a fixed path (never expected) becomes <c>?</c>, so no model text gets through.</summary>
    internal static PlanAttemptDetails Details(int number, AgentAttempt attempt, PlanValidation validation) => new(
        number,
        attempt.InputTokens,
        attempt.OutputTokens,
        attempt.FinishReason,
        [.. validation.Violations.Select(v => TrainingPlanAgent.IsSafeLocation(v.Location) ? v : v with { Location = "?" })],
        validation.Kept.Count,
        validation.DroppedCount,
        [.. validation.Repairs.Select(r => TrainingPlanAgent.IsSafeLocation(r.Location) ? r : r with { Location = "?" })]);

    [LoggerMessage(Level = LogLevel.Error, Message = "The plan fact sheet has {Length} characters, above the cap of {Cap}; not sent to the model.")]
    private partial void LogFactSheetTooLarge(int length, int cap);
}
