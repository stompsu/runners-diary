using System.Net;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>AC7: guest creation is limited per client IP; the limit is not shared between IPs.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GuestRateLimitTests(ContainersApiFactory factory)
{
    [Fact]
    public async Task Over_the_per_ip_limit_creation_answers_429_with_retry_after_and_another_ip_still_gets_201()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:GuestCreate:PermitLimit", "2"));
        using var client = limited.CreateClient();

        async Task<HttpResponseMessage> Create(string ip)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/guest-sessions", UriKind.Relative));
            request.Headers.Add(TestHost.ClientIpHeader, ip);
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.Created, (await Create("198.51.100.20")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create("198.51.100.20")).StatusCode);
        var rejected = await Create("198.51.100.20");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("urn:runnersdiary:problem:rate-limited", (await rejected.ReadAsync()).GetProperty("type").GetString());

        Assert.Equal(HttpStatusCode.Created, (await Create("198.51.100.21")).StatusCode);
    }
}
