using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RunnersDiary.Api.Features.Health;

/// <summary>
/// Runs the full health report (including the SQL check) at most once per <see cref="CacheDuration"/>, and the SQL-free
/// "live" subset on demand. Without the cache, every caller would cost a SQL round-trip and keep serverless SQL from
/// auto-pausing (ADR 0006).
/// </summary>
public sealed class CachedHealthReport(HealthCheckService healthChecks, TimeProvider timeProvider) : IDisposable
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private HealthStatus _status;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>Only the checks tagged "live" (never SQL or Blob): safe for anonymous callers, cheap enough not to cache.</summary>
    public async Task<HealthStatus> GetLiveStatusAsync(CancellationToken cancellationToken) =>
        (await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains("live"), cancellationToken)).Status;

    /// <summary>The full report, cached; it opens a SQL connection when the cache is stale.</summary>
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
