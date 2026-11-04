using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Ai.Tests.Demo;

/// <summary>The startup check (ADR 0028 §5): catalogs load and every demo fixture holds for the pristine demo data today.</summary>
public sealed class AiStartupCheckTests
{
    [Fact]
    public async Task Startup_check_passes_with_the_shipped_files()
    {
        var prompts = PromptCatalog.Load(RepoPaths.PromptsInOutput);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var loader = new DemoDatasetLoader(Options.Create(new GuestOptions { DemoDataPath = RepoPaths.DemoSeed }), clock);
        var check = new AiStartupCheck(prompts, TemplateCatalog.Load(prompts), DemoSummaryCatalog.Load(prompts), loader, clock);

        await check.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_fixture_whose_claim_does_not_hold_fails_startup()
    {
        var prompts = PromptCatalog.Load(RepoPaths.PromptsInOutput);
        var fixture = DemoSummaryCatalog.Load(prompts).Fixtures.Single();
        var broken = fixture with { Requires = [.. fixture.Requires, new FactPredicate("races.completedCount", PredicateOperator.Gte, FactValue.Of(99))] };
        var template = DemoTemplateParser.Parse(File.ReadAllText(Path.Combine(RepoPaths.DemoSeed, "runs.json")));

        var error = Assert.Throws<InvalidOperationException>(() => AiStartupCheck.CheckDemoFixture(broken, template, new DateOnly(2026, 11, 4), prompts.Current.Policy));
        Assert.Contains("races.completedCount", error.Message, StringComparison.Ordinal);
    }
}
