using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using RunnersDiary.Api.Tests.Infrastructure;
using static RunnersDiary.Api.Tests.Insights.SummaryTestSupport;

namespace RunnersDiary.Api.Tests.Insights;

/// <summary>AC8: durable quotas from the usage ledger (low limits through configuration).</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class InsightsSummaryQuotaTests(ContainersApiFactory factory)
{
    private static readonly object Regenerate = new { from = From, to = To, regenerate = true };

    [Fact]
    public async Task A_user_over_the_rolling_limit_gets_429_ai_quota_with_scope_limit_resets_at_and_retry_after()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:UserPer24h"] = "2" });
        var user = host.EntraUser("quser");
        await SeedRunsAsync(user);
        var first = await PostOkAsync(user, Regenerate);
        await PostOkAsync(user, Regenerate);

        var response = await PostAsync(user, Regenerate);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:ai-quota", problem.ProblemType());
        Assert.Equal("account", problem.GetProperty("scope").GetString());
        Assert.Equal(2, problem.GetProperty("limit").GetInt32());
        var resetsAt = problem.GetProperty("resetsAt").GetDateTimeOffset();
        var firstCreated = first.GetProperty("summary").GetProperty("createdAt").GetDateTimeOffset();
        Assert.InRange((resetsAt - firstCreated - TimeSpan.FromHours(24)).TotalSeconds, -3, 3);
        var retryAfter = response.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        Assert.InRange(retryAfter, 3600, 86400 + 5);
        Assert.Equal(2, client.CallCount);

        var read = (await GetOkAsync(user)).GetProperty("generation");
        Assert.Equal("quotaExhausted", read.GetProperty("availability").GetString());
        Assert.Equal(2, read.GetProperty("used").GetInt32());
        Assert.Equal(2, read.GetProperty("limit").GetInt32());
        Assert.Equal("rolling24h", read.GetProperty("scope").GetString());
        Assert.Equal(resetsAt, read.GetProperty("resetsAt").GetDateTimeOffset());
        // A fresh stored summary can still be read and re-posted without regenerating.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(user)).StatusCode);
    }

    [Fact]
    public async Task Ledger_rows_older_than_24_hours_no_longer_count_for_a_user()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:UserPer24h"] = "1" });
        var user = host.EntraUser("qold");
        await SeedRunsAsync(user);
        await host.SeedLedgerAsync(await user.GetUserIdAsync(), false, DateTime.UtcNow.AddHours(-25), 3);

        var body = await PostOkAsync(user);

        Assert.Equal(1, body.GetProperty("generation").GetProperty("used").GetInt32());
    }

    [Fact]
    public async Task A_guest_gets_three_generations_then_429_with_account_scope_and_no_reset_and_each_guest_has_its_own_budget()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid().ReplyValid().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:GuestGlobalPer24h"] = "100000", ["Ai:Quotas:GlobalPer24h"] = "100000" });
        var guest = await host.GuestAsync("qguest");
        var other = await host.GuestAsync("qguest2");
        await SeedRunsAsync(guest);
        await SeedRunsAsync(other);
        for (var i = 0; i < 3; i++)
        {
            await PostOkAsync(guest, Regenerate);
        }

        var response = await PostAsync(guest, Regenerate);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:ai-quota", problem.ProblemType());
        Assert.Equal("account", problem.GetProperty("scope").GetString());
        Assert.Equal(3, problem.GetProperty("limit").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, problem.GetProperty("resetsAt").ValueKind);
        Assert.Null(response.Headers.RetryAfter);
        var read = (await GetOkAsync(guest)).GetProperty("generation");
        Assert.Equal("quotaExhausted", read.GetProperty("availability").GetString());
        Assert.Equal("account", read.GetProperty("scope").GetString());
        Assert.True((await host.LedgerAsync(guest.UserId!.Value)).All(g => g.IsGuest));

        await PostOkAsync(other, Regenerate);
        Assert.Equal(1, (await GetOkAsync(other)).GetProperty("generation").GetProperty("used").GetInt32());
    }

    [Fact]
    public async Task The_global_daily_limit_blocks_everybody_with_global_scope()
    {
        factory.SkipIfDockerUnavailable();
        var (total, _) = await factory.GlobalLedgerAsync();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:GlobalPer24h"] = (total + 1).ToString(CultureInfo.InvariantCulture) });
        var first = host.EntraUser("qglob1");
        var second = host.EntraUser("qglob2");
        await SeedRunsAsync(first);
        await SeedRunsAsync(second);
        await PostOkAsync(first);

        var response = await PostAsync(second);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.ReadAsync();
        Assert.Equal("global", problem.GetProperty("scope").GetString());
        Assert.Equal(total + 1, problem.GetProperty("limit").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, problem.GetProperty("resetsAt").ValueKind);
        Assert.Null(response.Headers.RetryAfter);
        Assert.Equal("quotaExhausted", (await GetOkAsync(second)).GetProperty("generation").GetProperty("availability").GetString());
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task The_guest_global_limit_blocks_guests_but_not_users()
    {
        factory.SkipIfDockerUnavailable();
        var (_, guests) = await factory.GlobalLedgerAsync();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string>
        {
            ["Ai:Quotas:GuestGlobalPer24h"] = (guests + 1).ToString(CultureInfo.InvariantCulture),
            ["Ai:Quotas:GlobalPer24h"] = "100000",
        });
        var g1 = await host.GuestAsync("qgg1");
        var g2 = await host.GuestAsync("qgg2");
        var user = host.EntraUser("qggu");
        await SeedRunsAsync(g1);
        await SeedRunsAsync(g2);
        await SeedRunsAsync(user);
        await PostOkAsync(g1);

        var blocked = await PostAsync(g2);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var problem = await blocked.ReadAsync();
        Assert.Equal("global", problem.GetProperty("scope").GetString());
        Assert.Equal(guests + 1, problem.GetProperty("limit").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(user)).StatusCode);
    }

    [Fact]
    public async Task Failed_attempts_count_toward_the_quota()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyText("bad").ReplyText("bad");
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:UserPer24h"] = "1" });
        var user = host.EntraUser("qfail");
        await SeedRunsAsync(user);
        Assert.Equal(HttpStatusCode.BadGateway, (await PostAsync(user)).StatusCode);

        var response = await PostAsync(user);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:ai-quota", (await response.ReadAsync()).ProblemType());
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task The_ai_summary_burst_limit_answers_429_rate_limited_with_retry_after()
    {
        factory.SkipIfDockerUnavailable();
        using var host = AiTestHost.Create(factory, new ScriptedChatClient(), new Dictionary<string, string> { ["RateLimiting:AiSummary:UserPermitLimit"] = "2" });
        var user = host.EntraUser("qburst");

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(user, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(user, new { })).StatusCode);
        var limited = await PostAsync(user, new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal("urn:runnersdiary:problem:rate-limited", (await limited.ReadAsync()).ProblemType());
        // Reads use the general limiter and are unaffected.
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(Url(), Ct)).StatusCode);
    }
}
