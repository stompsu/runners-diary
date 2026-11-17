using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RunnersDiary.Infrastructure.Goals;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>M10 B4 (ADR 0042 §7): the committed <c>seed/demo/i18n/pl.json</c> is complete and keeps the numbers of the English source; offline.</summary>
public sealed partial class DemoPolishMapTests
{
    private static readonly IOptions<GuestOptions> Options = Microsoft.Extensions.Options.Options.Create(new GuestOptions());

    private static (DemoTextLocalizer Localizer, DemoDatasetTemplates Templates) Load()
    {
        var localizer = new DemoTextLocalizer(Options);
        var runs = DemoDatasetLoader.Load(Options.Value.DemoDataPath, DateTimeOffset.UtcNow).Template;
        var plan = new DemoTrainingPlan(Options, localizer).Template;
        return (localizer, new DemoDatasetTemplates(runs, plan));
    }

    private sealed record DemoDatasetTemplates(RunnersDiary.Domain.Demo.DemoTemplate Runs, RunnersDiary.Domain.Goals.Planning.DemoTrainingPlanTemplate Plan);

    [Fact]
    public void The_committed_polish_map_exists_and_covers_every_required_text()
    {
        var (localizer, t) = Load();

        Assert.True(localizer.HasMap("pl"));
        Assert.Empty(localizer.MissingTexts(t.Runs, t.Plan, "pl"));
    }

    [Fact]
    public void Every_polish_text_keeps_the_numbers_of_the_english_source()
    {
        var (localizer, t) = Load();
        var required = DemoTextLocalizer.RequiredTexts(t.Runs, t.Plan);
        var titles = t.Runs.Runs.Select(r => r.Title).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!);

        foreach (var english in required.Concat(titles).Distinct(StringComparer.Ordinal))
        {
            var polish = localizer.Translate(english, "pl")!;
            Assert.Equal(Numbers(english), Numbers(polish));
        }
    }

    [Fact]
    public void Polish_texts_are_not_left_in_english()
    {
        var (localizer, t) = Load();
        var untranslated = DemoTextLocalizer.RequiredTexts(t.Runs, t.Plan).Where(e => localizer.Translate(e, "pl") == e).ToList();

        Assert.Empty(untranslated);
    }

    private static List<string> Numbers(string text) => [.. NumberPattern().Matches(text).Select(m => m.Value.Replace(',', '.')).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();
}
