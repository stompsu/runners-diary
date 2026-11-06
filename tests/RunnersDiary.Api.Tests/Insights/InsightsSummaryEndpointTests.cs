using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Time;
using static RunnersDiary.Api.Tests.Insights.SummaryTestSupport;

namespace RunnersDiary.Api.Tests.Insights;

/// <summary>GET/POST /api/insights/summary (M6 plan section 3, AC6 to AC10): contract, states, error mapping and what the model sees.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class InsightsSummaryEndpointTests(ContainersApiFactory factory)
{
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- validation ------------------------------------------------------------------------------------------------

    public static TheoryData<string, string> BadQueries() => new()
    {
        { "", "from" },
        { "?to=2025-03-16", "from" },
        { "?from=2025-03-03", "to" },
        { "?from=2025-13-01&to=2025-03-16", "from" },
        { "?from=2025-03-10&to=2025-03-01", "to" },
        { "?from=2025-01-01&to=2026-01-02", "to" },
        { "?from=1949-12-31&to=1950-01-05", "from" },
        { "?from=2025-03-03&to=2025-03-16&compare=bogus", "compare" },
        { "?from=2025-03-03&to=2025-03-16&compare=Previous", "compare" },
        { "?from=2025-03-03&to=2025-03-16&lang=pl", "lang" },
        { "?from=2025-03-03&to=2025-03-16&lang=", "lang" },
        { "?from=2025-03-03&to=2025-03-16&lang=EN", "lang" },
    };

    [Theory]
    [MemberData(nameof(BadQueries))]
    public async Task Get_with_invalid_input_returns_a_400_problem_with_the_camel_case_key(string query, string key)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("sumbadget");

        var response = await user.Client.GetAsync(Url(query), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.ReadAsync()).GetProperty("errors").TryGetProperty(key, out _));
    }

    public static TheoryData<string, string> BadBodies() => new()
    {
        { """{}""", "from" },
        { """{"from":"2025-03-03"}""", "to" },
        { """{"from":"2025-03-10","to":"2025-03-01"}""", "to" },
        { """{"from":"2025-01-01","to":"2026-01-02"}""", "to" },
        { """{"from":"2025-03-03","to":"2025-03-16","compare":"Previous"}""", "compare" },
        { """{"from":"2025-03-03","to":"2025-03-16","compare":"bogus"}""", "compare" },
        { """{"from":"2025-03-03","to":"2025-03-16","lang":"pl"}""", "lang" },
        { """{"from":"2025-03-03","to":"2025-03-16","regenerate":"yes"}""", "regenerate" },
        { """{"from":"2025-03-03","to":"2025-03-16","userId":"00000000-0000-0000-0000-000000000001"}""", "userId" },
        { """{"from":"2025-03-03","to":"2025-03-16","extra":1}""", "extra" },
        { """{"from":"not-a-date","to":"2025-03-16"}""", "from" },
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task Post_with_invalid_input_returns_a_400_problem_and_never_reaches_the_model(string json, string key)
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumbadpost");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await user.Client.PostAsync(new Uri("/api/insights/summary", UriKind.Relative), content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync();
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)), problem.ToString());
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task A_post_without_a_token_is_401_and_a_get_with_a_guest_or_user_token_is_accepted()
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();

        var get = await anonymous.GetAsync(Url(), Ct);
        var post = await anonymous.PostAsJsonAsync(new Uri("/api/insights/summary", UriKind.Relative), new { from = From, to = To }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.EntraUser("sumauthu").Client.GetAsync(Url(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await factory.GuestAsync("sumauthg")).Client.GetAsync(Url(), Ct)).StatusCode);
    }

    // ---- GET states --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_state_none_when_nothing_is_stored_and_no_model_is_configured()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("sumnone");
        await SeedRunsAsync(user);

        var response = await user.Client.GetAsync(Url(), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        var body = await response.ReadAsync();
        Assert.Equal(From, body.GetProperty("period").GetProperty("from").GetString());
        Assert.Equal("previous", body.GetProperty("period").GetProperty("compare").GetString());
        Assert.Equal("en", body.GetProperty("period").GetProperty("lang").GetString());
        Assert.Equal("none", body.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("summary").ValueKind);
        var generation = body.GetProperty("generation");
        Assert.Equal("notConfigured", generation.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, generation.GetProperty("used").ValueKind);
        Assert.Equal(JsonValueKind.Null, generation.GetProperty("limit").ValueKind);
    }

    [Fact]
    public async Task Get_with_a_model_reports_the_remaining_quota_and_no_model_call_is_made()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumavail");
        await SeedRunsAsync(user);

        var body = await GetOkAsync(user);

        Assert.Equal("none", body.GetProperty("state").GetString());
        var generation = body.GetProperty("generation");
        Assert.Equal("available", generation.GetProperty("availability").GetString());
        Assert.Equal(0, generation.GetProperty("used").GetInt32());
        Assert.Equal(10, generation.GetProperty("limit").GetInt32());
        Assert.Equal("rolling24h", generation.GetProperty("scope").GetString());
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task An_account_without_runs_gets_the_empty_template_and_a_future_period_gets_a_template_too()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumtemplate");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var empty = await GetOkAsync(user, "?from=2025-03-03&to=2025-03-16");
        var future = await GetOkAsync(user, $"?from={Iso(today.AddDays(30))}&to={Iso(today.AddDays(36))}");
        var posted = await PostOkAsync(user, new { from = "2025-03-03", to = "2025-03-16" });

        foreach (var body in new[] { empty, future, posted })
        {
            Assert.Equal("fresh", body.GetProperty("state").GetString());
            var summary = body.GetProperty("summary");
            Assert.Equal("template", summary.GetProperty("source").GetString());
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("createdAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("model").ValueKind);
            Assert.Equal("notNeeded", body.GetProperty("generation").GetProperty("availability").GetString());
            Assert.False(string.IsNullOrWhiteSpace(summary.GetProperty("headline").GetProperty("text").GetString()));
        }

        Assert.Equal(0, client.CallCount);
        Assert.Empty(await host.LedgerAsync(await user.GetUserIdAsync()));
        Assert.Empty(await host.SummariesAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_fresh_guests_default_view_returns_the_demo_summary_without_any_ai_configured_and_costs_no_quota()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("sumdemo");
        var today = UserToday.Get(TimeProvider.System, "Europe/Warsaw");
        var (from, to, compare) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;
        Assert.Equal(ComparisonKind.Previous, compare);

        var body = await GetOkAsync(guest, $"?from={Iso(from)}&to={Iso(to)}&compare=previous");

        Assert.Equal("fresh", body.GetProperty("state").GetString());
        var summary = body.GetProperty("summary");
        Assert.Equal("demo", summary.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("model").ValueKind);
        Assert.Equal("notConfigured", body.GetProperty("generation").GetProperty("availability").GetString());
        // Every cited key is resolved from the guest's own metrics.
        var values = summary.GetProperty("values");
        foreach (var key in summary.GetProperty("headline").GetProperty("keys").EnumerateArray())
        {
            Assert.True(values.TryGetProperty(key.GetString()!, out _));
        }

        Assert.Empty(await factory.LedgerAsync(guest.UserId!.Value));

        // Another range, or another comparison, is not the preset.
        var other = await GetOkAsync(guest, $"?from={Iso(from)}&to={Iso(to)}&compare=none");
        Assert.Equal("none", other.GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_demo_summary_stops_being_served_once_the_guest_breaks_a_claim_it_makes()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("sumdemobreak");
        var today = UserToday.Get(TimeProvider.System, "Europe/Warsaw");
        var (from, to, _) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;
        var query = $"?from={Iso(from)}&to={Iso(to)}&compare=previous";
        Assert.Equal("demo", (await GetOkAsync(guest, query)).GetProperty("summary").GetProperty("source").GetString());

        // Delete every run in the period: no predicate about distance, load or the spike week can hold any more.
        var list = await (await guest.Client.GetAsync(new Uri($"/api/runs?from={Iso(from)}&to={Iso(to)}", UriKind.Relative), Ct)).ReadAsync();
        foreach (var item in list.GetProperty("items").EnumerateArray())
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/runs/{item.GetProperty("id").GetGuid()}", UriKind.Relative));
            request.Headers.TryAddWithoutValidation("If-Match", item.GetProperty("version").GetString());
            Assert.True((await guest.Client.SendAsync(request, Ct)).IsSuccessStatusCode);
        }

        var after = await GetOkAsync(guest, query);
        Assert.NotEqual("demo", after.GetProperty("summary").ValueKind == JsonValueKind.Null ? "none" : after.GetProperty("summary").GetProperty("source").GetString());
    }

    // ---- POST --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_generates_stores_and_then_get_returns_the_fresh_summary_and_a_repeat_post_reuses_it_without_the_model()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid(inputTokens: 1234, outputTokens: 321);
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumok");
        await SeedRunsAsync(user);
        var userId = await user.GetUserIdAsync();

        var created = await PostOkAsync(user);

        Assert.Equal("fresh", created.GetProperty("state").GetString());
        var summary = created.GetProperty("summary");
        Assert.Equal("model", summary.GetProperty("source").GetString());
        Assert.Equal("scripted-model", summary.GetProperty("model").GetString());
        Assert.Equal(2, summary.GetProperty("promptVersion").GetInt32());
        Assert.Equal(1, summary.GetProperty("metricsVersion").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, summary.GetProperty("createdAt").ValueKind);
        Assert.Contains("{period.from}", summary.GetProperty("headline").GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(From, summary.GetProperty("values").GetProperty("period.from").GetProperty("text").GetString());
        Assert.Equal(1, created.GetProperty("generation").GetProperty("used").GetInt32());

        var row = Assert.Single(await host.SummariesAsync(userId));
        Assert.Equal(32, row.Fingerprint.Length);
        Assert.Equal((short)2, row.PromptVersion);
        Assert.Equal("gpt-test", row.ModelDeployment);
        Assert.Equal(1234, row.InputTokens);
        Assert.Equal(321, row.OutputTokens);
        var ledger = Assert.Single(await host.LedgerAsync(userId));
        Assert.Equal(AiGenerationOutcome.Succeeded, ledger.Outcome);
        Assert.Equal((byte)1, ledger.Attempts);
        Assert.Equal(1234, ledger.InputTokens);
        Assert.False(ledger.IsGuest);

        var read = await GetOkAsync(user);
        Assert.Equal("fresh", read.GetProperty("state").GetString());
        Assert.Equal(summary.GetProperty("headline").GetProperty("text").GetString(), read.GetProperty("summary").GetProperty("headline").GetProperty("text").GetString());

        var again = await PostOkAsync(user); // regenerate defaults to false: no model, no quota
        Assert.Equal("fresh", again.GetProperty("state").GetString());
        Assert.Equal(1, client.CallCount);
        Assert.Single(await host.LedgerAsync(userId));
    }

    [Fact]
    public async Task Regenerate_calls_the_model_again_replaces_the_row_and_counts_against_the_quota()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumregen");
        await SeedRunsAsync(user);
        var userId = await user.GetUserIdAsync();
        await PostOkAsync(user);

        var second = await PostOkAsync(user, new { from = From, to = To, regenerate = true });

        Assert.Equal(2, client.CallCount);
        Assert.Equal(2, second.GetProperty("generation").GetProperty("used").GetInt32());
        Assert.Single(await host.SummariesAsync(userId)); // one row per key
        Assert.Equal(2, (await host.LedgerAsync(userId)).Count);
    }

    [Fact]
    public async Task Post_without_a_configured_model_and_with_the_kill_switch_off_is_503_feature_unavailable_while_reads_still_work()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var enabled = AiTestHost.Create(factory, client);
        using var killed = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Enabled"] = "false" });
        var user = enabled.EntraUser("sumkill");
        await SeedRunsAsync(user);
        await PostOkAsync(user); // a stored summary exists
        var killedUser = killed.EntraUser("sumkill");
        // Same identity (object id), so the same account: the host switch changes only whether generation runs.
        killedUser.Client.DefaultRequestHeaders.Authorization = user.Client.DefaultRequestHeaders.Authorization;

        foreach (var actor in new[] { factory.EntraUser("sumnoprov"), killedUser })
        {
            var response = await PostAsync(actor);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("urn:runnersdiary:problem:feature-unavailable", (await response.ReadAsync()).ProblemType());
        }

        var read = await GetOkAsync(killedUser);
        Assert.Equal("fresh", read.GetProperty("state").GetString());
        Assert.Equal("model", read.GetProperty("summary").GetProperty("source").GetString());
        Assert.Equal("notConfigured", read.GetProperty("generation").GetProperty("availability").GetString());
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Invalid_output_twice_is_502_summary_failed_with_nothing_stored_and_the_retry_carries_codes_only()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyText("this is not json with the secret-looking text 123").ReplyText("{\"headline\":\"No placeholders, 12 km.\",\"wentWell\":[],\"watchOuts\":[],\"suggestion\":null,\"dataNotes\":[]}");
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumbad2");
        await SeedRunsAsync(user);
        var userId = await user.GetUserIdAsync();

        var response = await PostAsync(user);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:summary-failed", problem.ProblemType());
        Assert.Equal("invalidOutput", problem.GetProperty("reason").GetString());
        Assert.Equal(2, client.CallCount);
        var retry = ScriptedChatClient.AllText(client.Requests[1]);
        Assert.DoesNotContain("secret-looking", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("12 km", retry, StringComparison.Ordinal);
        Assert.Empty(await host.SummariesAsync(userId));
        var ledger = Assert.Single(await host.LedgerAsync(userId));
        Assert.Equal(AiGenerationOutcome.InvalidOutput, ledger.Outcome);
        Assert.Equal((byte)2, ledger.Attempts);
        Assert.Equal("none", (await GetOkAsync(user)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_valid_retry_after_an_invalid_first_attempt_succeeds_with_two_attempts_recorded()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyText("nope").ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumretry");
        await SeedRunsAsync(user);

        var body = await PostOkAsync(user);

        Assert.Equal("model", body.GetProperty("summary").GetProperty("source").GetString());
        var ledger = Assert.Single(await host.LedgerAsync(await user.GetUserIdAsync()));
        Assert.Equal((byte)2, ledger.Attempts);
        Assert.Equal(AiGenerationOutcome.Succeeded, ledger.Outcome);
    }

    [Fact]
    public async Task A_model_that_hangs_is_502_with_reason_timeout_and_a_provider_error_is_502_with_reason_provider()
    {
        factory.SkipIfDockerUnavailable();
        var hanging = new ScriptedChatClient().Hang();
        var failing = new ScriptedChatClient().Throw(new InvalidOperationException("provider says: secret detail"));
        using var timeoutHost = AiTestHost.Create(factory, hanging, new Dictionary<string, string> { ["Ai:AttemptTimeout"] = "00:00:01" });
        using var providerHost = AiTestHost.Create(factory, failing);
        var slow = timeoutHost.EntraUser("sumtimeout");
        var broken = providerHost.EntraUser("sumprovider");
        await SeedRunsAsync(slow);
        await SeedRunsAsync(broken);

        var timeout = await PostAsync(slow);
        var provider = await PostAsync(broken);

        Assert.Equal(HttpStatusCode.BadGateway, timeout.StatusCode);
        Assert.Equal("timeout", (await timeout.ReadAsync()).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.BadGateway, provider.StatusCode);
        var providerProblem = await provider.ReadAsync();
        Assert.Equal("provider", providerProblem.GetProperty("reason").GetString());
        Assert.DoesNotContain("secret detail", providerProblem.ToString(), StringComparison.Ordinal);
        Assert.Equal(AiGenerationOutcome.Timeout, Assert.Single(await timeoutHost.LedgerAsync(await slow.GetUserIdAsync())).Outcome);
        Assert.Equal(AiGenerationOutcome.ProviderError, Assert.Single(await providerHost.LedgerAsync(await broken.GetUserIdAsync())).Outcome);
        Assert.Empty(await timeoutHost.SummariesAsync(await slow.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_client_that_cancels_ends_the_model_call_records_a_cancelled_row_and_can_try_again()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().Hang().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumcancel");
        await SeedRunsAsync(user);
        var userId = await user.GetUserIdAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var started = client.RequestStarted.Task;
        var pending = PostAsync(user, null, cts.Token);
        await started.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // The server notices the abort asynchronously; the ledger row (written with no token) appears shortly after.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        List<AiGeneration> ledger;
        do
        {
            await Task.Delay(100, Ct);
            ledger = await host.LedgerAsync(userId);
        }
        while (ledger.Count == 0 && DateTime.UtcNow < deadline);

        Assert.Equal(AiGenerationOutcome.Cancelled, Assert.Single(ledger).Outcome);
        Assert.Empty(await host.SummariesAsync(userId));
        // The in-flight guard and the slot were released, and the cancelled attempt used a quota unit (it reached the model).
        var again = await PostOkAsync(user);
        Assert.Equal(2, again.GetProperty("generation").GetProperty("used").GetInt32());
    }

    [Fact]
    public async Task A_second_post_while_one_is_running_is_409_summary_in_progress_and_the_first_still_completes()
    {
        factory.SkipIfDockerUnavailable();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ScriptedChatClient().ReplyValidAfter(release.Task);
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumconc");
        await SeedRunsAsync(user);

        var started = client.RequestStarted.Task;
        var first = PostAsync(user);
        await started.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var second = await PostAsync(user);
        release.SetResult();
        var firstResponse = await first;

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:summary-in-progress", (await second.ReadAsync()).ProblemType());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(1, client.CallCount);
        Assert.Single(await host.LedgerAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task Another_users_generation_does_not_block_this_one_but_all_slots_taken_is_503_ai_busy_with_retry_after()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient();
        using var busy = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:MaxConcurrentGenerations"] = "0" });
        var user = busy.EntraUser("sumbusy");
        await SeedRunsAsync(user);

        var response = await PostAsync(user);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("10", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("urn:runnersdiary:problem:ai-busy", (await response.ReadAsync()).ProblemType());
        Assert.Equal(0, client.CallCount);
        Assert.Empty(await busy.LedgerAsync(await user.GetUserIdAsync())); // never reached the model: no quota used
    }

    // ---- what the model sees and what is stored ----------------------------------------------------------------------

    [Fact]
    public async Task No_free_text_of_a_run_reaches_the_model_or_the_stored_summary()
    {
        factory.SkipIfDockerUnavailable();
        const string canary = "CANARY-5e1d9c-zebra";
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumcanary");
        await SeedRunsAsync(user, run =>
        {
            run["title"] = canary + " title";
            run["notes"] = canary + " notes";
            run["gear"] = canary + " gear";
            run["weather"] = canary + " weather";
            run["location"] = new { latitude = 50.29, longitude = 18.67, name = canary + " place" };
        });
        // A race with free-text name, comment and category.
        var race = await user.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2025-03-15", r =>
        {
            r["runTypeId"] = 4;
            r["title"] = canary + " race title";
            r["race"] = new { name = canary + " race name", distanceCategory = "Marathon", categoryName = canary + " cat", bibNumber = canary };
            r["rating"] = new { stars = 4, comment = canary + " comment" };
        }), Ct);
        Assert.True(race.IsSuccessStatusCode, await race.Content.ReadAsStringAsync(Ct));
        var userId = await user.GetUserIdAsync();

        var body = await PostOkAsync(user);

        var request = Assert.Single(client.Requests);
        var sent = ScriptedChatClient.AllText(request);
        Assert.Contains("totals.runCount", sent, StringComparison.Ordinal); // the fact sheet is there...
        Assert.DoesNotContain("CANARY", sent, StringComparison.OrdinalIgnoreCase); // ...and none of the user's words
        Assert.DoesNotContain("zebra", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CANARY", body.ToString(), StringComparison.OrdinalIgnoreCase);
        var row = Assert.Single(await host.SummariesAsync(userId));
        Assert.DoesNotContain("CANARY", row.ContentJson, StringComparison.OrdinalIgnoreCase);
        // Request settings: strict JSON schema, token cap, no temperature.
        Assert.Equal(1500, request.Options!.MaxOutputTokens);
        Assert.NotNull(request.Options.ResponseFormat);
    }

    // ---- isolation ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Summaries_and_quota_are_per_caller_and_a_summary_of_another_user_is_never_returned_or_regenerated()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var a = host.EntraUser("sumisoA");
        var b = host.EntraUser("sumisoB");
        var guest = await host.GuestAsync("sumisoG");
        await SeedRunsAsync(a);
        await SeedRunsAsync(b);
        await SeedRunsAsync(guest);
        var aId = await a.GetUserIdAsync();
        var bId = await b.GetUserIdAsync();

        await PostOkAsync(a);

        // B and the guest have the same runs and the same period, but see nothing of A's summary or A's usage.
        foreach (var other in new[] { b, guest })
        {
            var read = await GetOkAsync(other);
            Assert.Equal("none", read.GetProperty("state").GetString());
            Assert.Equal(0, read.GetProperty("generation").GetProperty("used").GetInt32());
        }

        // B posting with regenerate=false must not reuse A's row: it generates its own (a second model call) for B only.
        await PostOkAsync(b);
        Assert.Equal(2, client.CallCount);
        var aRow = Assert.Single(await host.SummariesAsync(aId));
        var bRow = Assert.Single(await host.SummariesAsync(bId));
        Assert.NotEqual(aRow.Id, bRow.Id);
        Assert.Single(await host.LedgerAsync(aId));
        Assert.Single(await host.LedgerAsync(bId));
        Assert.Empty(await host.LedgerAsync(guest.UserId!.Value));
        Assert.Equal(1, (await GetOkAsync(a)).GetProperty("generation").GetProperty("used").GetInt32());
        Assert.Equal(0, (await GetOkAsync(guest)).GetProperty("generation").GetProperty("used").GetInt32());
    }
}
