using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Localization;
using RunnersDiary.Domain.Time;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>
/// Fails host startup when <c>seed/demo/training-plan.json</c> is missing or malformed, or its example plan does not validate against the demo diary
/// for today, instead of failing every guest creation later (ADR 0041 §5). Also checks every supported language (ADR 0042 §7): the translated plan must validate,
/// a malformed <c>seed/demo/i18n/&lt;lang&gt;.json</c> fails the host, and seed texts without an entry are logged (and fail the host when
/// <see cref="GuestOptions.RequireCompleteDemoTranslations"/> is on). Runs after <see cref="DemoDatasetStartupCheck"/> (registered after it).
/// Not registered during build-time OpenAPI generation or in job mode.
/// </summary>
internal sealed partial class DemoTrainingPlanStartupCheck(
    DemoDatasetLoader loader, DemoTrainingPlan plan, DemoTextLocalizer localizer, IOptions<GuestOptions> options, TimeProvider clock, ILogger<DemoTrainingPlanStartupCheck> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dataset = loader.Get();
        var today = UserToday.Get(clock, dataset.Template.TimeZoneId);
        var now = clock.GetUtcNow();
        foreach (var language in SupportedLanguages.All)
        {
            var runs = Domain.Demo.DemoScheduleExpander.Expand(dataset.Template, today).Select(r => localizer.Localize(r, language)).ToList();
            plan.Build(Guid.CreateVersion7(), runs, today, now, language);

            var missing = localizer.MissingTexts(dataset.Template, plan.Template, language);
            if (missing.Count == 0)
            {
                continue;
            }

            if (options.Value.RequireCompleteDemoTranslations)
            {
                throw new InvalidOperationException(
                    $"seed/demo/i18n/{language}.json lacks {missing.Count} demo texts (first: \"{missing[0]}\").");
            }

            LogIncomplete(language, missing.Count);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo translations for '{Language}' are incomplete: {Count} seed texts fall back to English.")]
    private partial void LogIncomplete(string language, int count);
}
