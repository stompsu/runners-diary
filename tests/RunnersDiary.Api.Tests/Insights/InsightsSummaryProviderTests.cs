using System.Globalization;
using System.Net;
using Azure.Identity;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Ai;
using static RunnersDiary.Api.Tests.Insights.SummaryTestSupport;

namespace RunnersDiary.Api.Tests.Insights;

/// <summary>
/// M6 review fixes on the provider side: a provider 429 is 503 ai-busy and costs no quota (M4), a credential failure is 503
/// feature-unavailable without a ledger row (L6), global budgets count attempts (M2), and a long model id is truncated (L7).
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class InsightsSummaryProviderTests(ContainersApiFactory factory)
{
    private static readonly object Regenerate = new { from = From, to = To, regenerate = true };

    [Fact]
    public async Task A_provider_429_is_503_ai_busy_with_the_providers_retry_after_and_costs_no_quota()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient()
            .Throw(ProviderErrors.Throttled(new Dictionary<string, string> { ["Retry-After"] = "25" }))
            .ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:UserPer24h"] = "1" });
        var user = host.EntraUser("prov429");
        await SeedRunsAsync(user);

        var response = await PostAsync(user);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("25", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("urn:runnersdiary:problem:ai-busy", (await response.ReadAsync()).ProblemType());
        Assert.Empty(await host.LedgerAsync(await user.GetUserIdAsync()));
        Assert.Equal(0, (await GetOkAsync(user)).GetProperty("generation").GetProperty("used").GetInt32());

        // The single generation of the quota is still there.
        Assert.Equal(1, (await PostOkAsync(user)).GetProperty("generation").GetProperty("used").GetInt32());
    }

    [Fact]
    public async Task A_429_after_an_invalid_attempt_writes_a_throttled_row_that_counts_globally_but_not_for_the_guest()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyText("not json").Throw(ProviderErrors.Throttled());
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:GuestGlobalPer24h"] = "100000", ["Ai:Quotas:GlobalPer24h"] = "100000" });
        var guest = await host.GuestAsync("prov429g");
        await SeedRunsAsync(guest);

        var response = await PostAsync(guest);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("10", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(CultureInfo.InvariantCulture)); // no hint: the default
        var row = Assert.Single(await host.LedgerAsync(guest.UserId!.Value));
        Assert.Equal((AiGenerationOutcome.Throttled, (byte)1, 500, true), (row.Outcome, row.Attempts, row.InputTokens, row.IsGuest));
        Assert.Equal(0, (await GetOkAsync(guest)).GetProperty("generation").GetProperty("used").GetInt32());
    }

    [Theory]
    [InlineData(typeof(CredentialUnavailableException))]
    [InlineData(typeof(AuthenticationFailedException))]
    public async Task A_credential_failure_is_503_feature_unavailable_without_a_ledger_row(Type exceptionType)
    {
        factory.SkipIfDockerUnavailable();
        var exception = (Exception)Activator.CreateInstance(exceptionType, "EnvironmentCredential authentication unavailable: secret detail")!;
        using var host = AiTestHost.Create(factory, new ScriptedChatClient().Throw(exception));
        var user = host.EntraUser("provcred" + exceptionType.Name.Length.ToString(CultureInfo.InvariantCulture));
        await SeedRunsAsync(user);

        var response = await PostAsync(user);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:feature-unavailable", problem.ProblemType());
        Assert.DoesNotContain("secret detail", problem.ToString(), StringComparison.Ordinal);
        Assert.Null(response.Headers.RetryAfter);
        Assert.Empty(await host.LedgerAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task The_global_limit_counts_attempts_so_a_two_attempt_generation_uses_two()
    {
        factory.SkipIfDockerUnavailable();
        var (total, _) = await factory.GlobalLedgerAsync();
        var client = new ScriptedChatClient().ReplyText("not json").ReplyValid().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string> { ["Ai:Quotas:GlobalPer24h"] = (total + 2).ToString(CultureInfo.InvariantCulture) });
        var first = host.EntraUser("provatt1");
        var second = host.EntraUser("provatt2");
        await SeedRunsAsync(first);
        await SeedRunsAsync(second);
        await PostOkAsync(first); // invalid, then valid: one generation, two attempts

        var blocked = await PostAsync(second);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var problem = await blocked.ReadAsync();
        Assert.Equal("global", problem.GetProperty("scope").GetString());
        Assert.Equal(total + 2, problem.GetProperty("limit").GetInt32());
        Assert.Equal(2, client.CallCount);
        Assert.Equal(1, (await GetOkAsync(first)).GetProperty("generation").GetProperty("used").GetInt32());
    }

    [Fact]
    public async Task A_model_id_longer_than_the_column_is_truncated_not_a_failure()
    {
        factory.SkipIfDockerUnavailable();
        var longId = "model-" + new string('x', 300);
        using var host = AiTestHost.Create(factory, new ScriptedChatClient().ReplyValid(modelId: longId));
        var user = host.EntraUser("provmodel");
        await SeedRunsAsync(user);

        var body = await PostOkAsync(user, Regenerate);

        Assert.Equal(longId[..100], body.GetProperty("summary").GetProperty("model").GetString());
        Assert.Equal(longId[..100], Assert.Single(await host.SummariesAsync(await user.GetUserIdAsync())).ModelId);
    }
}
