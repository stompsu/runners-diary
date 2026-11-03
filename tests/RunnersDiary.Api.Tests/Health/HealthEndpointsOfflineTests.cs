using System.Net;
using System.Net.Http.Json;
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
    public async Task ApiHealth_Returns503WithUnhealthyStatus_WhenSqlIsUnreachable()
    {
        var response = await _client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"Unhealthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("runnersdiary", body, StringComparison.OrdinalIgnoreCase);
        var payload = await response.Content.ReadFromJsonAsync<ApiHealthResponse>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Version));
    }
}
