using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Goals.Planning;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>Who asks for a plan and its deterministic fact sheet. The user id is the caller's, never from a request body or the model.</summary>
public sealed record PlanGenerationRequest(Guid UserId, bool IsGuest, PlanFactSheet Sheet);

/// <summary>The outcome of <see cref="TrainingPlanGenerator.GenerateAsync"/>; on success <see cref="Plan"/> is the valid validation result.</summary>
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
    TimeSpan? RetryAfter = null);

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

        IReadOnlyList<PlanViolation>? rejected = null;
        var run = await runner.RunAsync<PlanValidation>(
            new AiRunRequest(AiFeature.Plan, request.UserId, request.IsGuest),
            new AiRunSettings(settings.Plan.MaxAttempts, settings.Plan.AttemptTimeout, settings.Plan.TotalTimeout, agent.Prompt.Version, deployment),
            async ct =>
            {
                var attempt = await agent.RunAsync(factSheetJson, request.Sheet.Language, rejected, ct);
                var validation = TrainingPlanValidator.Validate(attempt.Text, request.Sheet, agent.Prompt.Policy);
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
                run.Attempts, run.InputTokens, run.OutputTokens, run.CreatedAt)
            : new PlanGenerationResult(run.Status, Quota: run.Quota, Failure: run.Failure, RetryAfter: run.RetryAfter);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The plan fact sheet has {Length} characters, above the cap of {Cap}; not sent to the model.")]
    private partial void LogFactSheetTooLarge(int length, int cap);
}
