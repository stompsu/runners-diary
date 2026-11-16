using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>
/// The shared runner (ADR 0040 §6, M9 step 4): per-feature guard and quotas, the weighted global budget, the total deadline and the ledger
/// feature. The summary behaviour itself is covered unchanged by <see cref="SummaryGeneratorTests"/>.
/// </summary>
public sealed class AiGenerationRunnerTests
{
    private static readonly Guid User = new("22222222-2222-2222-2222-222222222222");

    private sealed class Rig
    {
        public Rig(Action<AiOptions>? configure = null)
        {
            configure?.Invoke(Options);
            Ledger = new AiUsageLedger(Store, Microsoft.Extensions.Options.Options.Create(Options), Clock);
            Slots = new GenerationSlots(Microsoft.Extensions.Options.Options.Create(Options));
            Runner = new AiGenerationRunner(Ledger, Slots, InFlight, Clock, NullLogger.Instance);
        }

        public AiOptions Options { get; } = new() { Provider = AiProvider.Fake };

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));

        public InMemoryAiGenerationStore Store { get; } = new();

        public AiUsageLedger Ledger { get; }

        public GenerationSlots Slots { get; }

        public GenerationInFlightGuard InFlight { get; } = new();

        public AiGenerationRunner Runner { get; }

        public int Calls { get; private set; }

        public static AiRunSettings PlanSettings(TimeSpan? attempt = null, TimeSpan? total = null) =>
            new(2, attempt ?? TimeSpan.FromSeconds(90), total ?? TimeSpan.FromSeconds(190), 1, "gpt-test");

        public static AiRunSettings SummarySettings => new(2, TimeSpan.FromSeconds(30), null, 2, "gpt-test");

        /// <summary>Replies valid ("ok") or invalid (null value, a rule code), optionally advancing the clock first (a slow model).</summary>
        public Func<CancellationToken, Task<AiAttemptResult<string>>> Script(params (bool Valid, TimeSpan Takes)[] steps)
        {
            var queue = new Queue<(bool Valid, TimeSpan Takes)>(steps);
            return _ =>
            {
                Calls++;
                var (valid, takes) = queue.Dequeue();
                Clock.Advance(takes);
                return Task.FromResult(new AiAttemptResult<string>(1000, 400, "m", valid ? "ok" : null, valid ? [] : ["weeklyVolume"]));
            };
        }

        public Task<AiRunResult<string>> Run(AiFeature feature, AiRunSettings settings, Func<CancellationToken, Task<AiAttemptResult<string>>> attempt, bool guest = false) =>
            Runner.RunAsync(new AiRunRequest(feature, User, guest), settings, attempt, TestContext.Current.CancellationToken);

        public Task Seed(AiFeature feature, int attempts = 1, bool guest = false, Guid? user = null) =>
            Store.AddAsync(
                new AiGenerationRecord(user ?? User, guest, Clock.GetUtcNow().AddHours(-1), AiGenerationOutcome.Succeeded, attempts, 10, 10, 10, 1, "gpt-test", feature),
                CancellationToken.None);
    }

    [Fact]
    public async Task A_plan_run_is_recorded_with_the_plan_feature_and_the_summed_usage()
    {
        var rig = new Rig();

        var result = await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((false, TimeSpan.FromSeconds(10)), (true, TimeSpan.FromSeconds(10))));

        Assert.Equal((GenerationStatus.Succeeded, "ok", 2, 2000, 800), (result.Status, result.Value, result.Attempts, result.InputTokens, result.OutputTokens));
        var row = Assert.Single(rig.Store.Rows);
        Assert.Equal((AiFeature.Plan, AiGenerationOutcome.Succeeded, 2, 1), (row.Feature, row.Outcome, row.Attempts, row.PromptVersion));
    }

    [Fact]
    public async Task Account_quotas_count_only_the_rows_of_their_own_feature()
    {
        var rig = new Rig(o =>
        {
            o.Quotas.UserPer24h = 1;
            o.Plan.Quotas.UserPer24h = 2;
        });
        await rig.Seed(AiFeature.Summary);

        // The summary quota is used up; plans are not affected.
        Assert.Equal(QuotaBlock.Account, (await rig.Ledger.GetStatusAsync(User, false, TestContext.Current.CancellationToken)).Block);
        var plan = await rig.Ledger.GetStatusAsync(AiFeature.Plan, User, false, TestContext.Current.CancellationToken);
        Assert.Equal((0, 2, QuotaBlock.None), (plan.Used, plan.Limit, plan.Block));

        Assert.Equal(GenerationStatus.Succeeded, (await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((true, TimeSpan.Zero)))).Status);
        Assert.Equal(GenerationStatus.Succeeded, (await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((true, TimeSpan.Zero)))).Status);
        var third = await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((true, TimeSpan.Zero)));

        Assert.Equal(GenerationStatus.QuotaExceeded, third.Status);
        Assert.Equal((QuotaBlock.Account, 2), (third.Quota!.Block, third.Quota.BlockLimit));
        // The summary quota still counts one row (the plans are not in it).
        Assert.Equal(1, (await rig.Ledger.GetStatusAsync(User, false, TestContext.Current.CancellationToken)).Used);
    }

    [Fact]
    public async Task Guests_have_a_per_account_plan_quota_of_two_by_default()
    {
        var rig = new Rig();
        await rig.Seed(AiFeature.Plan, guest: true);
        await rig.Seed(AiFeature.Plan, guest: true);

        var status = await rig.Ledger.GetStatusAsync(AiFeature.Plan, User, true, TestContext.Current.CancellationToken);

        Assert.Equal((2, 2, QuotaWindow.Account, QuotaBlock.Account, (DateTimeOffset?)null), (status.Used, status.Limit, status.Window, status.Block, status.ResetsAt));
    }

    /// <summary>Plan §9: 2 plan attempts + 5 summary attempts = 2 × 3 + 5 = 11 weighted units.</summary>
    [Theory]
    [InlineData(11, QuotaBlock.Global)]
    [InlineData(12, QuotaBlock.None)]
    public async Task The_global_budget_counts_plan_attempts_with_their_weight(int globalLimit, QuotaBlock expected)
    {
        var rig = new Rig(o => o.Quotas.GlobalPer24h = globalLimit);
        var other = Guid.NewGuid();
        await rig.Seed(AiFeature.Plan, attempts: 2, user: other);
        for (var i = 0; i < 5; i++)
        {
            await rig.Seed(AiFeature.Summary, user: other);
        }

        var usage = await rig.Store.GetGlobalUsageAsync(rig.Clock.GetUtcNow().AddDays(-1), 3, TestContext.Current.CancellationToken);
        Assert.Equal(11, usage.Attempts);
        Assert.Equal(expected, (await rig.Ledger.GetStatusAsync(User, false, TestContext.Current.CancellationToken)).Block);
        Assert.Equal(expected, (await rig.Ledger.GetStatusAsync(AiFeature.Plan, User, false, TestContext.Current.CancellationToken)).Block);
    }

    [Fact]
    public async Task The_guest_global_budget_is_weighted_too()
    {
        var rig = new Rig(o =>
        {
            o.Quotas.GuestGlobalPer24h = 6;
            o.Plan.GlobalAttemptWeight = 3;
        });
        await rig.Seed(AiFeature.Plan, attempts: 2, guest: true, user: Guid.NewGuid());

        var status = await rig.Ledger.GetStatusAsync(AiFeature.Summary, User, true, TestContext.Current.CancellationToken);

        Assert.Equal((QuotaBlock.Global, 6), (status.Block, status.BlockLimit));
    }

    [Fact]
    public async Task The_in_flight_guard_is_per_feature_and_user()
    {
        var rig = new Rig();
        using (rig.InFlight.TryEnter(AiFeature.Plan, User))
        {
            Assert.Equal(GenerationStatus.InProgress, (await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((true, TimeSpan.Zero)))).Status);
            // The same user's summary runs.
            Assert.Equal(GenerationStatus.Succeeded, (await rig.Run(AiFeature.Summary, Rig.SummarySettings, rig.Script((true, TimeSpan.Zero)))).Status);
        }

        Assert.Equal(1, rig.Calls);
        Assert.NotNull(rig.InFlight.TryEnter(AiFeature.Plan, User));
        // The M6 overload is the summary guard.
        using (rig.InFlight.TryEnter(User))
        {
            Assert.Null(rig.InFlight.TryEnter(AiFeature.Summary, User));
        }
    }

    [Fact]
    public async Task The_total_deadline_skips_a_retry_with_less_than_thirty_seconds_left()
    {
        var rig = new Rig();

        // The first (invalid) attempt takes 165 s of the 190 s budget: 25 s are left, so no retry.
        var result = await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((false, TimeSpan.FromSeconds(165)), (true, TimeSpan.Zero)));

        Assert.Equal((GenerationStatus.Failed, GenerationFailure.InvalidOutput), (result.Status, result.Failure!.Value));
        Assert.Equal(1, rig.Calls);
        var row = Assert.Single(rig.Store.Rows);
        Assert.Equal((AiFeature.Plan, AiGenerationOutcome.InvalidOutput, 1), (row.Feature, row.Outcome, row.Attempts));
    }

    [Fact]
    public async Task With_thirty_seconds_or_more_left_the_retry_runs()
    {
        var rig = new Rig();

        var result = await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((false, TimeSpan.FromSeconds(160)), (true, TimeSpan.Zero)));

        Assert.Equal(GenerationStatus.Succeeded, result.Status);
        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task The_retry_gets_only_the_time_left_of_the_total_deadline()
    {
        var rig = new Rig();
        var calls = 0;
        TimeSpan? secondBudget = null;

        var run = rig.Runner.RunAsync<string>(
            new AiRunRequest(AiFeature.Plan, User, false),
            Rig.PlanSettings(),
            async ct =>
            {
                if (++calls == 1)
                {
                    rig.Clock.Advance(TimeSpan.FromSeconds(140));
                    return new AiAttemptResult<string>(1, 1, null, null, ["schema"]);
                }

                // 50 s are left: advancing 49 s keeps the attempt alive, 1 s more cancels it.
                rig.Clock.Advance(TimeSpan.FromSeconds(49));
                Assert.False(ct.IsCancellationRequested);
                rig.Clock.Advance(TimeSpan.FromSeconds(1));
                secondBudget = ct.IsCancellationRequested ? TimeSpan.FromSeconds(50) : null;
                await Task.Delay(Timeout.Infinite, ct);
                return null!;
            },
            TestContext.Current.CancellationToken);

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(50), secondBudget);
        Assert.Equal((GenerationStatus.Failed, GenerationFailure.Timeout), (result.Status, result.Failure!.Value));
        Assert.Equal((AiGenerationOutcome.Timeout, 2), (Assert.Single(rig.Store.Rows).Outcome, rig.Store.Rows[0].Attempts));
    }

    [Fact]
    public void The_shared_client_network_timeout_is_the_longest_attempt_timeout()
    {
        var options = new AiOptions();
        Assert.Equal(TimeSpan.FromSeconds(90), options.ClientNetworkTimeout);
        options.Plan.AttemptTimeout = TimeSpan.FromSeconds(20);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ClientNetworkTimeout);
    }

    [Theory]
    [InlineData("MaxOutputTokens", "255")]
    [InlineData("MaxOutputTokens", "16001")]
    [InlineData("ReasoningEffort", "none")]
    [InlineData("MaxAttempts", "3")]
    [InlineData("AttemptTimeout", "00:02:01")]
    [InlineData("TotalTimeout", "00:03:21")]
    [InlineData("TotalTimeout", "00:01:00")]
    [InlineData("GlobalAttemptWeight", "0")]
    [InlineData("GlobalAttemptWeight", "11")]
    [InlineData("MaxFactSheetChars", "999")]
    public void Invalid_plan_settings_fail_validation(string setting, string value)
    {
        var options = new AiOptions();
        var plan = options.Plan;
        switch (setting)
        {
            case "MaxOutputTokens": plan.MaxOutputTokens = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "ReasoningEffort": plan.ReasoningEffort = value; break;
            case "MaxAttempts": plan.MaxAttempts = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "AttemptTimeout": plan.AttemptTimeout = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "TotalTimeout": plan.TotalTimeout = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "GlobalAttemptWeight": plan.GlobalAttemptWeight = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            default: plan.MaxFactSheetChars = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
        }

        var result = new AiOptionsValidator(new TestEnvironment()).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Ai:Plan:" + setting, StringComparison.Ordinal));
    }

    [Fact]
    public void The_plan_defaults_are_valid_and_match_the_plan()
    {
        var options = new AiOptions();
        Assert.True(new AiOptionsValidator(new TestEnvironment()).Validate(null, options).Succeeded);
        var plan = options.Plan;
        Assert.Equal(
            (true, 8000, "low", TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(190), 2, 24_000, "prompts/training-plan", 5, 2, 3),
            (plan.Enabled, plan.MaxOutputTokens, plan.ReasoningEffort, plan.AttemptTimeout, plan.TotalTimeout, plan.MaxAttempts, plan.MaxFactSheetChars,
                plan.PromptsPath, plan.Quotas.UserPer24h, plan.Quotas.GuestPerAccount, plan.GlobalAttemptWeight));
        options.Plan.Enabled = false;
        Assert.False(options.PlanGenerationAvailable);
        options.Plan.Enabled = true;
        options.Provider = AiProvider.None;
        Assert.False(options.PlanGenerationAvailable);
    }

    /// <summary>Review L2: plans get at most <c>MaxConcurrentGenerations − 1</c> slots, so a summary always finds one (defaults 2 and 1).</summary>
    [Fact]
    public void A_plan_never_takes_the_last_replica_slot()
    {
        var rig = new Rig();

        var plan = rig.Slots.TryAcquire(AiFeature.Plan);
        Assert.NotNull(plan);
        Assert.Null(rig.Slots.TryAcquire(AiFeature.Plan));

        // The summary keeps its slot while the plan runs, and then all slots are taken.
        var summary = rig.Slots.TryAcquire(AiFeature.Summary);
        Assert.NotNull(summary);
        Assert.Null(rig.Slots.TryAcquire());

        // A failed plan acquisition released its plan slot: once the summary is done, a plan still has to wait for the running plan.
        summary.Dispose();
        Assert.Null(rig.Slots.TryAcquire(AiFeature.Plan));
        plan.Dispose();
        plan.Dispose(); // idempotent
        using var next = rig.Slots.TryAcquire(AiFeature.Plan);
        Assert.NotNull(next);
        using var other = rig.Slots.TryAcquire();
        Assert.NotNull(other);
        Assert.Null(rig.Slots.TryAcquire());
    }

    [Fact]
    public void Two_summaries_hold_both_slots_and_a_plan_then_finds_none()
    {
        var rig = new Rig();
        using var a = rig.Slots.TryAcquire();
        using var b = rig.Slots.TryAcquire(AiFeature.Summary);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Null(rig.Slots.TryAcquire(AiFeature.Plan));
        // The plan slot was given back: with the shared slots freed a plan runs.
        b!.Dispose();
        using var plan = rig.Slots.TryAcquire(AiFeature.Plan);
        Assert.NotNull(plan);
    }

    [Fact]
    public async Task While_a_plan_runs_a_second_plan_is_busy_and_a_summary_still_runs()
    {
        var rig = new Rig();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var running = rig.Runner.RunAsync<string>(
            new AiRunRequest(AiFeature.Plan, Guid.NewGuid(), true),
            Rig.PlanSettings(),
            async _ =>
            {
                started.SetResult();
                await release.Task;
                return new AiAttemptResult<string>(1, 1, "m", "ok", []);
            },
            TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var second = await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((true, TimeSpan.Zero)));
        var summary = await rig.Run(AiFeature.Summary, Rig.SummarySettings, rig.Script((true, TimeSpan.Zero)));

        Assert.Equal(GenerationStatus.Busy, second.Status);
        Assert.Equal(GenerationStatus.Succeeded, summary.Status);
        release.SetResult();
        Assert.Equal(GenerationStatus.Succeeded, (await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
        // The busy plan cost nothing: one plan row (the first) and one summary row.
        Assert.Equal(2, rig.Store.Rows.Count);
    }

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, false)]
    [InlineData(2, -1, false)]
    [InlineData(2, 0, true)] // plans off on this replica
    [InlineData(1, 1, true)] // one slot (local/test): nothing to keep free, plans and summaries share it
    [InlineData(0, 1, true)] // no slots: everything is busy anyway
    public void The_plan_slot_count_leaves_a_slot_for_summaries(int slots, int planSlots, bool valid)
    {
        var options = new AiOptions { MaxConcurrentGenerations = slots };
        options.Plan.MaxConcurrentGenerations = planSlots;

        var result = new AiOptionsValidator(new TestEnvironment()).Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.Contains(result.Failures!, f => f.Contains("Ai:Plan:MaxConcurrentGenerations", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_new_plan_capacity_defaults_keep_room_for_summaries()
    {
        var options = new AiOptions();

        Assert.Equal((2, 1, 60, 20), (options.MaxConcurrentGenerations, options.Plan.MaxConcurrentGenerations, options.Quotas.GuestGlobalPer24h, options.Plan.GuestGlobalPer24h));
        options.Plan.GuestGlobalPer24h = -1;
        var result = new AiOptionsValidator(new TestEnvironment()).Validate(null, options);
        Assert.Contains(result.Failures!, f => f.Contains("Ai:Plan:GuestGlobalPer24h", StringComparison.Ordinal));
    }

    /// <summary>Review L2: guest plans have their own weighted sub-cap inside the guest budget; summaries and signed-in users are not affected.</summary>
    [Fact]
    public async Task Guest_plans_stop_at_their_sub_cap_and_leave_the_guest_summary_budget()
    {
        var rig = new Rig(o =>
        {
            o.Quotas.GuestGlobalPer24h = 60;
            o.Plan.GuestGlobalPer24h = 20;
            o.Plan.GlobalAttemptWeight = 3;
        });
        // Seven guest plan attempts by other guests: 21 weighted units, over the plan sub-cap of 20 but far below the guest budget of 60.
        for (var i = 0; i < 3; i++)
        {
            await rig.Seed(AiFeature.Plan, attempts: 2, guest: true, user: Guid.NewGuid());
        }

        await rig.Seed(AiFeature.Plan, attempts: 1, guest: true, user: Guid.NewGuid());
        // A signed-in user's plan attempts do not count in the guest plan sub-cap.
        await rig.Seed(AiFeature.Plan, attempts: 2, guest: false, user: Guid.NewGuid());

        var usage = await rig.Store.GetGlobalUsageAsync(rig.Clock.GetUtcNow().AddDays(-1), 3, TestContext.Current.CancellationToken);
        Assert.Equal((27, 21, 21), (usage.Attempts, usage.GuestAttempts, usage.GuestPlanAttempts));

        var guestPlan = await rig.Ledger.GetStatusAsync(AiFeature.Plan, User, true, TestContext.Current.CancellationToken);
        Assert.Equal((QuotaBlock.Global, 20), (guestPlan.Block, guestPlan.BlockLimit));
        Assert.Equal(GenerationStatus.QuotaExceeded, (await rig.Run(AiFeature.Plan, Rig.PlanSettings(), rig.Script((true, TimeSpan.Zero)), guest: true)).Status);

        Assert.Equal(QuotaBlock.None, (await rig.Ledger.GetStatusAsync(AiFeature.Summary, User, true, TestContext.Current.CancellationToken)).Block);
        Assert.Equal(QuotaBlock.None, (await rig.Ledger.GetStatusAsync(AiFeature.Plan, User, false, TestContext.Current.CancellationToken)).Block);
        Assert.Equal(GenerationStatus.Succeeded, (await rig.Run(AiFeature.Summary, Rig.SummarySettings, rig.Script((true, TimeSpan.Zero)), guest: true)).Status);
    }

    [Theory]
    [InlineData(19, QuotaBlock.None)]
    [InlineData(20, QuotaBlock.Global)]
    public async Task The_guest_plan_sub_cap_blocks_at_its_limit(int seededUnits, QuotaBlock expected)
    {
        var rig = new Rig(o => o.Plan.GlobalAttemptWeight = 1);
        await rig.Seed(AiFeature.Plan, attempts: seededUnits, guest: true, user: Guid.NewGuid());

        Assert.Equal(expected, (await rig.Ledger.GetStatusAsync(AiFeature.Plan, User, true, TestContext.Current.CancellationToken)).Block);
    }

    private sealed class TestEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
