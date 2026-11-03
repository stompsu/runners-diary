using System.Net;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Health;

[Collection(ContainersFixtureGroup.Name)]
public sealed class HealthEndpointsTests(ContainersApiFactory factory)
{
    // Created per test, after the Docker check, so the host never starts without containers.
    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsHealthy_WithSqlAndBlobUp()
    {
        factory.SkipIfDockerUnavailable();
        var response = await Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApiHealth_ReturnsHealthyJson_WithVersion()
    {
        factory.SkipIfDockerUnavailable();
        var response = await Client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.Ordinal);
        Assert.Matches("\"version\":\"[^\"]+\"", body);
    }
}
