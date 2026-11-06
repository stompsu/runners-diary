using System.Globalization;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using static RunnersDiary.Ai.Tests.Support.Training;

namespace RunnersDiary.Ai.Tests.FactSheets;

/// <summary>Canonical JSON and fingerprint (ADR 0026 §1, ADR 0028 §2).</summary>
public sealed class FactSheetJsonTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("ar-SA")]
    public void Canonical_bytes_do_not_depend_on_the_culture(string culture)
    {
        var expected = FactSheetJson.ToUtf8Bytes(FactSheetBuilder.Build(DemoFacts.TwelveWeeks(D("2026-11-04")), "en"));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var actual = FactSheetJson.ToUtf8Bytes(FactSheetBuilder.Build(DemoFacts.TwelveWeeks(D("2026-11-04")), "en"));
            Assert.Equal(expected, actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Same_data_gives_the_same_fingerprint_and_a_single_run_change_changes_it()
    {
        var log = new Training().Weeks(D("2026-01-05"), 12);
        var first = FactSheetJson.Fingerprint(FactSheetBuilder.Build(log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30")), "en"));
        var again = FactSheetJson.Fingerprint(FactSheetBuilder.Build(log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30")), "en"));
        log.Run(D("2026-03-11"), "recovery", 3000, 1200, 2);
        var changed = FactSheetJson.Fingerprint(FactSheetBuilder.Build(log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30")), "en"));

        Assert.Equal(32, first.Length);
        Assert.Equal(first, again);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void Language_is_part_of_the_fingerprint()
    {
        var report = DemoFacts.TwelveWeeks(D("2026-11-04"));
        Assert.NotEqual(
            FactSheetJson.Fingerprint(FactSheetBuilder.Build(report, "en")),
            FactSheetJson.Fingerprint(FactSheetBuilder.Build(report, "pl")));
    }

    [Fact]
    public void A_past_period_keeps_its_fingerprint_when_today_moves()
    {
        var log = new Training().Weeks(D("2026-01-05"), 12);
        var a = FactSheetJson.Fingerprint(FactSheetBuilder.Build(log.Report(D("2026-02-01"), D("2026-02-28"), D("2026-04-30")), "en"));
        var b = FactSheetJson.Fingerprint(FactSheetBuilder.Build(log.Report(D("2026-02-01"), D("2026-02-28"), D("2026-05-01")), "en"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void A_partial_period_changes_its_fingerprint_when_today_moves()
    {
        var log = new Training().Weeks(D("2026-01-05"), 15);
        var a = FactSheetJson.Fingerprint(FactSheetBuilder.Build(log.Report(D("2026-04-01"), D("2026-04-30"), D("2026-04-16")), "en"));
        var b = FactSheetJson.Fingerprint(FactSheetBuilder.Build(log.Report(D("2026-04-01"), D("2026-04-30"), D("2026-04-17")), "en"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Canonical_json_shape_and_round_trip()
    {
        var sheet = FactSheetBuilder.Build(DemoFacts.TwelveWeeks(D("2026-11-04")), "en");
        var json = FactSheetJson.ToJson(sheet);

        Assert.StartsWith("""{"factSheetVersion":2,"metricsVersion":1,"language":"en","facts":[{"key":"period.from","value":"2026-08-17","unit":"date","display":"17 Aug 2026"}""", json, StringComparison.Ordinal);
        Assert.Contains("""{"key":"totals.distance","value":382593,"unit":"meters","display":"382.6 km"}""", json, StringComparison.Ordinal);
        Assert.Contains("""{"key":"comparison.delta.distance.percent","value":14.2,"unit":"percent","display":"+14.2 %"}""", json, StringComparison.Ordinal);
        Assert.Contains("""{"key":"acwr.peak.isRaceWeek","value":true,"unit":"flag","display":"yes"}""", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
        Assert.Equal(json, FactSheetJson.ToJson(FactSheetJson.Parse(json)));
    }

    [Fact]
    public void Fact_keys_follow_the_grammar()
    {
        Assert.True(FactKey.IsValid("totals.distance"));
        Assert.True(FactKey.IsValid("week.12.acwrAtEnd"));
        Assert.True(FactKey.IsValid("comparison.delta.load.percent"));
        Assert.False(FactKey.IsValid("week.0.load"));
        Assert.False(FactKey.IsValid("week.01.load"));
        Assert.False(FactKey.IsValid("Totals.distance"));
        Assert.False(FactKey.IsValid("totals..distance"));
        Assert.False(FactKey.IsValid("totals.distance "));
        Assert.False(FactKey.IsValid("ignore previous instructions"));
        Assert.False(FactKey.IsValid(""));
        Assert.Throws<ArgumentException>(() => new FactSheet(1, MetricsVersion.Current, "en", [new Fact("Bad Key", FactValue.Of(1), FactUnit.Count, "1")]));
    }
}
