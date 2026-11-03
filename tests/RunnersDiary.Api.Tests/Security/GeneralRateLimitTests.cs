using System.Net;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Security;

[Collection(ContainersFixtureGroup.Name)]
public sealed class GeneralRateLimitTests(ContainersApiFactory factory)
{
    [Fact]
    public async Task Callers_over_the_general_limit_get_429_with_retry_after_and_each_caller_has_their_own_bucket()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:General:TokenLimit", "3");
            b.UseSetting("RateLimiting:General:TokensPerPeriod", "1");
            b.UseSetting("RateLimiting:General:ReplenishmentPeriod", "00:10:00");
        });
        var noisy = limited.EntraUser("noisy");
        var quiet = limited.EntraUser("quiet");
        var uri = new Uri("/api/run-types", UriKind.Relative);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await noisy.Client.GetAsync(uri, TestContext.Current.CancellationToken)).StatusCode);
        }

        var rejected = await noisy.Client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("urn:runnersdiary:problem:rate-limited", (await rejected.ReadAsync()).GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.OK, (await quiet.Client.GetAsync(uri, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_traffic_is_limited_per_client_ip_and_health_is_exempt()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:General:TokenLimit", "2");
            b.UseSetting("RateLimiting:General:TokensPerPeriod", "1");
            b.UseSetting("RateLimiting:General:ReplenishmentPeriod", "00:10:00");
        });
        using var client = limited.CreateClient();
        var me = new Uri("/api/me", UriKind.Relative);

        async Task<HttpStatusCode> Call(string ip, Uri uri)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(TestHost.ClientIpHeader, ip);
            return (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await Call("203.0.113.5", me));
        Assert.Equal(HttpStatusCode.Unauthorized, await Call("203.0.113.5", me));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Call("203.0.113.5", me));
        Assert.Equal(HttpStatusCode.Unauthorized, await Call("203.0.113.6", me));
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await Call("203.0.113.5", new Uri("/api/health", UriKind.Relative)));
        }
    }
}
