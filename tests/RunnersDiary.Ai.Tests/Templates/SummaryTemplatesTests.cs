using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Insights.Narrative;
using static RunnersDiary.Ai.Tests.Support.Training;

namespace RunnersDiary.Ai.Tests.Templates;

/// <summary>ADR 0026 §4: empty, wholly future and &lt; 3-session periods get a deterministic template.</summary>
public sealed class SummaryTemplatesTests
{
    private static readonly SummaryTemplateSet English =
        SummaryTemplateSet.Parse(File.ReadAllText(Path.Combine(RepoPaths.Prompts, "templates", "en.json")));

    [Fact]
    public void Wholly_future_period_is_planned_only_even_with_history()
    {
        var log = new Training().Weeks(D("2026-01-05"), 10);
        Assert.Equal(SummaryTemplateKind.PlannedOnly, SummaryTemplates.Select(log.Report(D("2026-06-01"), D("2026-06-30"), D("2026-05-31"))));
    }

    [Fact]
    public void Empty_period_and_rest_only_period_are_empty()
    {
        var log = new Training().Weeks(D("2026-01-05"), 4);
        Assert.Equal(SummaryTemplateKind.Empty, SummaryTemplates.Select(log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30"))));

        log.Run(D("2026-03-10"), "rest", null, null, null);
        Assert.Equal(SummaryTemplateKind.Empty, SummaryTemplates.Select(log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30"))));
    }

    [Theory]
    [InlineData(1, SummaryTemplateKind.FewSessions)]
    [InlineData(2, SummaryTemplateKind.FewSessions)]
    [InlineData(3, null)]
    public void Fewer_than_three_sessions_is_few_sessions(int sessions, SummaryTemplateKind? expected)
    {
        var log = new Training();
        for (var i = 0; i < sessions; i++)
        {
            log.Run(D("2026-03-02").AddDays(i), "easy", 8000, 2880, 3);
        }

        Assert.Equal(expected, SummaryTemplates.Select(log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30"))));
    }

    [Fact]
    public void Partial_period_on_its_first_day_is_not_planned_only()
    {
        var log = new Training().Weeks(D("2026-01-05"), 13);
        var report = log.Report(D("2026-04-01"), D("2026-04-30"), D("2026-04-01"));
        Assert.Equal(1, report.Period.ElapsedDays);
        Assert.NotEqual(SummaryTemplateKind.PlannedOnly, SummaryTemplates.Select(report));
    }

    [Fact]
    public void English_file_has_every_template()
    {
        Assert.Equal("en", English.Language);
        foreach (var kind in Enum.GetValues<SummaryTemplateKind>())
        {
            var variants = English.Variants(kind);
            Assert.NotEmpty(variants);
            Assert.Empty(variants[^1].When);
        }
    }

    [Theory]
    [InlineData(EvalCases.EmptyMonth, SummaryTemplateKind.Empty)]
    [InlineData(EvalCases.WhollyFuture, SummaryTemplateKind.PlannedOnly)]
    public void Template_validates_and_resolves_against_its_case(string name, SummaryTemplateKind kind)
    {
        var sheet = EvalCases.Get(name).Sheet;
        var draft = English.Get(kind, sheet);

        var result = SummaryValidator.Validate(draft, sheet, SummaryValidationPolicy.Default);
        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.NotEmpty(SummaryValues.Resolve(draft, sheet).Values);
    }

    /// <summary>The few-sessions wording follows the facts: "so far" only for a partial period, "session" for exactly one.</summary>
    [Theory]
    [InlineData(1, true, "Only one session so far between", "session covering")]
    [InlineData(2, true, "Only a few sessions so far between", "sessions covering")]
    [InlineData(1, false, "Only one session between", "session covering")]
    [InlineData(2, false, "Only a few sessions between", "sessions covering")]
    public void Few_sessions_template_picks_the_wording_for_the_count_and_partial_period(int sessions, bool partial, string headline, string item)
    {
        var log = new Training();
        for (var i = 0; i < sessions; i++)
        {
            log.Run(D("2026-03-02").AddDays(2 * i), "easy", 8000, 2880, 3);
        }

        var report = log.Report(D("2026-03-01"), D("2026-03-31"), partial ? D("2026-03-10") : D("2026-04-30"));
        var sheet = FactSheetBuilder.Build(report, "en");
        Assert.Equal(SummaryTemplateKind.FewSessions, SummaryTemplates.Select(report));
        Assert.Equal(partial, sheet.Flag("period.isPartial"));

        var draft = English.Get(SummaryTemplateKind.FewSessions, sheet);

        var result = SummaryValidator.Validate(draft, sheet, SummaryValidationPolicy.Default);
        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.StartsWith(headline, draft.Headline, StringComparison.Ordinal);
        Assert.Contains("{dq.completedSessions} " + item, Assert.Single(draft.WentWell).Text, StringComparison.Ordinal);
        Assert.Equal(sessions, SummaryValues.Resolve(draft, sheet).Values["dq.completedSessions"].Number);
    }

    [Theory]
    [InlineData(true, "No completed sessions between {period.from} and {period.to} yet.")]
    [InlineData(false, "No completed sessions between {period.from} and {period.to}.")]
    public void Empty_template_says_yet_only_for_a_partial_period(bool partial, string headline)
    {
        var log = new Training().Weeks(D("2026-01-05"), 7);
        var report = log.Report(D("2026-03-01"), D("2026-03-31"), partial ? D("2026-03-20") : D("2026-04-10"));
        var sheet = FactSheetBuilder.Build(report, "en");
        Assert.Equal(SummaryTemplateKind.Empty, SummaryTemplates.Select(report));

        var draft = English.Get(SummaryTemplateKind.Empty, sheet);

        Assert.Equal(headline, draft.Headline);
        Assert.True(SummaryValidator.Validate(draft, sheet, SummaryValidationPolicy.Default).IsValid);
    }

    [Fact]
    public void A_variant_list_must_end_with_an_unconditional_variant_and_a_single_draft_still_parses()
    {
        const string draft = """{ "headline": "x {period.from}", "wentWell": [], "watchOuts": [], "suggestion": { "text": "y" }, "dataNotes": [] }""";
        var conditionalOnly = $$"""{ "version": 1, "language": "en", "templates": { "empty": [ { "when": [ { "key": "period.isPartial", "op": "eq", "value": true } ], "summary": {{draft}} } ], "plannedOnly": {{draft}}, "fewSessions": {{draft}} } }""";
        Assert.Throws<DomainRuleException>(() => SummaryTemplateSet.Parse(conditionalOnly));

        var unknownMember = $$"""{ "version": 1, "language": "en", "templates": { "empty": [ { "if": [], "summary": {{draft}} } ], "plannedOnly": {{draft}}, "fewSessions": {{draft}} } }""";
        Assert.Throws<DomainRuleException>(() => SummaryTemplateSet.Parse(unknownMember));

        var single = SummaryTemplateSet.Parse($$"""{ "version": 1, "language": "en", "templates": { "empty": {{draft}}, "plannedOnly": {{draft}}, "fewSessions": {{draft}} } }""");
        Assert.Empty(Assert.Single(single.Variants(SummaryTemplateKind.Empty)).When);
    }

    [Fact]
    public void A_template_file_missing_a_kind_is_rejected()
    {
        const string json = """{ "version": 1, "language": "en", "templates": { "empty": { "headline": "x", "wentWell": [], "watchOuts": [], "suggestion": null, "dataNotes": [] } } }""";
        Assert.Throws<DomainRuleException>(() => SummaryTemplateSet.Parse(json));
    }
}
