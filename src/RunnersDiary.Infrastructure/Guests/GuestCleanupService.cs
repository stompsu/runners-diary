using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// Periodic guest cleanup (ADR 0011 §5). It runs only while an API replica is alive, i.e. while somebody uses the app and SQL is
/// awake anyway, so it never wakes a paused serverless database by itself. Not registered during build-time OpenAPI generation
/// (the whole infrastructure registration is skipped there).
/// </summary>
public sealed partial class GuestCleanupService(GuestCleanup cleanup, IOptions<GuestOptions> options, TimeProvider clock, ILogger<GuestCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Cleanup;
        try
        {
            await Task.Delay(settings.InitialDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(settings.Interval, clock);
            do
            {
                try
                {
                    await cleanup.RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFailed(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Periodic guest cleanup failed; it will be retried.")]
    private partial void LogFailed(Exception exception);
}
