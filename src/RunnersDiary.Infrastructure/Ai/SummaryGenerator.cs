using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>Bounds concurrent generations per replica (<see cref="AiOptions.MaxConcurrentGenerations"/>, ADR 0028 §3).</summary>
public sealed class GenerationSlots(IOptions<AiOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(Math.Max(0, options.Value.MaxConcurrentGenerations));

    /// <summary>A lease to dispose when done, or null when every slot is taken (no waiting: the caller answers 503 <c>ai-busy</c>).</summary>
    public IDisposable? TryAcquire() => _slots.Wait(0) ? new Lease(_slots) : null;

    public void Dispose() => _slots.Dispose();

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}

/// <summary>One generation per user at a time on this replica (409 <c>summary-in-progress</c>, ADR 0028 §3).</summary>
public sealed class GenerationInFlightGuard
{
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    public IDisposable? TryEnter(Guid userId) => _inFlight.TryAdd(userId, 0) ? new Entry(_inFlight, userId) : null;

    private sealed class Entry(ConcurrentDictionary<Guid, byte> inFlight, Guid userId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                inFlight.TryRemove(userId, out _);
            }
        }
    }
}

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

public enum GenerationStatus
{
    Succeeded,

    /// <summary>No provider or the kill switch is off: 503 <c>feature-unavailable</c>.</summary>
    Unavailable,

    /// <summary>The same user already has a generation running: 409 <c>summary-in-progress</c>.</summary>
    InProgress,

    /// <summary>429 <c>ai-quota</c> (see <see cref="GenerationResult.Quota"/>).</summary>
    QuotaExceeded,

    /// <summary>
    /// No free slot on this replica, or the provider throttled (429 after the transport retry): 503 <c>ai-busy</c>, with
    /// <see cref="GenerationResult.RetryAfter"/> when the provider gave one. Costs the caller no quota.
    /// </summary>
    Busy,

    /// <summary>502 <c>summary-failed</c> with <see cref="GenerationResult.Failure"/>; nothing stored.</summary>
    Failed,
}

/// <summary>The <c>reason</c> of 502 <c>summary-failed</c>.</summary>
public enum GenerationFailure
{
    InvalidOutput,
    Timeout,
    Provider,
}

public sealed record GenerationResult(
    GenerationStatus Status, GeneratedSummary? Summary = null, QuotaStatus? Quota = null, GenerationFailure? Failure = null, TimeSpan? RetryAfter = null);

/// <summary>
/// Runs one generation (plan §3.1 steps 6–9): the per-user in-flight guard, the durable quotas, a replica slot, then at most
/// <see cref="AiOptions.MaxAttempts"/> model attempts of at most <see cref="AiOptions.AttemptTimeout"/> each. An invalid output is retried
/// once with the rule codes only; a timeout or a provider error is not retried. Every run that reached the model writes one ledger row
/// (tokens, outcome; never content), also when the caller cancels. Cancellation by the caller propagates as
/// <see cref="OperationCanceledException"/>.
/// <para>
/// Two provider-side conditions are not the caller's fault and cost no quota: a <b>429</b> (after the transport retry) answers
/// <see cref="GenerationStatus.Busy"/> with the provider's Retry-After and writes a <see cref="AiGenerationOutcome.Throttled"/> row only
/// if an earlier attempt was billed (for the global attempt budget); a <b>credential failure</b> before any request (no token for the
/// managed identity or the developer) answers <see cref="GenerationStatus.Unavailable"/> and writes no row unless an earlier attempt was billed.
/// </para>
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
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

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

        using var entry = inFlight.TryEnter(request.UserId);
        if (entry is null)
        {
            return new GenerationResult(GenerationStatus.InProgress);
        }

        var quota = await ledger.GetStatusAsync(request.UserId, request.IsGuest, cancellationToken);
        if (quota.Block != QuotaBlock.None)
        {
            return new GenerationResult(GenerationStatus.QuotaExceeded, Quota: quota);
        }

        using var slot = slots.TryAcquire();
        if (slot is null)
        {
            return new GenerationResult(GenerationStatus.Busy);
        }

        return await RunAttemptsAsync(agent, deployment, request, factSheetJson, settings, cancellationToken);
    }

    private async Task<GenerationResult> RunAttemptsAsync(
        TrainingInsightsAgent agent, string deployment, GenerationRequest request, string factSheetJson, AiOptions settings, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var attempts = 0;
        var inputTokens = 0;
        var outputTokens = 0;
        IReadOnlyList<SummaryViolation>? rejected = null;
        var outcome = AiGenerationOutcome.InvalidOutput;
        GeneratedSummary? summary = null;
        var credentialFailed = false;
        TimeSpan? retryAfter = null;

        try
        {
            while (attempts < settings.MaxAttempts)
            {
                // Counted before the call: a timed-out or failed attempt may still be billed, and its usage never comes back.
                attempts++;
                using var timer = new CancellationTokenSource(settings.AttemptTimeout, clock);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);

                var attempt = await agent.RunAsync(factSheetJson, request.Sheet.Language, rejected, timeout.Token);
                inputTokens += attempt.InputTokens;
                outputTokens += attempt.OutputTokens;

                var (draft, result) = SummaryValidator.Validate(attempt.Text, request.Sheet, agent.Prompt.Policy);
                if (result.IsValid)
                {
                    outcome = AiGenerationOutcome.Succeeded;
                    summary = new GeneratedSummary(
                        draft!, SummaryValues.Resolve(draft!, request.Sheet), agent.Prompt.Version, deployment, EfAiSummaryStore.TruncateModelId(attempt.ModelId),
                        attempts, inputTokens, outputTokens, clock.GetUtcNow());
                    break;
                }

                rejected = result.Violations;
                LogInvalidOutput(attempts, string.Join(",", result.Codes.Select(c => c.ToCode())));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = AiGenerationOutcome.Cancelled;
            throw;
        }
        catch (OperationCanceledException)
        {
            outcome = AiGenerationOutcome.Timeout;
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            // The throttled call was not billed; only earlier attempts of this generation were.
            attempts--;
            outcome = AiGenerationOutcome.Throttled;
            retryAfter = RetryAfterOf(ex, clock.GetUtcNow());
            LogThrottled(attempts + 1, retryAfter?.TotalSeconds);
        }
        catch (Exception ex) when (IsCredentialFailure(ex))
        {
            // No token, so no request left this process. The exception type only: credential messages list the probed sources.
            attempts--;
            credentialFailed = true;
            outcome = AiGenerationOutcome.ProviderError;
            LogCredentialFailure(ex.GetType().Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception type only: provider messages can carry request details.
            outcome = AiGenerationOutcome.ProviderError;
            LogProviderError(ex.GetType().Name, attempts);
        }
        finally
        {
            var duration = (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            // Every path that sent a request has attempts >= 1; a throttled or credential-less generation with nothing billed
            // leaves no trace in the budgets.
            if (attempts > 0)
            {
                await ledger.RecordAsync(
                    new AiGenerationRecord(
                        request.UserId, request.IsGuest, clock.GetUtcNow(), outcome, attempts, inputTokens, outputTokens, duration,
                        agent.Prompt.Version, deployment),
                    CancellationToken.None);
            }

            LogGeneration(outcome, attempts, inputTokens, outputTokens, duration, deployment, agent.Prompt.Version);
        }

        if (credentialFailed)
        {
            return new GenerationResult(GenerationStatus.Unavailable);
        }

        return outcome switch
        {
            AiGenerationOutcome.Succeeded => new GenerationResult(GenerationStatus.Succeeded, summary),
            AiGenerationOutcome.Throttled => new GenerationResult(GenerationStatus.Busy, RetryAfter: retryAfter),
            AiGenerationOutcome.Timeout => new GenerationResult(GenerationStatus.Failed, Failure: GenerationFailure.Timeout),
            AiGenerationOutcome.ProviderError => new GenerationResult(GenerationStatus.Failed, Failure: GenerationFailure.Provider),
            _ => new GenerationResult(GenerationStatus.Failed, Failure: GenerationFailure.InvalidOutput),
        };
    }

    /// <summary>True for an Azure.Identity failure to get a token (<see cref="CredentialUnavailableException"/> derives from <see cref="AuthenticationFailedException"/>), also when wrapped.</summary>
    internal static bool IsCredentialFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationFailedException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The provider's wait hint: <c>retry-after-ms</c> / <c>x-ms-retry-after-ms</c> (Azure) or <c>Retry-After</c> (seconds or an HTTP
    /// date), rounded up to whole seconds and capped at <see cref="MaxRetryAfter"/>; null when absent or unparsable.
    /// </summary>
    internal static TimeSpan? RetryAfterOf(ClientResultException exception, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var headers = exception.GetRawResponse()?.Headers;
        if (headers is null)
        {
            return null;
        }

        TimeSpan? wait = null;
        foreach (var name in new[] { "retry-after-ms", "x-ms-retry-after-ms" })
        {
            if (headers.TryGetValue(name, out var ms) && double.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var millis) && millis >= 0)
            {
                wait = TimeSpan.FromMilliseconds(millis);
                break;
            }
        }

        if (wait is null && headers.TryGetValue("Retry-After", out var value) && value is not null)
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                wait = TimeSpan.FromSeconds(seconds);
            }
            else if (DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                wait = at - now;
            }
        }

        if (wait is not { } w)
        {
            return null;
        }

        var rounded = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(1, w.TotalSeconds)));
        return rounded > MaxRetryAfter ? MaxRetryAfter : rounded;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI summary generation: {Outcome} after {Attempts} attempt(s), {InputTokens} input / {OutputTokens} output tokens, {DurationMs} ms, deployment {Deployment}, prompt v{PromptVersion}.")]
    private partial void LogGeneration(AiGenerationOutcome outcome, int attempts, int inputTokens, int outputTokens, int durationMs, string deployment, int promptVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI summary attempt {Attempt} was rejected by the validator: {Codes}.")]
    private partial void LogInvalidOutput(int attempt, string codes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI summary provider call failed with {ExceptionType} on attempt {Attempt}.")]
    private partial void LogProviderError(string exceptionType, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI summary provider throttled attempt {Attempt} (429); retry after {RetryAfterSeconds} s.")]
    private partial void LogThrottled(int attempt, double? retryAfterSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "AI summary credential failure ({ExceptionType}): no token for the model endpoint.")]
    private partial void LogCredentialFailure(string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "The fact sheet has {Length} characters, above the cap of {Cap}; not sent to the model.")]
    private partial void LogFactSheetTooLarge(int length, int cap);
}
