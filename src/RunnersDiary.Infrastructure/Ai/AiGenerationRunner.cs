using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Ai;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>Bounds concurrent generations per replica, all features together (<see cref="AiOptions.MaxConcurrentGenerations"/>, ADR 0028 §3).</summary>
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

/// <summary>
/// One generation per (feature, user) at a time on this replica (409 <c>summary-in-progress</c> / <c>plan-in-progress</c>, ADR 0028 §3,
/// ADR 0040 §6): a running plan does not block the same user's summary, and the other way round.
/// </summary>
public sealed class GenerationInFlightGuard
{
    private readonly ConcurrentDictionary<(AiFeature Feature, Guid UserId), byte> _inFlight = new();

    /// <summary>The summary guard (M6).</summary>
    public IDisposable? TryEnter(Guid userId) => TryEnter(AiFeature.Summary, userId);

    public IDisposable? TryEnter(AiFeature feature, Guid userId) =>
        _inFlight.TryAdd((feature, userId), 0) ? new Entry(_inFlight, (feature, userId)) : null;

    private sealed class Entry(ConcurrentDictionary<(AiFeature, Guid), byte> inFlight, (AiFeature, Guid) key) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                inFlight.TryRemove(key, out _);
            }
        }
    }
}

public enum GenerationStatus
{
    Succeeded,

    /// <summary>No provider or the kill switch is off: 503 <c>feature-unavailable</c>.</summary>
    Unavailable,

    /// <summary>The same user already has a generation of this feature running: 409 <c>summary-in-progress</c> / <c>plan-in-progress</c>.</summary>
    InProgress,

    /// <summary>429 <c>ai-quota</c> (see <see cref="GenerationResult.Quota"/>).</summary>
    QuotaExceeded,

    /// <summary>
    /// No free slot on this replica, or the provider throttled (429 after the transport retry): 503 <c>ai-busy</c>, with
    /// <see cref="GenerationResult.RetryAfter"/> when the provider gave one. Costs the caller no quota.
    /// </summary>
    Busy,

    /// <summary>502 <c>summary-failed</c> / <c>plan-failed</c> with a <see cref="GenerationFailure"/>; nothing stored.</summary>
    Failed,
}

/// <summary>The <c>reason</c> of 502 <c>summary-failed</c> / <c>plan-failed</c>.</summary>
public enum GenerationFailure
{
    InvalidOutput,
    Timeout,
    Provider,
}

/// <summary>Who asks for which feature. The user id always comes from the caller's claims, never from a request body or the model.</summary>
public sealed record AiRunRequest(AiFeature Feature, Guid UserId, bool IsGuest);

/// <summary>The per-feature limits of one run.</summary>
/// <param name="TotalTimeout">The deadline of the whole run (all attempts), or null for none (summaries: only the per-attempt timeout).</param>
/// <param name="PromptVersion">Written to the ledger row.</param>
/// <param name="Deployment">Written to the ledger row.</param>
public sealed record AiRunSettings(int MaxAttempts, TimeSpan AttemptTimeout, TimeSpan? TotalTimeout, int PromptVersion, string Deployment)
{
    /// <summary>With a <see cref="TotalTimeout"/>, a retry starts only if at least this much time is left (ADR 0040 §6).</summary>
    public static readonly TimeSpan MinRetryWindow = TimeSpan.FromSeconds(30);
}

/// <summary>
/// One model call as the feature saw it: usage, the model id, and either the validated value or the rule codes that rejected it (codes only,
/// for the log; the feature keeps what it needs for its retry message).
/// </summary>
public sealed record AiAttemptResult<T>(int InputTokens, int OutputTokens, string? ModelId, T? Value, IReadOnlyList<string> RejectedCodes)
    where T : class;

/// <summary>The outcome of <see cref="AiGenerationRunner.RunAsync"/>. On success <see cref="Value"/> is set and the usage is the sum over all attempts.</summary>
public sealed record AiRunResult<T>(
    GenerationStatus Status,
    T? Value = null,
    int Attempts = 0,
    int InputTokens = 0,
    int OutputTokens = 0,
    string? ModelId = null,
    DateTimeOffset CreatedAt = default,
    QuotaStatus? Quota = null,
    GenerationFailure? Failure = null,
    TimeSpan? RetryAfter = null)
    where T : class;

/// <summary>
/// The shared generation pipeline of all AI features (ADR 0040 §6, extracted from the M6 summary generator without a behaviour change): the
/// in-flight guard per (feature, user), the durable quotas of the feature, a replica slot, then at most <see cref="AiRunSettings.MaxAttempts"/>
/// attempts of at most <see cref="AiRunSettings.AttemptTimeout"/> each (and within <see cref="AiRunSettings.TotalTimeout"/>). An invalid output
/// is retried once (the attempt delegate builds the retry message from rule codes only); a timeout or a provider error is not retried. Every
/// run that reached the model writes one ledger row (feature, tokens, outcome; never content), also when the caller cancels. Cancellation by the
/// caller propagates as <see cref="OperationCanceledException"/>.
/// <para>
/// Two provider-side conditions are not the caller's fault and cost no quota: a <b>429</b> (after the transport retry) answers
/// <see cref="GenerationStatus.Busy"/> with the provider's Retry-After and writes a <see cref="AiGenerationOutcome.Throttled"/> row only if an
/// earlier attempt was billed (for the global attempt budget); a <b>credential failure</b> before any request (no token for the managed identity
/// or the developer) answers <see cref="GenerationStatus.Unavailable"/> and writes no row unless an earlier attempt was billed.
/// </para>
/// The availability checks (provider, kill switches) and the fact-sheet size guard stay with each feature, before the runner.
/// </summary>
public sealed partial class AiGenerationRunner(
    AiUsageLedger ledger, GenerationSlots slots, GenerationInFlightGuard inFlight, TimeProvider clock, ILogger logger)
{
    /// <summary>The longest provider Retry-After passed on to the client.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

    /// <param name="attempt">One model call plus validation. Called at most <see cref="AiRunSettings.MaxAttempts"/> times; on a rejected value the next call is the retry.</param>
    public async Task<AiRunResult<T>> RunAsync<T>(
        AiRunRequest request, AiRunSettings settings, Func<CancellationToken, Task<AiAttemptResult<T>>> attempt, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(attempt);

        using var entry = inFlight.TryEnter(request.Feature, request.UserId);
        if (entry is null)
        {
            return new AiRunResult<T>(GenerationStatus.InProgress);
        }

        var quota = await ledger.GetStatusAsync(request.Feature, request.UserId, request.IsGuest, cancellationToken);
        if (quota.Block != QuotaBlock.None)
        {
            return new AiRunResult<T>(GenerationStatus.QuotaExceeded, Quota: quota);
        }

        using var slot = slots.TryAcquire();
        if (slot is null)
        {
            return new AiRunResult<T>(GenerationStatus.Busy);
        }

        return await RunAttemptsAsync(request, settings, attempt, cancellationToken);
    }

    private async Task<AiRunResult<T>> RunAttemptsAsync<T>(
        AiRunRequest request, AiRunSettings settings, Func<CancellationToken, Task<AiAttemptResult<T>>> attempt, CancellationToken cancellationToken)
        where T : class
    {
        var feature = FeatureName(request.Feature);
        var started = Stopwatch.GetTimestamp();
        var startedAt = clock.GetUtcNow();
        var attempts = 0;
        var inputTokens = 0;
        var outputTokens = 0;
        var outcome = AiGenerationOutcome.InvalidOutput;
        T? value = null;
        string? modelId = null;
        var credentialFailed = false;
        TimeSpan? retryAfter = null;

        try
        {
            while (attempts < settings.MaxAttempts)
            {
                var budget = settings.AttemptTimeout;
                if (settings.TotalTimeout is { } total)
                {
                    var remaining = total - (clock.GetUtcNow() - startedAt);
                    if (attempts > 0 && remaining < AiRunSettings.MinRetryWindow)
                    {
                        // Not enough time left for a useful retry: the run fails with the first attempt's invalid output.
                        LogRetrySkipped(logger, feature, Math.Max(0, remaining.TotalSeconds));
                        break;
                    }

                    budget = remaining < budget ? remaining : budget;
                }

                // Counted before the call: a timed-out or failed attempt may still be billed, and its usage never comes back.
                attempts++;
                using var timer = new CancellationTokenSource(budget, clock);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);

                var result = await attempt(timeout.Token);
                inputTokens += result.InputTokens;
                outputTokens += result.OutputTokens;
                modelId = result.ModelId;
                if (result.Value is not null)
                {
                    outcome = AiGenerationOutcome.Succeeded;
                    value = result.Value;
                    break;
                }

                LogInvalidOutput(logger, feature, attempts, string.Join(",", result.RejectedCodes));
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
            LogThrottled(logger, feature, attempts + 1, retryAfter?.TotalSeconds);
        }
        catch (Exception ex) when (IsCredentialFailure(ex))
        {
            // No token, so no request left this process. The exception type only: credential messages list the probed sources.
            attempts--;
            credentialFailed = true;
            outcome = AiGenerationOutcome.ProviderError;
            LogCredentialFailure(logger, feature, ex.GetType().Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception type only: provider messages can carry request details.
            outcome = AiGenerationOutcome.ProviderError;
            LogProviderError(logger, feature, ex.GetType().Name, attempts);
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
                        settings.PromptVersion, settings.Deployment, request.Feature),
                    CancellationToken.None);
            }

            LogGeneration(logger, feature, outcome, attempts, inputTokens, outputTokens, duration, settings.Deployment, settings.PromptVersion);
        }

        if (credentialFailed)
        {
            return new AiRunResult<T>(GenerationStatus.Unavailable);
        }

        return outcome switch
        {
            AiGenerationOutcome.Succeeded => new AiRunResult<T>(
                GenerationStatus.Succeeded, value, attempts, inputTokens, outputTokens, modelId, clock.GetUtcNow()),
            AiGenerationOutcome.Throttled => new AiRunResult<T>(GenerationStatus.Busy, RetryAfter: retryAfter),
            AiGenerationOutcome.Timeout => new AiRunResult<T>(GenerationStatus.Failed, Failure: GenerationFailure.Timeout),
            AiGenerationOutcome.ProviderError => new AiRunResult<T>(GenerationStatus.Failed, Failure: GenerationFailure.Provider),
            _ => new AiRunResult<T>(GenerationStatus.Failed, Failure: GenerationFailure.InvalidOutput),
        };
    }

    private static string FeatureName(AiFeature feature) => feature switch
    {
        AiFeature.Plan => "plan",
        _ => "summary",
    };

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

    [LoggerMessage(Level = LogLevel.Information, Message = "AI {Feature} generation: {Outcome} after {Attempts} attempt(s), {InputTokens} input / {OutputTokens} output tokens, {DurationMs} ms, deployment {Deployment}, prompt v{PromptVersion}.")]
    private static partial void LogGeneration(ILogger logger, string feature, AiGenerationOutcome outcome, int attempts, int inputTokens, int outputTokens, int durationMs, string deployment, int promptVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI {Feature} attempt {Attempt} was rejected by the validator: {Codes}.")]
    private static partial void LogInvalidOutput(ILogger logger, string feature, int attempt, string codes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI {Feature} retry skipped: {RemainingSeconds} s left of the total timeout.")]
    private static partial void LogRetrySkipped(ILogger logger, string feature, double remainingSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI {Feature} provider call failed with {ExceptionType} on attempt {Attempt}.")]
    private static partial void LogProviderError(ILogger logger, string feature, string exceptionType, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI {Feature} provider throttled attempt {Attempt} (429); retry after {RetryAfterSeconds} s.")]
    private static partial void LogThrottled(ILogger logger, string feature, int attempt, double? retryAfterSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "AI {Feature} credential failure ({ExceptionType}): no token for the model endpoint.")]
    private static partial void LogCredentialFailure(ILogger logger, string feature, string exceptionType);
}
