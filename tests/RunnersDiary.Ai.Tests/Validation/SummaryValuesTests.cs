using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;
using static RunnersDiary.Ai.Tests.Support.Training;

namespace RunnersDiary.Ai.Tests.Validation;

/// <summary>Placeholders resolve from the fact sheet (grounding by construction) and the stored content round-trips.</summary>
public sealed class SummaryValuesTests
{
    [Fact]
    public void Every_cited_key_is_resolved_with_its_unit()
    {
        var sheet = FactSheetBuilder.Build(DemoFacts.TwelveWeeks(D("2026-11-04")), "en");
        var draft = new SummaryDraft(
            "Peak {acwr.peak.value} after your {acwr.peak.raceType}.",
            [new SummaryItem("You ran {totals.distance} by {period.asOf}; PB: {race.1.isPersonalBest}.")],
            [],
            null,
            []);

        var content = SummaryValues.Resolve(draft, sheet);

        Assert.Equal(["acwr.peak.value", "acwr.peak.raceType"], content.Headline.Keys);
        Assert.Equal(new SummaryValue(1.79, null, FactUnit.Ratio), content.Values["acwr.peak.value"]);
        Assert.Equal(new SummaryValue(null, "trailRace", FactUnit.RunType), content.Values["acwr.peak.raceType"]);
        Assert.Equal(new SummaryValue(382593, null, FactUnit.Meters), content.Values["totals.distance"]);
        Assert.Equal(new SummaryValue(null, "2026-11-04", FactUnit.Date), content.Values["period.asOf"]);
        Assert.Equal(new SummaryValue(null, "true", FactUnit.Flag), content.Values["race.1.isPersonalBest"]);
        Assert.Equal(5, content.Values.Count);
    }

    [Fact]
    public void Content_json_round_trips_and_uses_the_contract_shape()
    {
        var sheet = FactSheetBuilder.Build(DemoFacts.TwelveWeeks(D("2026-11-04")), "en");
        var draft = SummaryDraftJson.Parse(File.ReadAllText(Path.Combine(RepoPaths.TestsDirectory, "Evals", "cases", "race-week-peak-demo", "good-draft.json")));
        var content = SummaryValues.Resolve(draft, sheet);

        var json = SummaryContentJson.ToJson(content);
        var back = SummaryContentJson.Parse(json);

        Assert.Equal(json, SummaryContentJson.ToJson(back));
        Assert.Contains("\"values\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"acwr.peak.value\":{\"number\":1.79,\"text\":null,\"unit\":\"ratio\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"keys\":[\"totals.distance\",\"totals.runCount\",\"comparison.delta.distance.percent\"]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Oversized_content_is_refused()
    {
        var sheet = new FactSheet(1, 1, "en", [new Fact("a.b", FactValue.Of(1), FactUnit.Count, "1")]);
        var big = new SummaryDraft(new string('x', 20_000), [], [], null, []);
        Assert.Throws<InvalidOperationException>(() => SummaryContentJson.ToJson(SummaryValues.Resolve(big, sheet)));
    }

    [Fact]
    public void Resolving_an_unknown_key_is_a_bug()
    {
        var sheet = new FactSheet(1, 1, "en", [new Fact("a.b", FactValue.Of(1), FactUnit.Count, "1")]);
        Assert.Throws<InvalidOperationException>(() => SummaryValues.Resolve(new SummaryDraft("{c.d}", [], [], null, []), sheet));
    }
}
