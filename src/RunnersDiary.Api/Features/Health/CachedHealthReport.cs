using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RunnersDiary.Api.Features.Health;

/// <summary>
/// Runs the health checks at most once per <see cref="CacheDuration"/> for the anonymous /api/health endpoint.
/// Without this, every browser tab and anonymous caller would cost a SQL round-trip and keep serverless SQL
/// from auto-pausing (ADR 0006).
/// </summary>
public sealed class CachedHealthReport(HealthCheckService healthChecks, TimeProvider timeProvider) : IDisposable
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private HealthStatus _status;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<HealthStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() < _expiresAt)
        {
            return _status;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed while this one waited.
            if (timeProvider.GetUtcNow() >= _expiresAt)
            {
                var report = await healthChecks.CheckHealthAsync(cancellationToken);
                _status = report.Status;
                _expiresAt = timeProvider.GetUtcNow() + CacheDuration;
            }

            return _status;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();
}
