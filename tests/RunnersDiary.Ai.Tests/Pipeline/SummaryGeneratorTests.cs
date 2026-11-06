using System.Net;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>Plan AC3 against the scripted client: attempts, retry with codes only, timeout, cancellation, guards and the ledger.</summary>
public sealed class SummaryGeneratorTests
{
    private static readonly EvalCase Spike = EvalCases.Get(EvalCases.LoadSpike);
    private static string Good => EvalFiles.GoodDraft(EvalCases.LoadSpike);
    private static string Bad => File.ReadAllText(Path.Combine(EvalFiles.CaseDirectory(EvalCases.LoadSpike), "bad-literalNumber.json"));

    [Fact]
    public async Task Good_first_attempt_succeeds_and_is_recorded()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply(Good, inputTokens: 5200, outputTokens: 640, modelId: "gpt-5.4-mini-2026-03-17"));

        var result = await harness.Generate(Spike.Sheet);

        Assert.Equal(GenerationStatus.Succeeded, result.Status);
        var summary = result.Summary!;
        Assert.Equal(1, summary.Attempts);
        Assert.Equal(2, summary.PromptVersion);
        Assert.Equal("gpt-test", summary.ModelDeployment);
        Assert.Equal("gpt-5.4-mini-2026-03-17", summary.ModelId);
        Assert.Equal(Spike.Sheet.Number("week.4.load"), summary.Content.Values["week.4.load"].Number);
        Assert.Equal(harness.Clock.GetUtcNow(), summary.CreatedAt);

        var row = Assert.Single(harness.Generations.Rows);
        Assert.Equal(new AiGenerationRecord(AiHarness.User, false, harness.Clock.GetUtcNow(), AiGenerationOutcome.Succeeded, 1, 5200, 640, row.DurationMs, 2, "gpt-test"), row);
    }

    [Fact]
    public async Task Bad_then_good_retries_once_with_rule_codes_only()
    {
        var scripted = new ScriptedChatClient().Reply(Bad, 5000, 600).Reply(Good, 5100, 650);
        var harness = new AiHarness(scripted);

        var result = await harness.Generate(Spike.Sheet);

        Assert.Equal(GenerationStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Summary!.Attempts);
        Assert.Equal(10_100, result.Summary.InputTokens);
        Assert.Equal(1250, result.Summary.OutputTokens);

        var retry = scripted.Requests[1].Messages;
        Assert.Equal(2, retry.Count);
        Assert.Equal(scripted.Requests[0].Messages[0].Text, retry[0].Text);
        Assert.Contains("Violated rules: literalNumber", retry[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("80", retry[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("rose by", retry[1].Text, StringComparison.Ordinal);

        var row = Assert.Single(harness.Generations.Rows);
        Assert.Equal((AiGenerationOutcome.Succeeded, 2, 10_100, 1250), (row.Outcome, row.Attempts, row.InputTokens, row.OutputTokens));
    }

    [Fact]
    public async Task Bad_twice_fails_with_invalid_output_and_counts()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply(Bad).Reply("not json"));

        var result = await harness.Generate(Spike.Sheet);

        Assert.Equal(GenerationStatus.Failed, result.Status);
        Assert.Equal(GenerationFailure.InvalidOutput, result.Failure);
        Assert.Null(result.Summary);
        Assert.Equal(2, harness.Scripted!.CallCount);
        var row = Assert.Single(harness.Generations.Rows);
        Assert.Equal((AiGenerationOutcome.InvalidOutput, 2), (row.Outcome, row.Attempts));
    }

    [Fact]
    public async Task Max_attempts_one_disables_the_retry()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply(Bad), o => o.MaxAttempts = 1);
        var result = await harness.Generate(Spike.Sheet);
        Assert.Equal(GenerationFailure.InvalidOutput, result.Failure);
        Assert.Equal(1, harness.Scripted!.CallCount);
    }

    [Fact]
    public async Task An_attempt_that_exceeds_the_timeout_fails_with_timeout_and_is_not_retried()
    {
        var scripted = new ScriptedChatClient().Hang().Reply(Good);
        var harness = new AiHarness(scripted);

        var started = scripted.RequestStarted.Task;
        var generation = harness.Generate(Spike.Sheet);
        await started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        harness.Clock.Advance(TimeSpan.FromSeconds(31));
        var result = await generation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(GenerationStatus.Failed, result.Status);
        Assert.Equal(GenerationFailure.Timeout, result.Failure);
        Assert.Equal(1, scripted.CallCount);
        Assert.Equal(AiGenerationOutcome.Timeout, Assert.Single(harness.Generations.Rows).Outcome);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_recorded()
    {
        var scripted = new ScriptedChatClient().Hang();
        var harness = new AiHarness(scripted);
        using var cts = new CancellationTokenSource();

        var started = scripted.RequestStarted.Task;
        var generation = harness.GenerateWithToken(Spike.Sheet, cts.Token);
        await started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation);
        Assert.Equal(AiGenerationOutcome.Cancelled, Assert.Single(harness.Generations.Rows).Outcome);

        // The in-flight entry and the slot were released.
        Assert.NotNull(harness.InFlight.TryEnter(AiHarness.User));
        Assert.NotNull(harness.Slots.TryAcquire());
    }

    [Fact]
    public async Task A_provider_error_fails_without_retry()
    {
        var harness = new AiHarness(new ScriptedChatClient().Throw(ProviderErrors.Status(System.Net.HttpStatusCode.InternalServerError)).Reply(Good));

        var result = await harness.Generate(Spike.Sheet);

        Assert.Equal(GenerationFailure.Provider, result.Failure);
        Assert.Equal(1, harness.Scripted!.CallCount);
        Assert.Equal(AiGenerationOutcome.ProviderError, Assert.Single(harness.Generations.Rows).Outcome);
    }

    /// <summary>M2: a timed-out attempt reports no usage, so the row keeps the best-known tokens and counts the attempt for the global budget.</summary>
    [Fact]
    public async Task A_timeout_after_an_invalid_attempt_records_both_attempts_and_the_known_tokens()
    {
        var scripted = new ScriptedChatClient().Reply(Bad, 5000, 600).Hang();
        var harness = new AiHarness(scripted);

        var generation = harness.Generate(Spike.Sheet);
        await WaitForCallAsync(scripted, 2);
        harness.Clock.Advance(TimeSpan.FromSeconds(31));
        var result = await generation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(GenerationFailure.Timeout, result.Failure);
        var row = Assert.Single(harness.Generations.Rows);
        Assert.Equal((AiGenerationOutcome.Timeout, 2, 5000, 600), (row.Outcome, row.Attempts, row.InputTokens, row.OutputTokens));
        Assert.Equal(2, (await harness.Generations.GetGlobalUsageAsync(harness.Clock.GetUtcNow().AddDays(-1), TestContext.Current.CancellationToken)).Attempts);
    }

    /// <summary>M4: a provider 429 is "busy", passes the provider's Retry-After on, writes no row and costs no quota.</summary>
    [Fact]
    public async Task A_provider_429_is_busy_with_its_retry_after_and_costs_nothing()
    {
        var harness = new AiHarness(
            new ScriptedChatClient().Throw(ProviderErrors.Throttled(new Dictionary<string, string> { ["Retry-After"] = "17" })).Reply(Good),
            o => o.Quotas.UserPer24h = 1);

        var result = await harness.Generate(Spike.Sheet);

        Assert.Equal(GenerationStatus.Busy, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(17), result.RetryAfter);
        Assert.Equal(1, harness.Scripted!.CallCount);
        Assert.Empty(harness.Generations.Rows);
        // The quota of one generation is still available.
        Assert.Equal(GenerationStatus.Succeeded, (await harness.Generate(Spike.Sheet)).Status);
    }

    /// <summary>M4: after a billed (invalid) attempt, a 429 writes a Throttled row: global attempts count, the caller's quota does not.</summary>
    [Fact]
    public async Task A_429_after_a_billed_attempt_counts_for_the_global_budget_only()
    {
        var harness = new AiHarness(
            new ScriptedChatClient().Reply(Bad, 5000, 600).Throw(ProviderErrors.Throttled(new Dictionary<string, string> { ["retry-after-ms"] = "2500" })),
            o => o.Quotas.UserPer24h = 1);

        var result = await harness.Generate(Spike.Sheet, guest: true);

        Assert.Equal((GenerationStatus.Busy, TimeSpan.FromSeconds(3)), (result.Status, result.RetryAfter!.Value));
        var row = Assert.Single(harness.Generations.Rows);
        Assert.Equal((AiGenerationOutcome.Throttled, 1, 5000, 600, true), (row.Outcome, row.Attempts, row.InputTokens, row.OutputTokens, row.IsGuest));
        var status = await harness.Ledger.GetStatusAsync(AiHarness.User, isGuest: true, TestContext.Current.CancellationToken);
        Assert.Equal((0, QuotaBlock.None), (status.Used, status.Block));
        var global = await harness.Generations.GetGlobalUsageAsync(harness.Clock.GetUtcNow().AddDays(-1), TestContext.Current.CancellationToken);
        Assert.Equal((1, 1), (global.Attempts, global.GuestAttempts));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("retry-after-ms", "100", 1)]
    [InlineData("x-ms-retry-after-ms", "4001", 5)]
    [InlineData("Retry-After", "0", 1)]
    [InlineData("Retry-After", "86400", 300)]
    [InlineData("Retry-After", "soon", null)]
    public void Retry_after_is_read_from_the_provider_headers(string? header, string? value, int? seconds)
    {
        var headers = header is null ? null : new Dictionary<string, string> { [header] = value! };
        Assert.Equal(seconds is { } s ? TimeSpan.FromSeconds(s) : null, SummaryGenerator.RetryAfterOf(ProviderErrors.Throttled(headers), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Retry_after_as_an_http_date_is_relative_to_now()
    {
        var now = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var headers = new Dictionary<string, string> { ["Retry-After"] = now.AddSeconds(42).ToString("r", System.Globalization.CultureInfo.InvariantCulture) };
        Assert.Equal(TimeSpan.FromSeconds(42), SummaryGenerator.RetryAfterOf(ProviderErrors.Throttled(headers), now));
    }

    /// <summary>L6: no token means nothing left the process: feature unavailable, no ledger row, the exception type only in the log.</summary>
    [Theory]
    [InlineData(typeof(CredentialUnavailableException))]
    [InlineData(typeof(AuthenticationFailedException))]
    public async Task A_credential_failure_is_unavailable_without_a_row(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "secret-ish detail about probed sources")!;
        var harness = new AiHarness(new ScriptedChatClient().Throw(exception));
        var logs = new CapturingLogger<SummaryGenerator>();
        var generator = new SummaryGenerator(harness.Agents, harness.Ledger, harness.Slots, harness.InFlight, harness.Monitor, harness.Clock, logs);

        var result = await generator.GenerateAsync(new GenerationRequest(AiHarness.User, false, Spike.Sheet), TestContext.Current.CancellationToken);

        Assert.Equal(GenerationStatus.Unavailable, result.Status);
        Assert.Empty(harness.Generations.Rows);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(exceptionType.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("secret-ish", StringComparison.Ordinal) || e.Exception is not null);
        Assert.True(SummaryGenerator.IsCredentialFailure(new InvalidOperationException("wrapped", exception)));
        Assert.False(SummaryGenerator.IsCredentialFailure(new InvalidOperationException("other")));
    }

    /// <summary>L8: the kill switch is read per generation, so flipping it applies without a restart.</summary>
    [Fact]
    public async Task The_kill_switch_applies_at_runtime()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply(Good));

        harness.Options.Enabled = false;
        Assert.Equal(GenerationStatus.Unavailable, (await harness.Generate(Spike.Sheet)).Status);
        harness.Options.Enabled = true;
        Assert.Equal(GenerationStatus.Succeeded, (await harness.Generate(Spike.Sheet)).Status);
        Assert.Equal(1, harness.Scripted!.CallCount);
    }

    /// <summary>M4 + L6 end to end through the real Foundry client stack (capturing transport, fake credential): no network.</summary>
    [Fact]
    public async Task Through_the_foundry_client_a_429_is_busy_and_a_credential_failure_is_unavailable()
    {
        var options = new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = "https://res.openai.azure.com/", Deployment = "gpt-5.4-mini" } };
        var throttle = new Dictionary<string, string> { ["Retry-After"] = "1" };
        using var transport = new CapturingTransport()
            .Respond(HttpStatusCode.TooManyRequests, """{"error":{"code":"429","message":"Rate limit"}}""", throttle)
            .Respond(HttpStatusCode.TooManyRequests, """{"error":{"code":"429","message":"Rate limit"}}""", throttle);
        var throttled = new AiHarness(ChatClientFactory.CreateFoundryClient(options, new RecordingCredential(), transport.Transport));

        var busy = await throttled.Generate(Spike.Sheet);

        Assert.Equal((GenerationStatus.Busy, TimeSpan.FromSeconds(1)), (busy.Status, busy.RetryAfter!.Value));
        Assert.Equal(2, transport.Requests.Count); // the transport retried once
        Assert.Empty(throttled.Generations.Rows);

        using var silent = new CapturingTransport();
        var noToken = new AiHarness(ChatClientFactory.CreateFoundryClient(options, new RecordingCredential(new CredentialUnavailableException("no az login")), silent.Transport));

        Assert.Equal(GenerationStatus.Unavailable, (await noToken.Generate(Spike.Sheet)).Status);
        Assert.Empty(silent.Requests);
        Assert.Empty(noToken.Generations.Rows);
    }

    private static async Task WaitForCallAsync(ScriptedChatClient scripted, int calls)
    {
        for (var i = 0; i < 500 && scripted.CallCount < calls; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(calls, scripted.CallCount);
    }

    [Fact]
    public async Task No_provider_or_kill_switch_is_unavailable_without_a_call()
    {
        Assert.Equal(GenerationStatus.Unavailable, (await new AiHarness(client: null).Generate(Spike.Sheet)).Status);

        var off = new AiHarness(new ScriptedChatClient(), o => o.Enabled = false);
        Assert.Equal(GenerationStatus.Unavailable, (await off.Generate(Spike.Sheet)).Status);
        Assert.Equal(0, off.Scripted!.CallCount);
        Assert.Empty(off.Generations.Rows);
    }

    [Fact]
    public async Task A_second_generation_for_the_same_user_is_in_progress()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply(Good));
        using (harness.InFlight.TryEnter(AiHarness.User))
        {
            Assert.Equal(GenerationStatus.InProgress, (await harness.Generate(Spike.Sheet)).Status);
            Assert.Equal(GenerationStatus.Succeeded, (await harness.Generate(Spike.Sheet, userId: Guid.NewGuid())).Status);
        }

        Assert.Single(harness.Generations.Rows);
    }

    [Fact]
    public async Task No_free_slot_is_busy_and_zero_slots_always_busy()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply(Good), o => o.MaxConcurrentGenerations = 1);
        using (harness.Slots.TryAcquire())
        {
            Assert.Equal(GenerationStatus.Busy, (await harness.Generate(Spike.Sheet)).Status);
        }

        Assert.Equal(GenerationStatus.Succeeded, (await harness.Generate(Spike.Sheet)).Status);
        Assert.Equal(GenerationStatus.Busy, (await new AiHarness(new ScriptedChatClient(), o => o.MaxConcurrentGenerations = 0).Generate(Spike.Sheet)).Status);
    }

    [Fact]
    public async Task Quota_is_checked_before_the_model_and_failed_attempts_count()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply("x").Reply("y").Reply(Good), o => o.Quotas.UserPer24h = 2);

        Assert.Equal(GenerationStatus.Failed, (await harness.Generate(Spike.Sheet)).Status);
        Assert.Equal(GenerationStatus.Succeeded, (await harness.Generate(Spike.Sheet)).Status);
        var third = await harness.Generate(Spike.Sheet);

        Assert.Equal(GenerationStatus.QuotaExceeded, third.Status);
        Assert.Equal(QuotaBlock.Account, third.Quota!.Block);
        Assert.Equal(3, harness.Scripted!.CallCount);
        Assert.Equal(2, harness.Generations.Rows.Count);
    }

    [Fact]
    public async Task An_oversized_fact_sheet_is_never_sent()
    {
        var harness = new AiHarness(new ScriptedChatClient().Reply(Good), o => o.MaxFactSheetChars = 1000);
        var result = await harness.Generate(Spike.Sheet);
        Assert.Equal((GenerationStatus.Failed, GenerationFailure.Provider), (result.Status, result.Failure!.Value));
        Assert.Equal(0, harness.Scripted!.CallCount);
        Assert.Empty(harness.Generations.Rows);
    }

    [Fact]
    public async Task The_rule_based_fake_passes_the_validator_for_every_model_case()
    {
        foreach (var evalCase in EvalCases.All.Where(c => c.ExpectedTemplate is null))
        {
            var harness = new AiHarness(new RuleBasedFakeChatClient(), deployment: ChatClientFactory.FakeDeployment);
            var result = await harness.Generate(evalCase.Sheet);
            Assert.True(result.Status == GenerationStatus.Succeeded, $"{evalCase.Name}: {result.Status} {result.Failure}");
            Assert.Equal(1, result.Summary!.Attempts);
            Assert.Equal(RuleBasedFakeChatClient.ModelId, result.Summary.ModelId);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
