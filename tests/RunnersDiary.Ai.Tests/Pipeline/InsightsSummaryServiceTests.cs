using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Ai;
using static RunnersDiary.Ai.Tests.Support.Training;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>Read precedence, explicit generation and validity (plan §3.1–3.3, ADR 0028) without a database.</summary>
public sealed class InsightsSummaryServiceTests
{
    private static readonly SummaryKey March = new(D("2026-03-01"), D("2026-03-31"), ComparisonKind.Previous, "en");
    private static string Good => EvalFiles.GoodDraft(EvalCases.LoadSpike);

    private sealed class Scenario
    {
        public Scenario(bool guest = false, IReadOnlyList<RunFact>? facts = null, IReadOnlyList<RaceFact>? races = null, DateOnly? today = null, Action<AiOptions>? configure = null, string deployment = "gpt-test")
        {
            Facts = [.. facts ?? SpikeLog().Facts];
            Races = [.. races ?? []];
            Today = today ?? D("2026-04-15");
            Scripted = new ScriptedChatClient();
            Harness = new AiHarness(Scripted, configure, deployment);
            User = new TestCurrentUser(Guid.NewGuid(), guest);
        }

        public List<RunFact> Facts { get; }

        public List<RaceFact> Races { get; }

        public DateOnly Today { get; set; }

        public ScriptedChatClient Scripted { get; }

        public AiHarness Harness { get; }

        public TestCurrentUser User { get; }

        public InMemoryAiSummaryStore Store { get; init; } = new();

        public InsightsSummaryService Service => Harness.SummaryService(User, new TestReportService(() => Facts, () => Races, () => Today), Store);

        public Task<SummaryView> Get(SummaryKey key) => Service.GetAsync(key, TestContext.Current.CancellationToken);

        public Task<GenerateOutcome> Post(SummaryKey key, bool regenerate) => Service.GenerateAsync(key, regenerate, TestContext.Current.CancellationToken);
    }

    private static Training SpikeLog() => new Training().Weeks(D("2026-01-05"), 10).Week(D("2026-03-16"), 1.8).Weeks(D("2026-03-23"), 2);

    [Fact]
    public async Task Nothing_stored_is_none_with_availability()
    {
        var s = new Scenario();
        var view = await s.Get(March);

        Assert.Equal(SummaryState.None, view.State);
        Assert.Null(view.Summary);
        Assert.Equal(new GenerationInfo(GenerationAvailability.Available, 0, 10, null, QuotaWindow.Rolling24h), view.Generation);
    }

    /// <summary>L8: the kill switch is read per request: reads report notConfigured and Generate is unavailable at once, and both recover.</summary>
    [Fact]
    public async Task The_kill_switch_flips_availability_and_generation_at_runtime()
    {
        var s = new Scenario();
        s.Scripted.Reply(Good);

        s.Harness.Options.Enabled = false;
        Assert.Equal(GenerationAvailability.NotConfigured, (await s.Get(March)).Generation.Availability);
        Assert.Equal(GenerateStatus.Unavailable, (await s.Post(March, regenerate: false)).Status);

        s.Harness.Options.Enabled = true;
        Assert.Equal(GenerationAvailability.Available, (await s.Get(March)).Generation.Availability);
        Assert.Equal(GenerateStatus.Ok, (await s.Post(March, regenerate: false)).Status);
    }

    /// <summary>M4: the provider's Retry-After reaches the endpoint outcome; nothing is stored.</summary>
    [Fact]
    public async Task A_provider_429_is_busy_with_its_retry_after()
    {
        var s = new Scenario();
        s.Scripted.Throw(ProviderErrors.Throttled(new Dictionary<string, string> { ["Retry-After"] = "20" }));

        var outcome = await s.Post(March, regenerate: false);

        Assert.Equal((GenerateStatus.Busy, TimeSpan.FromSeconds(20)), (outcome.Status, outcome.RetryAfter!.Value));
        Assert.Empty(s.Store.Rows);
    }

    [Fact]
    public async Task Generate_stores_then_get_is_fresh_and_generate_without_regenerate_reuses_it()
    {
        var s = new Scenario();
        s.Scripted.Reply(Good, 5000, 600, "gpt-5.4-mini-2026-03-17");

        var first = await s.Post(March, regenerate: false);
        Assert.Equal(GenerateStatus.Ok, first.Status);
        Assert.Equal(SummaryState.Fresh, first.View!.State);
        Assert.Equal(SummarySource.Model, first.View.Summary!.Source);
        Assert.Equal("gpt-5.4-mini-2026-03-17", first.View.Summary.Model);
        Assert.Equal(1, first.View.Summary.PromptVersion);
        Assert.Equal(1, first.View.Generation.Used);

        var stored = Assert.Single(s.Store.Rows.Values);
        Assert.Equal((1, 1, 1, "gpt-test", 5000, 600), (stored.MetricsVersion, stored.FactSheetVersion, stored.PromptVersion, stored.ModelDeployment, stored.InputTokens, stored.OutputTokens));

        var get = await s.Get(March);
        Assert.Equal(SummaryState.Fresh, get.State);
        Assert.Equal(first.View.Summary.Content.Headline.Text, get.Summary!.Content.Headline.Text);

        var reuse = await s.Post(March, regenerate: false);
        Assert.Equal(GenerateStatus.Ok, reuse.Status);
        Assert.Equal(1, s.Scripted.CallCount);
        Assert.Single(s.Harness.Generations.Rows);
    }

    [Fact]
    public async Task Regenerate_always_calls_the_model()
    {
        var s = new Scenario();
        s.Scripted.Reply(Good).Reply(Good);
        await s.Post(March, regenerate: false);
        var again = await s.Post(March, regenerate: true);

        Assert.Equal(GenerateStatus.Ok, again.Status);
        Assert.Equal(2, s.Scripted.CallCount);
        Assert.Equal(2, again.View!.Generation.Used);
    }

    [Fact]
    public async Task A_run_edit_makes_the_stored_summary_stale_with_its_stored_values()
    {
        var s = new Scenario();
        s.Scripted.Reply(Good);
        var generated = await s.Post(March, regenerate: false);
        var storedLoad = generated.View!.Summary!.Content.Values["week.4.load"].Number;

        s.Facts.Add(new RunFact(Guid.NewGuid(), D("2026-03-18"), RunStatus.Completed, 20, 5000, 1800, null, 3, null, null));
        var view = await s.Get(March);

        Assert.Equal(SummaryState.Stale, view.State);
        Assert.Equal(storedLoad, view.Summary!.Content.Values["week.4.load"].Number);
    }

    [Fact]
    public async Task A_different_deployment_or_prompt_version_is_stale()
    {
        var store = new InMemoryAiSummaryStore();
        var a = new Scenario { Store = store };
        a.Scripted.Reply(Good);
        await a.Post(March, regenerate: false);

        var otherDeployment = new Scenario(deployment: "gpt-other") { Store = store };
        Assert.Equal(SummaryState.Stale, (await otherDeployment.Get(March)).State);

        var row = store.Rows[March];
        store.Rows[March] = row with { PromptVersion = 0 };
        Assert.Equal(SummaryState.Stale, (await a.Get(March)).State);
    }

    [Fact]
    public async Task With_no_provider_a_stored_summary_stays_fresh_and_generation_is_not_configured()
    {
        var store = new InMemoryAiSummaryStore();
        var a = new Scenario { Store = store };
        a.Scripted.Reply(Good);
        await a.Post(March, regenerate: false);

        var noClient = new AiHarness(client: null);
        var service = noClient.SummaryService(a.User, new TestReportService(() => a.Facts, () => a.Races, () => a.Today), store);
        var view = await service.GetAsync(March, TestContext.Current.CancellationToken);

        Assert.Equal(SummaryState.Fresh, view.State);
        Assert.Equal(new GenerationInfo(GenerationAvailability.NotConfigured, null, null, null, null), view.Generation);
        Assert.Equal(GenerateStatus.Unavailable, (await service.GenerateAsync(March, true, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task A_partial_period_turns_stale_when_today_moves()
    {
        var s = new Scenario(facts: new Training().Weeks(D("2026-01-05"), 15).Facts, today: D("2026-04-16"));
        var april = new SummaryKey(D("2026-04-01"), D("2026-04-30"), ComparisonKind.Previous, "en");
        s.Scripted.Reply(EvalFiles.GoodDraft(EvalCases.PartialPeriod));
        Assert.Equal(GenerateStatus.Ok, (await s.Post(april, false)).Status);

        Assert.Equal(SummaryState.Fresh, (await s.Get(april)).State);
        s.Today = D("2026-04-17");
        Assert.Equal(SummaryState.Stale, (await s.Get(april)).State);
    }

    [Theory]
    [InlineData("2026-02-01", "2026-02-28", "2026-03-10", 0)]
    [InlineData("2026-06-01", "2026-06-30", "2026-05-20", 0)]
    [InlineData("2026-02-01", "2026-02-28", "2026-03-10", 2)]
    public async Task Template_periods_never_call_the_model_or_use_quota(string from, string to, string today, int sessions)
    {
        var log = new Training();
        for (var i = 0; i < sessions; i++)
        {
            log.Run(D(from).AddDays(i), "easy", 8000, 2880, 3);
        }

        var s = new Scenario(facts: log.Facts, today: D(today));
        var key = new SummaryKey(D(from), D(to), ComparisonKind.Previous, "en");

        var view = await s.Get(key);
        var post = await s.Post(key, regenerate: true);

        Assert.Equal(SummarySource.Template, view.Summary!.Source);
        Assert.Equal(GenerationAvailability.NotNeeded, view.Generation.Availability);
        Assert.Equal(GenerateStatus.Ok, post.Status);
        Assert.Equal(SummarySource.Template, post.View!.Summary!.Source);
        Assert.Equal(0, s.Scripted.CallCount);
        Assert.Empty(s.Harness.Generations.Rows);
        Assert.Empty(s.Store.Rows);
        Assert.NotEmpty(post.View.Summary.Content.Values);
    }

    [Fact]
    public async Task A_guest_gets_the_demo_fixture_on_the_default_view_with_no_ai()
    {
        var today = D("2026-11-04");
        var (facts, races) = DemoFacts.Facts(today);
        var guest = new Scenario(guest: true, facts: facts, races: races, today: today, configure: o => o.Provider = AiProvider.None);
        var (from, to, compare) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;
        var key = new SummaryKey(from, to, compare, "en");

        var view = await guest.Get(key);

        Assert.Equal(SummarySource.Demo, view.Summary!.Source);
        Assert.Equal(SummaryState.Fresh, view.State);
        Assert.Null(view.Summary.CreatedAt);
        Assert.Equal(382593, view.Summary.Content.Values["totals.distance"].Number);
        Assert.Equal(0, guest.Scripted.CallCount);

        // Another period, a user instead of a guest, or a broken claim: no fixture.
        Assert.Equal(SummaryState.None, (await guest.Get(key with { Compare = ComparisonKind.None })).State);
        var user = new Scenario(guest: false, facts: facts, races: races, today: today);
        Assert.Equal(SummaryState.None, (await user.Get(key)).State);
        guest.Facts.RemoveAll(f => f.Date == from.AddDays(6) && f.RunTypeId == 22);
        Assert.Equal(SummaryState.None, (await guest.Get(key)).State);
    }

    [Fact]
    public async Task A_fresh_stored_summary_wins_over_the_demo_fixture()
    {
        var today = D("2026-11-04");
        var (facts, races) = DemoFacts.Facts(today);
        var guest = new Scenario(guest: true, facts: facts, races: races, today: today);
        var (from, to, compare) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;
        var key = new SummaryKey(from, to, compare, "en");
        guest.Scripted.Reply(EvalFiles.GoodDraft(EvalCases.RaceWeekPeakDemo));

        Assert.Equal(GenerateStatus.Ok, (await guest.Post(key, regenerate: false)).Status);
        var view = await guest.Get(key);

        Assert.Equal(SummarySource.Model, view.Summary!.Source);
        Assert.Equal(new GenerationInfo(GenerationAvailability.Available, 1, 3, null, QuotaWindow.Account), view.Generation);
    }

    [Fact]
    public async Task Quota_exhaustion_shows_in_availability_and_blocks_generation()
    {
        var s = new Scenario(configure: o => o.Quotas.UserPer24h = 1);
        s.Scripted.Reply(Good);
        await s.Post(March, regenerate: false);

        var view = await s.Get(March);
        Assert.Equal(GenerationAvailability.QuotaExhausted, view.Generation.Availability);
        Assert.NotNull(view.Generation.ResetsAt);

        var blocked = await s.Post(March, regenerate: true);
        Assert.Equal(GenerateStatus.QuotaExceeded, blocked.Status);
        Assert.Equal(QuotaBlock.Account, blocked.Quota!.Block);
        Assert.Equal(1, s.Scripted.CallCount);
    }

    [Fact]
    public async Task A_failed_generation_stores_nothing()
    {
        var s = new Scenario();
        s.Scripted.Reply("nope").Reply("still nope");
        var outcome = await s.Post(March, regenerate: false);

        Assert.Equal((GenerateStatus.Failed, GenerationFailure.InvalidOutput), (outcome.Status, outcome.Failure!.Value));
        Assert.Empty(s.Store.Rows);
        Assert.Single(s.Harness.Generations.Rows);
    }
}
