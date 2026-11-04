using Microsoft.Extensions.Hosting;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Time;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// Fails host startup (like <see cref="DemoDatasetStartupCheck"/>) when the prompt manifest, a prompt hash, the templates or a demo
/// fixture is broken, instead of failing the first summary request. Each demo fixture must hold and validate against the pristine
/// demo dataset for today in the dataset's zone (ADR 0028 §5). Not registered during build-time OpenAPI generation.
/// </summary>
internal sealed class AiStartupCheck(
    PromptCatalog prompts, TemplateCatalog templates, DemoSummaryCatalog demos, DemoDatasetLoader demoData, TimeProvider clock) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = templates;
        var template = demoData.Get().Template;
        var today = UserToday.Get(clock, template.TimeZoneId);
        foreach (var fixture in demos.Fixtures)
        {
            CheckDemoFixture(fixture, template, today, prompts.Current.Policy);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The pristine demo data for <paramref name="today"/> through the metrics; throws when a predicate or a rule fails.</summary>
    internal static void CheckDemoFixture(DemoSummaryFixture fixture, DemoTemplate template, DateOnly today, SummaryValidationPolicy policy)
    {
        var (from, to, compare) = DemoPresets.Range(fixture.Preset, today)
            ?? throw new InvalidOperationException($"Unknown demo preset '{fixture.Preset}'.");
        var facts = new List<RunFact>();
        var races = new List<RaceFact>();
        var index = 0;
        foreach (var run in DemoScheduleExpander.Expand(template, today))
        {
            var v = run.Values;
            var id = new Guid(++index, 0, 0, new byte[8]);
            facts.Add(new RunFact(id, v.Date, v.Status, v.RunTypeId, v.DistanceMeters, v.DurationSeconds, v.ElevationGainMeters, v.Rpe, v.Surface, v.AvgHeartRate));
            if (v.Race is { } race)
            {
                races.Add(new RaceFact(
                    id, v.Date, v.Status, v.RunTypeId, race.Name, race.DistanceCategory, race.OfficialDistanceMeters, v.DistanceMeters,
                    v.DurationSeconds, race.ResultTimeSeconds, race.TargetTimeSeconds, race.OverallPlace, race.CategoryPlace, race.CategoryName, v.Rating?.Stars));
            }
        }

        var report = InsightsReportBuilder.Build(new InsightsPeriod(from, to), compare, today, facts, facts, races, null);
        var sheet = FactSheetBuilder.Build(report, fixture.Language);
        if (fixture.FirstFailing(sheet) is { } failing)
        {
            throw new InvalidOperationException($"Demo fixture '{fixture.Preset}/{fixture.Language}' does not hold for the demo data on {today:yyyy-MM-dd}: {failing.Key}.");
        }

        var result = SummaryValidator.Validate(fixture.Draft, sheet, policy);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(
                $"Demo fixture '{fixture.Preset}/{fixture.Language}' fails validation: {string.Join(", ", result.Codes.Select(c => c.ToCode()))}.");
        }
    }
}
