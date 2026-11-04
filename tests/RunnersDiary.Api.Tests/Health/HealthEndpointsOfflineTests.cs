using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using RunnersDiary.Api.Features.Health;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Health;

public sealed class HealthEndpointsOfflineTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Alive_ReturnsHealthy_EvenWhenDependenciesAreDown()
    {
        var response = await _client.GetAsync(new Uri("/alive", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_Returns503_WhenSqlIsUnreachable()
    {
        var response = await _client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApiHealth_AnonymousInProduction_RunsOnlyLiveChecks_SoItStaysHealthyWithSqlDown()
    {
        var response = await _client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiHealthResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(ApiHealthStatus.Healthy, payload?.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Version));
    }

    [Fact]
    public async Task ApiHealth_IsRateLimitedPerClient()
    {
        using var limited = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:Health:UserPermitLimit", "2");
            b.UseSetting("RateLimiting:Health:Window", "00:10:00");
        });
        using var client = limited.CreateClient();
        var uri = new Uri("/api/health", UriKind.Relative);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(uri, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(uri, TestContext.Current.CancellationToken)).StatusCode);
        var limitedResponse = await client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter);
    }

    [Fact]
    public async Task ApiHealth_InDevelopment_RunsTheFullReport_Returns503WithUnhealthyStatus_WhenSqlIsUnreachable()
    {
        using var development = factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = development.CreateClient();
        var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"Unhealthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("runnersdiary", body, StringComparison.OrdinalIgnoreCase);
        var payload = await response.Content.ReadFromJsonAsync<ApiHealthResponse>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Version));
    }
}
