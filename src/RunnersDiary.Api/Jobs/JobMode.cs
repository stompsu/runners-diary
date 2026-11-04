using Microsoft.Extensions.Hosting;
using RunnersDiary.Api.Http;
using RunnersDiary.Infrastructure;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Api.Jobs;

/// <summary>
/// One-shot entry mode of the API image (ADR 0032 §2): <c>dotnet RunnersDiary.Api.dll --job guest-cleanup</c> builds a host with
/// ServiceDefaults and Infrastructure only (no Kestrel, no authentication, no endpoints, no hosted services), runs one cleanup and
/// exits with 0 on success and 1 on failure. Used by the scheduled Container Apps job <c>job-guest-cleanup</c>.
/// </summary>
internal static partial class JobMode
{
    public const string GuestCleanupJob = "guest-cleanup";

    private const string JobArgument = "--job";

    /// <summary>Safety bound on passes (batch size 50 by default): a stuck guest must not turn the job into an endless loop.</summary>
    internal const int MaxPasses = 1000;

    /// <summary>Whether <paramref name="args"/> ask for job mode (<c>--job x</c> or <c>--job=x</c>); the value may be empty or unknown.</summary>
    public static bool TryGetJob(string[] args, out string job)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], JobArgument, StringComparison.Ordinal))
            {
                job = i + 1 < args.Length ? args[i + 1] : string.Empty;
                return true;
            }

            if (args[i].StartsWith(JobArgument + "=", StringComparison.Ordinal))
            {
                job = args[i][(JobArgument.Length + 1)..];
                return true;
            }
        }

        job = string.Empty;
        return false;
    }

    /// <param name="configure">Test seam: runs after the services are registered and before the host is built.</param>
    /// <returns>The process exit code: 0 on success, 1 on any failure (including an unknown job).</returns>
    public static async Task<int> RunAsync(string job, string[] args, Action<HostApplicationBuilder>? configure = null)
    {
        if (!string.Equals(job, GuestCleanupJob, StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync($"Unknown job '{job}'. Supported jobs: {GuestCleanupJob}.");
            return 1;
        }

        IHost? host = null;
        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            ProductionConfigurationValidator.ValidateJob(builder.Configuration, builder.Environment);
            builder.AddServiceDefaults();
            builder.ConfigureJobSettings();
            builder.AddInfrastructure(jobMode: true);
            configure?.Invoke(builder);
            host = builder.Build();

            await host.StartAsync();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(JobMode));
            try
            {
                await RunGuestCleanupAsync(host.Services.GetRequiredService<IGuestCleanupPass>(), logger, lifetime.ApplicationStopping);
                return 0;
            }
            catch (Exception ex)
            {
                LogFailed(logger, ex);
                return 1;
            }
        }
#pragma warning disable CA1031 // Entry point: any startup or shutdown failure (configuration, DI) must become exit code 1, never an unhandled crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync($"Job '{job}' failed to start ({ex.GetType().Name}).");
            return 1;
        }
        finally
        {
            if (host is not null)
            {
                try
                {
                    await host.StopAsync();
                }
                finally
                {
                    // Disposing flushes the telemetry exporters before the container stops.
                    await ((IAsyncDisposable)host).DisposeAsync();
                }
            }
        }
    }

    /// <summary>Runs passes until one removes nothing (the batch is bounded), including the AI ledger purge of every pass.</summary>
    internal static async Task RunGuestCleanupAsync(IGuestCleanupPass cleanup, ILogger logger, CancellationToken cancellationToken)
    {
        var total = 0;
        for (var pass = 1; pass <= MaxPasses; pass++)
        {
            var removed = await cleanup.RunOnceAsync(cancellationToken);
            total += removed;
            if (removed == 0)
            {
                break;
            }
        }

        LogCompleted(logger, total);
    }

    /// <summary>The job runs its pass itself; the periodic service and the startup work must stay off whatever the settings say.</summary>
    private static void ConfigureJobSettings(this HostApplicationBuilder builder) =>
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Guests:Cleanup:Enabled"] = "false",
            [DependencyInjection.MigrateOnStartupKey] = "false",
            [DependencyInjection.CreateContainerOnStartupKey] = "false",
        });

    [LoggerMessage(Level = LogLevel.Information, Message = "Guest cleanup job finished; removed {Removed} expired guests.")]
    private static partial void LogCompleted(ILogger logger, int removed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Guest cleanup job failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
