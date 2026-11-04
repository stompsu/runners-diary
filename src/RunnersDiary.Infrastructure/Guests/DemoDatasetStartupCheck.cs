using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Time;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// Fails host startup when the demo dataset is missing, malformed, references missing GPX files, contains a GPX file the pipeline rejects (files are processed once while loading) or exceeds the guest run caps,
/// instead of failing every guest creation later. Not registered during build-time OpenAPI generation.
/// </summary>
internal sealed class DemoDatasetStartupCheck(
    DemoDatasetLoader loader, IOptions<RunLimitsOptions> limits, TimeProvider clock) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dataset = loader.Get();
        loader.LoadAndValidate(limits.Value, UserToday.Get(clock, dataset.Template.TimeZoneId));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
