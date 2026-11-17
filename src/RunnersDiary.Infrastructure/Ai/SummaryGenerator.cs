using System.ClientModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>The agent for the configured client and the current prompt, or none when no provider is configured. A singleton.</summary>
public sealed class TrainingInsightsAgentProvider(AiChatClientProvider clients, PromptCatalog prompts, IOptions<AiOptions> options, ILoggerFactory loggerFactory)
{
    private readonly Lazy<TrainingInsightsAgent?> _agent = new(() =>
        clients.Client is { } client ? new TrainingInsightsAgent(client, prompts.Current, options.Value, loggerFactory) : null);

    public TrainingInsightsAgent? Agent => _agent.Value;

    public string? Deployment => clients.Deployment;
}

public sealed record GenerationRequest(Guid UserId, bool IsGuest, FactSheet Sheet);

/// <summary>A validated, resolved model summary and what it cost.</summary>
public sealed record GeneratedSummary(
    SummaryDraft Draft,
    SummaryContent Content,
    int PromptVersion,
    string ModelDeployment,
    string? ModelId,
    int Attempts,
    int InputTokens,
    int OutputTokens,
    DateTimeOffset CreatedAt);

public sealed record GenerationResult(
    GenerationStatus Status, GeneratedSummary? Summary = null, QuotaStatus? Quota = null, GenerationFailure? Failure = null, TimeSpan? RetryAfter = null);

/// <summary>
/// Runs one summary generation (M6 plan §3.1 steps 6–9) through the shared <see cref="AiGenerationRunner"/> (ADR 0040 §6): the in-flight
/// guard, the durable summary quotas, a replica slot, then at most <see cref="AiOptions.MaxAttempts"/> model attempts of at most
/// <see cref="AiOptions.AttemptTimeout"/> each. An invalid output is retried once with the rule codes only; a timeout or a provider error is
/// not retried. Behaviour is unchanged from M6; the runner documents the 429, credential and cancellation paths.
/// <para>
/// The options are read through <see cref="IOptionsMonitor{TOptions}"/> once per generation, so the <c>Ai:Enabled</c> kill switch
/// (and the limits) apply at runtime without a restart; the client itself is built once at startup.
/// </para>
/// </summary>
public sealed partial class SummaryGenerator(
    TrainingInsightsAgentProvider agents,
    AiUsageLedger ledger,
    GenerationSlots slots,
    GenerationInFlightGuard inFlight,
    IOptionsMonitor<AiOptions> options,
    TimeProvider clock,
    ILogger<SummaryGenerator> logger)
{
    /// <summary>The longest provider Retry-After passed on to the client.</summary>
    public static readonly TimeSpan MaxRetryAfter = AiGenerationRunner.MaxRetryAfter;

    private readonly AiGenerationRunner _runner = new(ledger, slots, inFlight, clock, logger);

    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.CurrentValue;
        if (!settings.GenerationAvailable || agents.Agent is not { } agent || agents.Deployment is not { } deployment)
        {
            return new GenerationResult(GenerationStatus.Unavailable);
        }

        var factSheetJson = FactSheetJson.ToJson(request.Sheet);
        if (factSheetJson.Length > settings.MaxFactSheetChars)
        {
            LogFactSheetTooLarge(factSheetJson.Length, settings.MaxFactSheetChars);
            return new GenerationResult(GenerationStatus.Failed, Failure: GenerationFailure.Provider);
        }

        // The rules of the request language (ADR 0043 §3): Polish number words and the en ∪ pl deny list for a Polish summary.
        var policy = agent.Prompt.Policy.ForLanguage(request.Sheet.Language);
        IReadOnlyList<SummaryViolation>? rejected = null;
        var run = await _runner.RunAsync<ValidatedSummary>(
            new AiRunRequest(AiFeature.Summary, request.UserId, request.IsGuest),
            new AiRunSettings(settings.MaxAttempts, settings.AttemptTimeout, TotalTimeout: null, agent.Prompt.Version, deployment),
            async ct =>
            {
                var attempt = await agent.RunAsync(factSheetJson, request.Sheet.Language, rejected, ct);
                var (draft, result) = SummaryValidator.Validate(attempt.Text, request.Sheet, policy);
                if (result.IsValid)
                {
                    return new AiAttemptResult<ValidatedSummary>(
                        attempt.InputTokens, attempt.OutputTokens, attempt.ModelId, new ValidatedSummary(draft!, SummaryValues.Resolve(draft!, request.Sheet)), []);
                }

                rejected = result.Violations;
                return new AiAttemptResult<ValidatedSummary>(
                    attempt.InputTokens, attempt.OutputTokens, attempt.ModelId, null, [.. result.Codes.Select(c => c.ToCode())]);
            },
            cancellationToken);

        return run.Status switch
        {
            GenerationStatus.Succeeded => new GenerationResult(
                GenerationStatus.Succeeded,
                new GeneratedSummary(
                    run.Value!.Draft, run.Value.Content, agent.Prompt.Version, deployment, EfAiSummaryStore.TruncateModelId(run.ModelId),
                    run.Attempts, run.InputTokens, run.OutputTokens, run.CreatedAt)),
            _ => new GenerationResult(run.Status, Quota: run.Quota, Failure: run.Failure, RetryAfter: run.RetryAfter),
        };
    }

    /// <summary>See <see cref="AiGenerationRunner.IsCredentialFailure"/>.</summary>
    internal static bool IsCredentialFailure(Exception exception) => AiGenerationRunner.IsCredentialFailure(exception);

    /// <summary>See <see cref="AiGenerationRunner.RetryAfterOf"/>.</summary>
    internal static TimeSpan? RetryAfterOf(ClientResultException exception, DateTimeOffset now) => AiGenerationRunner.RetryAfterOf(exception, now);

    [LoggerMessage(Level = LogLevel.Error, Message = "The fact sheet has {Length} characters, above the cap of {Cap}; not sent to the model.")]
    private partial void LogFactSheetTooLarge(int length, int cap);

    private sealed record ValidatedSummary(SummaryDraft Draft, SummaryContent Content);
}
