using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Ai.Tests.Support.Training;

namespace RunnersDiary.Ai.Tests.Demo;

/// <summary>The hand-written <c>12w-previous</c> example summary (ADR 0028 §5, plan AC10).</summary>
public sealed class DemoSummaryFixtureTests
{
    private static readonly DemoSummaryFixture Fixture =
        DemoSummaryFixture.Parse(File.ReadAllText(Path.Combine(RepoPaths.Prompts, "demo", "en", "12w-previous.v1.json")));

    private static readonly DemoSummaryFixture PolishFixture =
        DemoSummaryFixture.Parse(File.ReadAllText(Path.Combine(RepoPaths.Prompts, "demo", "pl", "12w-previous.v1.json")));

    [Theory]
    [MemberData(nameof(DemoFacts.EveryDayOf2028), MemberType = typeof(DemoFacts))]
    public void Predicates_hold_and_the_fixture_validates_for_every_today(DateOnly today)
    {
        var sheet = FactSheetBuilder.Build(DemoFacts.TwelveWeeks(today), "en");

        Assert.Null(Fixture.FirstFailing(sheet));
        var result = SummaryValidator.Validate(Fixture.Draft, sheet, SummaryValidationPolicy.Default);
        Assert.True(result.IsValid, string.Join(", ", result.Violations));

        // Every placeholder resolves from the guest's own sheet.
        var content = SummaryValues.Resolve(Fixture.Draft, sheet);
        Assert.Equal(sheet.Number("totals.distance"), content.Values["totals.distance"].Number);
    }

    [Fact]
    public void Fixture_is_the_default_dashboard_preset_in_english()
    {
        Assert.Equal(DemoPresets.TwelveWeeksPrevious, Fixture.Preset);
        Assert.Equal("en", Fixture.Language);
        Assert.Equal(FactSheet.CurrentVersion, Fixture.FactSheetVersion);
        Assert.NotEmpty(Fixture.Requires);
    }

    [Fact]
    public void Preset_range_matches_the_spa_last_12_weeks()
    {
        var today = D("2026-11-04");
        Assert.True(DemoPresets.Matches(DemoPresets.TwelveWeeksPrevious, today, D("2026-08-17"), D("2026-11-08"), ComparisonKind.Previous));
        Assert.False(DemoPresets.Matches(DemoPresets.TwelveWeeksPrevious, today, D("2026-08-17"), D("2026-11-08"), ComparisonKind.None));
        Assert.False(DemoPresets.Matches(DemoPresets.TwelveWeeksPrevious, today, D("2026-08-10"), D("2026-11-08"), ComparisonKind.Previous));
        Assert.Null(DemoPresets.Range("this-month", today));
    }

    [Fact]
    public void Fixture_disappears_when_the_guest_breaks_a_claim()
    {
        var today = D("2026-11-04");
        var (facts, races) = DemoFacts.Facts(today);
        var (from, to, compare) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;

        // Delete the spike week's long run: week 1 is no longer "high".
        var spikeLong = facts.Single(f => f.Date == from.AddDays(6) && f.Status == RunStatus.Completed && f.RunTypeId == 22);
        var edited = facts.Where(f => f != spikeLong).ToList();
        var sheet = FactSheetBuilder.Build(InsightsReportBuilder.Build(new InsightsPeriod(from, to), compare, today, edited, edited, races, null), "en");

        Assert.False(Fixture.Holds(sheet));
        Assert.Equal("week.1.acwrBand", Fixture.FirstFailing(sheet)!.Key);
    }

    [Fact]
    public void Fixture_disappears_when_the_half_marathon_was_not_a_personal_best()
    {
        var today = D("2026-11-04");
        var (facts, races) = DemoFacts.Facts(today);
        var (from, to, compare) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;
        var half = races.Single(r => r.Category == RaceDistanceCategory.HalfMarathon && r.Status == RunStatus.Completed && r.Date >= from && r.Date <= to);
        var slower = races.Select(r => r == half ? r with { ResultTimeSeconds = 99_999 } : r).ToList();
        // An earlier, faster half marathon makes the period's race no longer a PB.
        slower.Add(half with { Id = Guid.NewGuid(), Date = from.AddDays(-200), ResultTimeSeconds = 6000 });

        var sheet = FactSheetBuilder.Build(InsightsReportBuilder.Build(new InsightsPeriod(from, to), compare, today, facts, facts, slower, null), "en");

        Assert.False(Fixture.Holds(sheet));
    }

    [Fact]
    public void Predicate_operators()
    {
        var sheet = new FactSheet(1, 1, "en",
        [
            new Fact("a.n", FactValue.Of(2), FactUnit.Count, "2"),
            new Fact("a.t", FactValue.Of("high"), FactUnit.Band, "high"),
            new Fact("a.d", FactValue.Of(D("2026-01-02")), FactUnit.Date, "2 Jan 2026"),
            new Fact("a.f", FactValue.Of(true), FactUnit.Flag, "yes"),
        ]);

        Assert.True(new FactPredicate("a.n", PredicateOperator.Eq, FactValue.Of(2)).Holds(sheet));
        Assert.True(new FactPredicate("a.n", PredicateOperator.Gt, FactValue.Of(1)).Holds(sheet));
        Assert.True(new FactPredicate("a.n", PredicateOperator.Gte, FactValue.Of(2)).Holds(sheet));
        Assert.False(new FactPredicate("a.n", PredicateOperator.Lt, FactValue.Of(2)).Holds(sheet));
        Assert.True(new FactPredicate("a.n", PredicateOperator.Lte, FactValue.Of(2)).Holds(sheet));
        Assert.True(new FactPredicate("a.t", PredicateOperator.Eq, FactValue.Of("high")).Holds(sheet));
        Assert.False(new FactPredicate("a.t", PredicateOperator.Eq, FactValue.Of("low")).Holds(sheet));
        Assert.True(new FactPredicate("a.d", PredicateOperator.Lt, FactValue.Of(D("2026-02-01"))).Holds(sheet));
        Assert.True(new FactPredicate("a.f", PredicateOperator.Eq, FactValue.Of(true)).Holds(sheet));
        Assert.False(new FactPredicate("a.f", PredicateOperator.Gt, FactValue.Of(false)).Holds(sheet));
        Assert.True(new FactPredicate("a.f", PredicateOperator.Exists, null).Holds(sheet));
        Assert.False(new FactPredicate("a.missing", PredicateOperator.Exists, null).Holds(sheet));
        Assert.False(new FactPredicate("a.n", PredicateOperator.Eq, FactValue.Of("2")).Holds(sheet));
    }

    // ---- Polish (ADR 0043 §4) ------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(DemoFacts.EveryDayOf2028), MemberType = typeof(DemoFacts))]
    public void Polish_fixture_holds_and_validates_with_the_polish_rules_for_every_today(DateOnly today)
    {
        var sheet = FactSheetBuilder.Build(DemoFacts.TwelveWeeks(today), "pl");

        Assert.Null(PolishFixture.FirstFailing(sheet));
        var result = SummaryValidator.Validate(PolishFixture.Draft, sheet, SummaryValidationPolicy.Default.ForLanguage("pl"));
        Assert.True(result.IsValid, string.Join(", ", result.Violations));
    }

    /// <summary>Same preset, same claims (predicates) and the same cited keys per section as the English fixture; written in Polish.</summary>
    [Fact]
    public void Polish_fixture_makes_the_same_claims_as_the_english_one()
    {
        Assert.Equal((DemoPresets.TwelveWeeksPrevious, "pl", Fixture.Version, FactSheet.CurrentVersion), (PolishFixture.Preset, PolishFixture.Language, PolishFixture.Version, PolishFixture.FactSheetVersion));
        Assert.Equal(Fixture.Requires, PolishFixture.Requires);
        Assert.Equal(Keys(Fixture.Draft), Keys(PolishFixture.Draft));
        Assert.Empty(LanguageCheck.EnglishWords(PolishFixture.Draft.Texts().Select(t => t.Text)));

        static List<string> Keys(SummaryDraft draft) =>
            [.. draft.Texts().Select(t => t.Location.Split('.')[0] + ":" + string.Join(",", Placeholders.Scan(t.Text).Keys.Order(StringComparer.Ordinal)))];
    }
}

