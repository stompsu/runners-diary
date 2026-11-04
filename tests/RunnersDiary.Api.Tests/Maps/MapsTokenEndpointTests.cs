using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Tests.Maps;

/// <summary>GET /api/maps/token (M7 plan 5.3): authenticated, rate limited, never cached, 503 unless Azure Maps display is on. No Docker, no network.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class MapsTokenEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Url = new("/api/maps/token", UriKind.Relative);

    private sealed class FakeIssuer : IMapsTokenIssuer
    {
        public bool Throw { get; set; }

        public int Calls { get; private set; }

        public bool IsEnabled => true;

        public Task<MapsToken> GetTokenAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Throw
                ? throw new HttpRequestException("ARM said: AuthorizationFailed for subscription 0000")
                : Task.FromResult(new MapsToken("sas-token-value", new DateTimeOffset(2026, 11, 4, 14, 0, 0, TimeSpan.Zero)));
        }
    }

    private static void AssertNotCacheable(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.False(response.Headers.CacheControl?.Public);
    }

    private WebApplicationFactory<Program> WithIssuer(FakeIssuer issuer, Action<IWebHostBuilder>? more = null) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services => services.AddSingleton<IMapsTokenIssuer>(issuer));
            more?.Invoke(b);
        });

    [Fact]
    public async Task Without_a_token_the_answer_is_401()
    {
        factory.SkipIfDockerUnavailable();
        using var host = WithIssuer(new FakeIssuer());
        using var anonymous = host.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url, Ct)).StatusCode);
    }

    [Fact]
    public async Task By_default_display_is_off_so_the_answer_is_503_feature_unavailable_and_not_cacheable()
    {
        factory.SkipIfDockerUnavailable();
        var host = factory;
        var user = host.EntraUser("maps-default");

        var response = await user.Client.GetAsync(Url, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNotCacheable(response);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("urn:runnersdiary:problem:feature-unavailable", (await response.ReadAsync()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task An_authenticated_user_and_a_guest_get_the_token_with_no_store()
    {
        factory.SkipIfDockerUnavailable();
        var issuer = new FakeIssuer();
        using var host = WithIssuer(issuer);
        var user = host.EntraUser("maps-user");
        var guestActor = await host.GuestAsync("maps-guest");
        var guest = guestActor.Client;

        foreach (var client in new[] { user.Client, guest })
        {
            var response = await client.GetAsync(Url, Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertNotCacheable(response);
            var body = await response.ReadAsync();
            Assert.Equal("sas-token-value", body.GetProperty("token").GetString());
            Assert.Equal(new DateTimeOffset(2026, 11, 4, 14, 0, 0, TimeSpan.Zero), body.GetProperty("expiresAt").GetDateTimeOffset());
        }

        Assert.Equal(2, issuer.Calls);
    }

    [Fact]
    public async Task An_issuing_failure_is_a_503_without_provider_details()
    {
        factory.SkipIfDockerUnavailable();
        using var host = WithIssuer(new FakeIssuer { Throw = true });
        var user = host.EntraUser("maps-fail");

        var response = await user.Client.GetAsync(Url, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNotCacheable(response);
        Assert.Contains("feature-unavailable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthorizationFailed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0000", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_maps_token_policy_limits_users_and_guests_separately_and_per_caller()
    {
        factory.SkipIfDockerUnavailable();
        using var host = WithIssuer(new FakeIssuer(), b =>
        {
            b.UseSetting("RateLimiting:MapsToken:UserPermitLimit", "2");
            b.UseSetting("RateLimiting:MapsToken:GuestPermitLimit", "1");
        });
        var user = host.EntraUser("maps-limited");
        var other = host.EntraUser("maps-other");
        var guestActor = await host.GuestAsync("maps-guest");
        var guest = guestActor.Client;

        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(Url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(Url, Ct)).StatusCode);
        var limited = await user.Client.GetAsync(Url, Ct);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(Url, Ct)).StatusCode);
        var guestLimited = await guest.GetAsync(Url, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.TooManyRequests, guestLimited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.Client.GetAsync(Url, Ct)).StatusCode);
    }

    [Fact]
    public void The_shipped_limits_are_30_per_hour_for_users_and_10_for_guests()
    {
        var limits = new RunnersDiary.Api.RateLimiting.RateLimitingOptions().MapsToken;

        Assert.Equal((30, 10, TimeSpan.FromHours(1)), (limits.UserPermitLimit, limits.GuestPermitLimit, limits.Window));
    }
}
