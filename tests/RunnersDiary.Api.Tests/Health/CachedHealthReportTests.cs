using Microsoft.Extensions.Diagnostics.HealthChecks;
using RunnersDiary.Api.Features.Health;

namespace RunnersDiary.Api.Tests.Health;

public sealed class CachedHealthReportTests
{
    [Fact]
    public async Task GetStatus_RunsChecksOncePerCacheWindow()
    {
        var checks = new CountingHealthCheckService(HealthStatus.Healthy);
        var time = new ManualTimeProvider();
        using var cache = new CachedHealthReport(checks, time);

        Assert.Equal(HealthStatus.Healthy, await cache.GetStatusAsync(TestContext.Current.CancellationToken));
        checks.Status = HealthStatus.Unhealthy;
        time.Advance(CachedHealthReport.CacheDuration - TimeSpan.FromSeconds(1));
        Assert.Equal(HealthStatus.Healthy, await cache.GetStatusAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, checks.Calls);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HealthStatus.Unhealthy, await cache.GetStatusAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, checks.Calls);
    }

    private sealed class CountingHealthCheckService(HealthStatus status) : HealthCheckService
    {
        public HealthStatus Status { get; set; } = status;

        public int Calls { get; private set; }

        public override Task<HealthReport> CheckHealthAsync(Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new HealthReport(new Dictionary<string, HealthReportEntry>(), Status, TimeSpan.Zero));
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
