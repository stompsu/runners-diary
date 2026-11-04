using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// Periodic guest cleanup (ADR 0011 §5). It runs only while an API replica is alive and, since M7 (ADR 0032 §2), only when the
/// process has opened a SQL connection since the previous tick: a replica woken by an anonymous request or a crawler must not
/// wake an auto-paused serverless database by itself. Expired guests of an idle app are removed by the weekly cleanup job.
/// Not registered during build-time OpenAPI generation (the whole infrastructure registration is skipped there) nor in job mode.
/// </summary>
public sealed partial class GuestCleanupService(
    IGuestCleanupPass cleanup, SqlActivityInterceptor sqlActivity, IOptions<GuestOptions> options, TimeProvider clock, ILogger<GuestCleanupService> logger)
    : BackgroundService
{
    private long _lastSeenOpened;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Cleanup;
        try
        {
            await Task.Delay(settings.InitialDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(settings.Interval, clock);
            do
            {
                await TickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>Runs one pass if SQL was used since the last tick; returns whether it ran.</summary>
    internal async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        if (sqlActivity.OpenedCount == Interlocked.Read(ref _lastSeenOpened))
        {
            LogSkipped();
            return false;
        }

        try
        {
            await cleanup.RunOnceAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
        }
        finally
        {
            // Taken after the pass on purpose: the pass opens connections itself, and counting them as "activity" would make every
            // tick trigger the next one and keep SQL awake forever. Activity during the pass is picked up by the opportunistic run.
            Interlocked.Exchange(ref _lastSeenOpened, sqlActivity.OpenedCount);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Periodic guest cleanup failed; it will be retried after the next SQL activity or by the weekly job.")]
    private partial void LogFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Periodic guest cleanup skipped: no SQL connection was opened since the last tick.")]
    private partial void LogSkipped();
}
