using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Persistence;

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

    private sealed class ProbeCounter
    {
        public int Runs;
    }

    // A non-"live" check standing in for the SQL check: it counts how often the full report ran.
    private sealed class ProbeCheck(ProbeCounter counter) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref counter.Runs);
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }

    [Fact]
    public async Task ApiHealth_Anonymous_in_Production_opens_no_SQL_connection_and_skips_the_full_report()
    {
        factory.SkipIfDockerUnavailable();
        var counter = new ProbeCounter();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton(counter);
            services.AddHealthChecks().AddCheck<ProbeCheck>("probe");
        }));
        using var client = host.CreateClient();
        var interceptor = host.Services.GetRequiredService<SqlActivityInterceptor>();
        var before = interceptor.OpenedCount;

        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"status\":\"Healthy\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        Assert.Equal(before, interceptor.OpenedCount);
        Assert.Equal(0, counter.Runs);
    }

    [Fact]
    public async Task ApiHealth_with_a_valid_bearer_runs_the_full_report_once_per_cache_window()
    {
        factory.SkipIfDockerUnavailable();
        var counter = new ProbeCounter();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton(counter);
            services.AddHealthChecks().AddCheck<ProbeCheck>("probe");
        }));
        var guest = await host.GuestAsync("health-guest");

        for (var i = 0; i < 3; i++)
        {
            var response = await guest.Client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, counter.Runs);
    }

    [Fact]
    public async Task ApiHealth_with_an_invalid_bearer_is_treated_as_anonymous()
    {
        factory.SkipIfDockerUnavailable();
        var counter = new ProbeCounter();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton(counter);
            services.AddHealthChecks().AddCheck<ProbeCheck>("probe");
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-token");

        var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, counter.Runs);
    }
}
