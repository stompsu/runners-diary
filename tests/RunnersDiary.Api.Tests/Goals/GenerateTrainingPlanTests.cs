using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Api.Tests.Insights;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Ai;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>
/// AC5: <c>POST /api/goals/{goalId}/plan</c> with a scripted model (the rule-based fake's plans, no network): the stored set, regenerate,
/// <c>replaceOtherGoals</c>, the order of checks and every problem type of plan §4.3, per-feature quotas and the ledger feature.
/// Ledger rows written here are removed afterwards so the summary tests' global budgets on the shared database are not crowded by plan weights.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GenerateTrainingPlanTests(ContainersApiFactory factory) : IAsyncLifetime
{
    private static readonly Dictionary<string, string> HighGlobal = new()
    {
        ["Ai:Quotas:GlobalPer24h"] = "100000",
        ["Ai:Quotas:GuestGlobalPer24h"] = "100000",
    };

    private readonly List<TestActor> _actors = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        var ids = _actors.Where(a => a.UserId is not null).Select(a => a.UserId!.Value).ToList();
        if (ids.Count > 0)
        {
            await factory.WithDbAsync(db => db.AiGenerations.Where(g => ids.Contains(g.UserId)).ExecuteDeleteAsync(CancellationToken.None));
        }
    }

    private WebApplicationFactory<Program> Host(ScriptedChatClient? client, Dictionary<string, string>? settings = null)
    {
        var all = new Dictionary<string, string>(HighGlobal);
        foreach (var (key, value) in settings ?? [])
        {
            all[key] = value;
        }

        return AiTestHost.Create(factory, client, all);
    }

    private async Task<TestActor> UserAsync(WebApplicationFactory<Program> host, string name, int runs = 4)
    {
        var actor = host.EntraUser(name + "-" + Guid.NewGuid().ToString("N")[..6]);
        await actor.GetUserIdAsync();
        _actors.Add(actor);
        await SeedHistoryAsync(actor, runs);
        return actor;
    }

    private async Task<TestActor> GuestAsync(WebApplicationFactory<Program> host, string name, int runs = 4)
    {
        var actor = await host.GuestAsync(name + "-" + Guid.NewGuid().ToString("N")[..6]);
        _actors.Add(actor);
        await SeedHistoryAsync(actor, runs);
        return actor;
    }

    /// <summary>Completed easy runs in the last weeks (the 422 rule needs 3 in 84 days).</summary>
    private static async Task SeedHistoryAsync(TestActor actor, int runs)
    {
        for (var i = 0; i < runs; i++)
        {
            var response = await actor.Client.PostAsJsonAsync(U("/api/runs"), Json.Run(Ahead(-3 - (4 * i)), r => r["title"] = "Secret title " + i), Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    private static Task<HttpResponseMessage> GenerateAsync(TestActor actor, Guid goalId, object? body = null, CancellationToken? ct = null) =>
        body is null
            ? actor.Client.PostAsync(U($"/api/goals/{goalId}/plan"), JsonContent.Create(new { }), ct ?? Ct)
            : actor.Client.PostAsJsonAsync(U($"/api/goals/{goalId}/plan"), body, ct ?? Ct);

    private static async Task<JsonElement> GenerateOkAsync(TestActor actor, Guid goalId, object? body = null)
    {
        var response = await GenerateAsync(actor, goalId, body);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        AssertNoStore(response);
        return await response.ReadAsync();
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static List<JsonElement> Suggestions(JsonElement plan) => [.. plan.GetProperty("suggestions").EnumerateArray()];

    private Task<List<AiGeneration>> LedgerAsync(TestActor actor) => factory.LedgerAsync(actor.UserId!.Value);

    [Fact]
    public async Task A_plan_for_an_over_ambitious_target_stores_the_prediction_and_reports_what_it_trains_toward()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid(9_000, 3_500);
        using var host = Host(client);
        var user = host.EntraUser("plan-ambitious-" + Guid.NewGuid().ToString("N")[..6]);
        await user.GetUserIdAsync();
        _actors.Add(user);
        for (var i = 0; i < 10; i++)
        {
            var run = await user.Client.PostAsJsonAsync(
                U("/api/runs"), Json.Run(Ahead(-2 - (3 * i)), r => { r["distanceMeters"] = 8_000; r["durationSeconds"] = 2_880; r["rpe"] = 3; }), Ct);
            Assert.Equal(HttpStatusCode.Created, run.StatusCode);
        }

        var goal = await user.CreateGoalAsync(g =>
        {
            g["type"] = "Performance";
            g["distanceMeters"] = 21_097.5;
            g["targetDurationSeconds"] = 6_000;
            g["targetDate"] = Ahead(84);
        });

        var body = await GenerateOkAsync(user, Id(goal));

        var plan = body.GetProperty("goal").GetProperty("plan");
        Assert.True(plan.GetProperty("ambitiousTarget").GetBoolean());
        Assert.Equal(7_245, plan.GetProperty("trainsTowardSeconds").GetInt32()); // the fallback prediction of 10 x 8 km at 6:00/km
    }

    [Fact]
    public async Task Generate_stores_a_valid_plan_returns_it_and_records_a_plan_ledger_row()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid(9_000, 3_500);
        using var host = Host(client);
        var user = await UserAsync(host, "plan-ok");
        var goal = await user.CreateGoalAsync(g => g["notes"] = "Secret goal note");

        var body = await GenerateOkAsync(user, Id(goal));

        var plan = body.GetProperty("goal").GetProperty("plan");
        Assert.Equal(("Model", 3, "scripted-model", false, false), (plan.GetProperty("source").GetString(), plan.GetProperty("promptVersion").GetInt32(),
            plan.GetProperty("model").GetString(), plan.GetProperty("outdated").GetBoolean(), plan.GetProperty("horizonCapped").GetBoolean()));
        Assert.Equal(Ahead(59), plan.GetProperty("horizonEnd").GetString());
        Assert.Equal(RuleBasedNotes, plan.GetProperty("notes").GetString());
        var suggestions = Suggestions(body);
        Assert.NotEmpty(suggestions);
        Assert.Equal(suggestions.Count, plan.GetProperty("pendingCount").GetInt32());
        Assert.All(suggestions, s =>
        {
            Assert.Equal(plan.GetProperty("generationId").GetGuid(), s.GetProperty("generationId").GetGuid());
            Assert.Equal("Pending", s.GetProperty("status").GetString());
            Assert.InRange(string.CompareOrdinal(s.GetProperty("date").GetString(), Ahead(1)), 0, int.MaxValue);
            Assert.True(string.CompareOrdinal(s.GetProperty("date").GetString(), Ahead(60)) < 0);
        });
        Assert.Equal(suggestions.Select(s => s.GetProperty("date").GetString()).Order(StringComparer.Ordinal), suggestions.Select(s => s.GetProperty("date").GetString()));
        Assert.Equal(0, body.GetProperty("droppedCount").GetInt32());
        Assert.Equal(0, body.GetProperty("replacedGoalIds").GetArrayLength());

        // Readable through the suggestions range endpoint and the goal.
        var listed = await user.ListSuggestionsAsync(0, 60);
        Assert.Equal(suggestions.Select(Id), listed.GetProperty("items").EnumerateArray().Select(Id));
        Assert.Equal(suggestions.Count, (await user.GetGoalJsonAsync(Id(goal))).GetProperty("plan").GetProperty("pendingCount").GetInt32());

        var row = Assert.Single(await LedgerAsync(user));
        Assert.Equal((AiFeature.Plan, AiGenerationOutcome.Succeeded, (byte)1, 9_000, 3_500, (short)3), (row.Feature, row.Outcome, row.Attempts, row.InputTokens, row.OutputTokens, row.PromptVersion));

        // Metrics only reach the model: no goal title or note, no run title.
        var sent = ScriptedChatClient.AllText(Assert.Single(client.Requests));
        Assert.Contains("Plan fact sheet:", sent, StringComparison.Ordinal);
        foreach (var secret in new[] { "Autumn 10K", "Secret goal note", "Secret title", "Morning run", "Europe/Warsaw" })
        {
            Assert.DoesNotContain(secret, sent, StringComparison.Ordinal);
        }
    }

    private const string RuleBasedNotes =
        "A gradual plan toward your goal: easy running, a weekly long run and one quality session in the build and peak weeks, then a lighter taper.";

    [Fact]
    public async Task The_body_is_optional_and_unknown_members_are_a_400()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-body");
        var goal = await user.CreateGoalAsync();

        var unknown = await GenerateAsync(user, Id(goal), new { replaceOtherGoals = true, userId = Guid.NewGuid() });
        await unknown.ProblemAsync(HttpStatusCode.BadRequest);
        Assert.Equal(0, client.CallCount);

        var empty = await user.Client.PostAsync(U($"/api/goals/{Id(goal)}/plan"), content: null, Ct);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
    }

    [Fact]
    public async Task Lang_pl_stores_and_returns_the_plan_language_and_the_texts_are_Polish()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-pl");
        var goal = await user.CreateGoalAsync();

        var body = await GenerateOkAsync(user, Id(goal), new { lang = "pl" });

        Assert.Equal("pl", body.GetProperty("goal").GetProperty("plan").GetProperty("language").GetString());
        Assert.NotEqual(RuleBasedNotes, body.GetProperty("goal").GetProperty("plan").GetProperty("notes").GetString());
        Assert.Contains("\"language\":\"pl\"", ScriptedChatClient.AllText(Assert.Single(client.Requests)).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal("pl", (await user.GetGoalJsonAsync(Id(goal))).GetProperty("plan").GetProperty("language").GetString());

        // A regeneration without lang is English again (the default), and the stored language follows.
        var again = await GenerateOkAsync(user, Id(goal));
        Assert.Equal("en", again.GetProperty("goal").GetProperty("plan").GetProperty("language").GetString());
        Assert.Equal(RuleBasedNotes, again.GetProperty("goal").GetProperty("plan").GetProperty("notes").GetString());
    }

    [Fact]
    public async Task Without_lang_the_plan_language_is_en()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-en");
        var goal = await user.CreateGoalAsync();

        var body = await GenerateOkAsync(user, Id(goal));

        Assert.Equal("en", body.GetProperty("goal").GetProperty("plan").GetProperty("language").GetString());
    }

    [Theory]
    [InlineData("de")]
    [InlineData("")]
    [InlineData("PL")]
    public async Task An_unsupported_lang_is_a_400_with_the_lang_key_and_no_model_call(string lang)
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-badlang");
        var goal = await user.CreateGoalAsync();

        var response = await GenerateAsync(user, Id(goal), new { lang });

        var problem = await response.ProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("lang", out _), problem.ToString());
        Assert.Equal(0, client.CallCount);
        Assert.Empty(await LedgerAsync(user));
    }

    [Fact]
    public async Task Regenerate_replaces_pending_and_dismissed_suggestions_and_never_touches_runs()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-regen");
        var goal = await user.CreateGoalAsync();
        var first = Suggestions(await GenerateOkAsync(user, Id(goal)));

        var accepted = await (await user.AcceptAsync(Id(first[0]))).ReadAsync();
        var dismissed = first[1];
        Assert.Equal(HttpStatusCode.OK, (await user.DismissAsync(Id(dismissed))).StatusCode);

        var second = await GenerateOkAsync(user, Id(goal));

        var newIds = Suggestions(second).Select(Id).ToHashSet();
        var all = (await user.ListSuggestionsAsync(0, 60, includeDismissed: true)).GetProperty("items").EnumerateArray().ToList();
        Assert.All(all, s => Assert.Contains(Id(s), newIds));
        Assert.DoesNotContain(all, s => Id(s) == Id(dismissed));

        // The accepted run is still there, linked to the goal, and its day got no new suggestion.
        var run = await (await user.Client.GetAsync(U($"/api/runs/{Id(accepted)}"), Ct)).ReadAsync();
        Assert.Equal(Id(goal), run.GetProperty("goalId").GetGuid());
        Assert.DoesNotContain(all, s => s.GetProperty("date").GetString() == accepted.GetProperty("date").GetString());
        var plan = second.GetProperty("goal").GetProperty("plan");
        Assert.Equal((1, 0), (plan.GetProperty("acceptedCount").GetInt32(), plan.GetProperty("dismissedCount").GetInt32()));

        // The second request told the model about the accepted run (fromThisGoal) and the declined date.
        var sheet = ScriptedChatClient.AllText(client.Requests[1]);
        Assert.Contains("\"fromThisGoal\":true", sheet, StringComparison.Ordinal);
        Assert.Contains($"\"dismissed\":[{{\"date\":\"{dismissed.GetProperty("date").GetString()}\"", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_goal_with_pending_suggestions_is_409_until_replace_other_goals_is_true()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-other");
        var a = await user.CreateGoalAsync(g => g["title"] = "Goal A");
        var b = await user.CreateGoalAsync(g =>
        {
            g["title"] = "Goal B";
            g["targetDate"] = Ahead(70);
        });
        await GenerateOkAsync(user, Id(a));

        var conflict = await (await GenerateAsync(user, Id(b))).ProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal("urn:runnersdiary:problem:plan-other-goal-active", conflict.ProblemType());
        Assert.Equal(Id(a), conflict.GetProperty("goalId").GetGuid());
        Assert.Equal(1, client.CallCount);
        Assert.Single(await LedgerAsync(user));

        var replaced = await GenerateOkAsync(user, Id(b), new { replaceOtherGoals = true });

        Assert.Equal([Id(a)], replaced.GetProperty("replacedGoalIds").EnumerateArray().Select(e => e.GetGuid()));
        var all = (await user.ListSuggestionsAsync(0, 90, includeDismissed: true)).GetProperty("items").EnumerateArray().ToList();
        Assert.All(all, s => Assert.Equal(Id(b), s.GetProperty("goalId").GetGuid()));
        var replacedPlan = (await user.GetGoalJsonAsync(Id(a))).GetProperty("plan");
        Assert.Equal(0, replacedPlan.GetProperty("pendingCount").GetInt32());
        Assert.True(replacedPlan.GetProperty("outdated").GetBoolean());
        // Goal A's target date is a commitment of goal B's plan: no suggestion on it.
        Assert.DoesNotContain(all, s => s.GetProperty("date").GetString() == a.GetProperty("targetDate").GetString());
    }

    [Fact]
    public async Task Fewer_than_three_recent_runs_is_422_insufficient_history_without_a_model_call_or_quota()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-422", runs: 2);
        var goal = await user.CreateGoalAsync();

        var problem = await (await GenerateAsync(user, Id(goal))).ProblemAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("urn:runnersdiary:problem:insufficient-history", problem.ProblemType());
        Assert.Equal(0, client.CallCount);
        Assert.Empty(await LedgerAsync(user));
    }

    [Fact]
    public async Task A_goal_that_is_not_active_or_too_close_is_400_on_goal_id()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-400");
        var archived = await user.CreateGoalAsync();
        Assert.Equal(HttpStatusCode.OK, (await user.PutGoalAsync(archived, g => g["status"] = "Archived")).StatusCode);
        var close = await user.CreateGoalAsync(g => g["title"] = "Close");
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE Goals SET TargetDate = {GoalTestSupport.Today.AddDays(1)} WHERE Id = {Id(close)}", Ct));

        foreach (var goalId in new[] { Id(archived), Id(close) })
        {
            var problem = await (await GenerateAsync(user, goalId)).ProblemAsync(HttpStatusCode.BadRequest);
            Assert.True(problem.GetProperty("errors").TryGetProperty("goalId", out _), problem.ToString());
        }

        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task An_unknown_or_foreign_goal_is_404_and_never_reaches_the_model()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var host = Host(client);
        var owner = await UserAsync(host, "plan-owner");
        var goal = await owner.CreateGoalAsync();
        var intruder = await GuestAsync(host, "plan-intruder");

        await (await GenerateAsync(intruder, Id(goal), new { replaceOtherGoals = true })).ProblemAsync(HttpStatusCode.NotFound);
        await (await GenerateAsync(owner, Guid.NewGuid())).ProblemAsync(HttpStatusCode.NotFound);

        Assert.Equal(0, client.CallCount);
        Assert.Equal(JsonValueKind.Null, (await owner.GetGoalJsonAsync(Id(goal))).GetProperty("plan").ValueKind);
    }

    [Fact]
    public async Task No_model_or_the_plan_switch_off_is_503_feature_unavailable_after_the_goal_checks()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var switchedOff = Host(client, new() { ["Ai:Plan:Enabled"] = "false" });
        using var noModel = Host(null);

        foreach (var host in new[] { switchedOff, noModel })
        {
            var user = await UserAsync(host, "plan-503", runs: 0);
            var goal = await user.CreateGoalAsync();
            var problem = await (await GenerateAsync(user, Id(goal))).ProblemAsync(HttpStatusCode.ServiceUnavailable);
            Assert.Equal("urn:runnersdiary:problem:feature-unavailable", problem.ProblemType());
            // 404 still comes first.
            await (await GenerateAsync(user, Guid.NewGuid())).ProblemAsync(HttpStatusCode.NotFound);
        }

        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Plan_quotas_are_per_feature_with_reset_time_for_users_and_per_account_for_guests()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid().ReplyValid();
        using var host = Host(client, new() { ["Ai:Plan:Quotas:UserPer24h"] = "1", ["Ai:Quotas:UserPer24h"] = "1" });
        var user = await UserAsync(host, "plan-quota");
        var goal = await user.CreateGoalAsync();
        await GenerateOkAsync(user, Id(goal));

        var response = await GenerateAsync(user, Id(goal));
        var problem = await response.ProblemAsync(HttpStatusCode.TooManyRequests);
        Assert.Equal("urn:runnersdiary:problem:ai-quota", problem.ProblemType());
        Assert.Equal(("account", 1), (problem.GetProperty("scope").GetString(), problem.GetProperty("limit").GetInt32()));
        Assert.True(problem.GetProperty("resetsAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddHours(23));
        Assert.InRange(response.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 3600, 86400 + 5);

        // The summary quota (also 1 here) is untouched by the plan.
        using (var scope = host.Services.CreateScope())
        {
            var summaryQuota = await scope.ServiceProvider.GetRequiredService<AiUsageLedger>().GetStatusAsync(user.UserId!.Value, false, Ct);
            Assert.Equal((0, QuotaBlock.None), (summaryQuota.Used, summaryQuota.Block));
        }

        // Guests: two plans per account.
        var guest = await GuestAsync(host, "plan-guest-quota");
        var guestGoal = await guest.CreateGoalAsync();
        await GenerateOkAsync(guest, Id(guestGoal));
        await GenerateOkAsync(guest, Id(guestGoal));
        var guestProblem = await (await GenerateAsync(guest, Id(guestGoal))).ProblemAsync(HttpStatusCode.TooManyRequests);
        Assert.Equal(("account", 2, JsonValueKind.Null), (guestProblem.GetProperty("scope").GetString(), guestProblem.GetProperty("limit").GetInt32(), guestProblem.GetProperty("resetsAt").ValueKind));
        Assert.All(await LedgerAsync(guest), r => Assert.Equal(AiFeature.Plan, r.Feature));
    }

    [Fact]
    public async Task A_plan_attempt_weighs_three_in_the_global_budget()
    {
        factory.SkipIfDockerUnavailable();
        var (total, _) = await factory.GlobalLedgerAsync();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = Host(client, new() { ["Ai:Quotas:GlobalPer24h"] = (total + 3).ToString(CultureInfo.InvariantCulture) });
        var first = await UserAsync(host, "plan-global-1");
        var second = await UserAsync(host, "plan-global-2");
        await GenerateOkAsync(first, Id(await first.CreateGoalAsync()));

        var problem = await (await GenerateAsync(second, Id(await second.CreateGoalAsync()))).ProblemAsync(HttpStatusCode.TooManyRequests);

        Assert.Equal(("global", total + 3, JsonValueKind.Null), (problem.GetProperty("scope").GetString(), problem.GetProperty("limit").GetInt32(), problem.GetProperty("resetsAt").ValueKind));
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task A_second_generation_while_one_runs_is_409_plan_in_progress_and_a_summary_is_not_blocked()
    {
        factory.SkipIfDockerUnavailable();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ScriptedChatClient().ReplyValidAfter(release.Task);
        using var host = Host(client);
        var user = await UserAsync(host, "plan-inflight");
        var goal = await user.CreateGoalAsync();

        var started = client.RequestStarted.Task;
        var first = GenerateAsync(user, Id(goal));
        await started.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        var problem = await (await GenerateAsync(user, Id(goal))).ProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal("urn:runnersdiary:problem:plan-in-progress", problem.ProblemType());

        release.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await first.WaitAsync(TimeSpan.FromSeconds(30), Ct)).StatusCode);
        Assert.Single(await LedgerAsync(user));
    }

    [Fact]
    public async Task Invalid_output_twice_is_502_plan_failed_with_nothing_stored_and_a_retry_with_codes_only()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyText("{\"notes\":\"<b>bad</b>\",\"sessions\":[]}").ReplyText("not json");
        using var host = Host(client);
        var user = await UserAsync(host, "plan-502");
        var goal = await user.CreateGoalAsync();

        var problem = await (await GenerateAsync(user, Id(goal))).ProblemAsync(HttpStatusCode.BadGateway);

        Assert.Equal("urn:runnersdiary:problem:plan-failed", problem.ProblemType());
        Assert.Equal("invalidOutput", problem.GetProperty("reason").GetString());
        Assert.Equal(2, client.CallCount);
        var retry = client.Requests[1].Messages[^1].Text;
        // An empty plan also leaves full training weeks empty.
        Assert.Contains("Violated rules: schema, emptyWeek, markup, empty", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", retry, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, (await user.GetGoalJsonAsync(Id(goal))).GetProperty("plan").ValueKind);
        Assert.Empty((await user.ListSuggestionsAsync(0, 60, includeDismissed: true)).GetProperty("items").EnumerateArray());
        var row = Assert.Single(await LedgerAsync(user));
        Assert.Equal((AiFeature.Plan, AiGenerationOutcome.InvalidOutput, (byte)2), (row.Feature, row.Outcome, row.Attempts));
    }

    [Fact]
    public async Task A_hanging_model_is_502_timeout_and_a_provider_error_is_502_provider()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().Hang().Throw(new InvalidOperationException("boom"));
        using var host = Host(client, new() { ["Ai:Plan:AttemptTimeout"] = "00:00:01", ["Ai:Plan:TotalTimeout"] = "00:00:02" });
        var user = await UserAsync(host, "plan-timeout");
        var goal = await user.CreateGoalAsync();

        var timeout = await (await GenerateAsync(user, Id(goal))).ProblemAsync(HttpStatusCode.BadGateway);
        var provider = await (await GenerateAsync(user, Id(goal))).ProblemAsync(HttpStatusCode.BadGateway);

        Assert.Equal(("timeout", "provider"), (timeout.GetProperty("reason").GetString(), provider.GetProperty("reason").GetString()));
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task Sessions_on_days_with_a_planned_run_are_dropped_and_counted()
    {
        factory.SkipIfDockerUnavailable();
        string? blocked = null;
        var client = new ScriptedChatClient().ReplyValidTransformed(json =>
        {
            // The "model" also suggests a session on the day of the planned run.
            var draft = JsonNode.Parse(json)!.AsObject();
            var sessions = draft["sessions"]!.AsArray();
            var copy = sessions[0]!.DeepClone().AsObject();
            copy["date"] = blocked;
            sessions.Add(copy);
            return draft.ToJsonString();
        });
        using var host = Host(client);
        var user = await UserAsync(host, "plan-drop");
        var goal = await user.CreateGoalAsync();
        blocked = Ahead(9);
        var planned = await user.Client.PostAsJsonAsync(U("/api/runs"), Json.Run(blocked, r =>
        {
            r["status"] = "Planned";
            r["durationSeconds"] = null;
            r["rpe"] = null;
        }), Ct);
        Assert.Equal(HttpStatusCode.Created, planned.StatusCode);

        var body = await GenerateOkAsync(user, Id(goal));

        Assert.Equal(1, body.GetProperty("droppedCount").GetInt32());
        Assert.DoesNotContain(Suggestions(body), s => s.GetProperty("date").GetString() == blocked);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task A_goal_changed_while_the_model_works_is_409_concurrency_conflict_and_nothing_is_stored()
    {
        factory.SkipIfDockerUnavailable();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ScriptedChatClient().ReplyValidAfter(release.Task);
        using var host = Host(client);
        var user = await UserAsync(host, "plan-409");
        var goal = await user.CreateGoalAsync();

        var started = client.RequestStarted.Task;
        var generation = GenerateAsync(user, Id(goal));
        await started.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal(HttpStatusCode.OK, (await user.PutGoalAsync(goal, g => g["title"] = "Renamed")).StatusCode);
        release.SetResult();

        var problem = await (await generation.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ProblemAsync(HttpStatusCode.Conflict);

        Assert.Equal("urn:runnersdiary:problem:concurrency-conflict", problem.ProblemType());
        Assert.Equal(JsonValueKind.Null, (await user.GetGoalJsonAsync(Id(goal))).GetProperty("plan").ValueKind);
        Assert.Empty((await user.ListSuggestionsAsync(0, 60, includeDismissed: true)).GetProperty("items").EnumerateArray());
        // The model's work was paid for: the quota counts it.
        Assert.Equal(AiGenerationOutcome.Succeeded, Assert.Single(await LedgerAsync(user)).Outcome);
    }

    [Fact]
    public async Task A_new_plan_clears_the_outdated_flag()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = Host(client);
        var user = await UserAsync(host, "plan-outdated");
        var goal = (await GenerateOkAsync(user, Id(await user.CreateGoalAsync()))).GetProperty("goal");

        var changed = await (await user.PutGoalAsync(goal, g => g["distanceMeters"] = 12_000)).ReadAsync();
        Assert.True(changed.GetProperty("plan").GetProperty("outdated").GetBoolean());

        var regenerated = await GenerateOkAsync(user, Id(goal));
        Assert.False(regenerated.GetProperty("goal").GetProperty("plan").GetProperty("outdated").GetBoolean());
    }
}
