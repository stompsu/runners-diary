using Microsoft.Extensions.Hosting;
using RunnersDiary.Domain.Time;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>
/// Fails host startup when <c>seed/demo/training-plan.json</c> is missing or malformed, or its example plan does not validate against the demo diary
/// for today, instead of failing every guest creation later (ADR 0041 §5). Runs after <see cref="DemoDatasetStartupCheck"/> (registered after it).
/// Not registered during build-time OpenAPI generation or in job mode.
/// </summary>
internal sealed class DemoTrainingPlanStartupCheck(DemoDatasetLoader loader, DemoTrainingPlan plan, TimeProvider clock) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dataset = loader.Get();
        var today = UserToday.Get(clock, dataset.Template.TimeZoneId);
        plan.Build(Guid.CreateVersion7(), Domain.Demo.DemoScheduleExpander.Expand(dataset.Template, today), today, clock.GetUtcNow());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
