using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Tests.Maps;

public sealed class AzureMapsSasTokenIssuerTests
{
    private const string ResourceId = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-runnersdiary-prod/providers/Microsoft.Maps/accounts/map-rd-prod";
    private const string PrincipalId = "11111111-2222-3333-4444-555555555555";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AzureMapsOptions Options() => new()
    {
        AccountClientId = "account-unique-id",
        AccountResourceId = ResourceId,
        SasPrincipalId = PrincipalId,
        SasRegions = ["westeurope", "northeurope"],
        SasMaxRatePerSecond = 10,
    };

    private sealed class Fixture
    {
        public int Counter;
        public bool Fail;
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        public FakeCredential Credential { get; } = new("arm-token");
        public StubHandler Handler { get; }
        public AzureMapsSasTokenIssuer Issuer { get; }

        public Fixture()
        {
            Handler = new StubHandler(_ => Fail
                ? StubHandler.Json("{\"error\":{\"code\":\"AuthorizationFailed\",\"message\":\"detail\"}}", HttpStatusCode.Forbidden)
                : StubHandler.Json($"{{\"accountSasToken\":\"sas-token-{++Counter}\"}}"));
            Issuer = new AzureMapsSasTokenIssuer(
                new HttpClient(Handler) { BaseAddress = new Uri("https://management.azure.com/") },
                Credential, Options(), Time, NullLogger<AzureMapsSasTokenIssuer>.Instance);
        }
    }

    [Fact]
    public async Task It_posts_listSas_with_the_limits_the_plan_demands_and_the_api_identitys_arm_token()
    {
        var f = new Fixture();

        var token = await f.Issuer.GetTokenAsync(Ct);

        var (request, body) = Assert.Single(f.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("management.azure.com", request.RequestUri!.Host);
        Assert.Equal(ResourceId + "/listSas", request.RequestUri.AbsolutePath);
        Assert.Equal("?api-version=2023-06-01", request.RequestUri.Query);
        Assert.Equal("Bearer arm-token", request.Headers.Authorization?.ToString());
        Assert.Equal(["https://management.azure.com/.default"], f.Credential.Scopes);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("primaryKey", root.GetProperty("signingKey").GetString());
        Assert.Equal(PrincipalId, root.GetProperty("principalId").GetString());
        Assert.Equal(10, root.GetProperty("maxRatePerSecond").GetInt32());
        Assert.Equal(["westeurope", "northeurope"], root.GetProperty("regions").EnumerateArray().Select(e => e.GetString()));
        var start = root.GetProperty("start").GetDateTimeOffset();
        var expiry = root.GetProperty("expiry").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5), expiry - start);
        Assert.True(expiry - start <= TimeSpan.FromHours(24));
        Assert.True(start < f.Time.GetUtcNow()); // backdated for clock skew

        Assert.Equal("sas-token-1", token.Token);
        Assert.Equal(expiry, token.ExpiresAt);
    }

    [Fact]
    public async Task Regions_are_omitted_when_none_are_configured()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{\"accountSasToken\":\"t\"}"));
        var options = Options();
        options.SasRegions = [];
        var issuer = new AzureMapsSasTokenIssuer(new HttpClient(handler) { BaseAddress = new Uri("https://management.azure.com/") },
            new FakeCredential(), options, new FakeTimeProvider(), NullLogger<AzureMapsSasTokenIssuer>.Instance);

        await issuer.GetTokenAsync(Ct);

        Assert.DoesNotContain("regions", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_token_is_shared_and_cached_until_the_refresh_window()
    {
        var f = new Fixture();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => f.Issuer.GetTokenAsync(Ct)));
        f.Time.Advance(TimeSpan.FromMinutes(89)); // 31 minutes left
        var later = await f.Issuer.GetTokenAsync(Ct);

        Assert.Single(f.Handler.Requests);
        Assert.All(results, r => Assert.Equal("sas-token-1", r.Token));
        Assert.Equal("sas-token-1", later.Token);
    }

    [Fact]
    public async Task The_token_is_renewed_when_less_than_thirty_minutes_remain()
    {
        var f = new Fixture();
        var first = await f.Issuer.GetTokenAsync(Ct);

        f.Time.Advance(TimeSpan.FromMinutes(91)); // 29 minutes left
        var second = await f.Issuer.GetTokenAsync(Ct);

        Assert.Equal(2, f.Handler.Requests.Count);
        Assert.Equal("sas-token-2", second.Token);
        Assert.True(second.ExpiresAt > first.ExpiresAt);
        Assert.True(second.ExpiresAt - f.Time.GetUtcNow() > TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task A_failed_renewal_keeps_serving_the_still_usable_token_and_backs_off()
    {
        var f = new Fixture();
        await f.Issuer.GetTokenAsync(Ct);
        f.Time.Advance(TimeSpan.FromMinutes(100)); // 20 minutes left
        f.Fail = true;

        var kept = await f.Issuer.GetTokenAsync(Ct);
        var again = await f.Issuer.GetTokenAsync(Ct); // inside the back-off: no new ARM call

        Assert.Equal("sas-token-1", kept.Token);
        Assert.Equal("sas-token-1", again.Token);
        Assert.Equal(2, f.Handler.Requests.Count);
    }

    [Fact]
    public async Task A_failure_without_a_usable_token_throws_without_leaking_arm_details_then_backs_off_then_recovers()
    {
        var f = new Fixture { Fail = true };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => f.Issuer.GetTokenAsync(Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => f.Issuer.GetTokenAsync(Ct)); // backing off

        Assert.DoesNotContain("AuthorizationFailed", ex.Message, StringComparison.Ordinal);
        Assert.Single(f.Handler.Requests);

        f.Fail = false;
        f.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("sas-token-1", (await f.Issuer.GetTokenAsync(Ct)).Token);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"accountSasToken\":\"\"}")]
    [InlineData("{\"accountSasToken\":5}")]
    public async Task A_response_without_a_token_is_an_error(string json)
    {
        var handler = new StubHandler(_ => StubHandler.Json(json));
        var issuer = new AzureMapsSasTokenIssuer(new HttpClient(handler) { BaseAddress = new Uri("https://management.azure.com/") },
            new FakeCredential(), Options(), new FakeTimeProvider(), NullLogger<AzureMapsSasTokenIssuer>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.GetTokenAsync(Ct));
    }

    [Fact]
    public async Task The_disabled_issuer_reports_disabled_and_never_issues()
    {
        var disabled = new DisabledMapsTokenIssuer();

        Assert.False(disabled.IsEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.GetTokenAsync(Ct));
        Assert.True(new Fixture().Issuer.IsEnabled);
    }
}
