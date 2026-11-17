using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Plans;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>
/// ADR 0043 §5 (M10 C2): with <c>Ai:UseFake</c> the rule-based fake answers a Polish request in Polish, for summaries and plans, and the answer
/// passes the Polish rules through the real pipeline; an English request is answered exactly as before.
/// </summary>
public sealed class PolishFakeModelTests
{
    private static IEnumerable<FactSheet> AllSheets(string language) =>
        EvalCases.All.Select(c => EvalCases.Get(c.Name, language).Sheet)
            .Append(FactSheetBuilder.Build(DemoFacts.TwelveWeeks(new DateOnly(2026, 11, 4)), language));

    [Fact]
    public async Task The_fake_writes_a_polish_summary_that_passes_the_polish_rules_for_every_model_case()
    {
        foreach (var evalCase in EvalCases.All.Where(c => c.ExpectedTemplate is null).Select(c => c.InLanguage("pl")))
        {
            var harness = new AiHarness(new RuleBasedFakeChatClient(), deployment: ChatClientFactory.FakeDeployment);

            var result = await harness.Generate(evalCase.Sheet);

            Assert.True(result.Status == GenerationStatus.Succeeded, $"{evalCase}: {result.Status} {result.Failure}");
            Assert.Equal(1, result.Summary!.Attempts);
            Assert.Empty(LanguageCheck.EnglishWords(result.Summary.Draft.Texts().Select(t => t.Text)));
        }
    }

    /// <summary>Every Polish text of the fake is used by some sheet and valid on its own; nothing of it reaches an English sheet.</summary>
    [Fact]
    public void Every_polish_fake_text_is_reached_and_valid_and_english_sheets_get_the_english_texts()
    {
        var polishTexts = typeof(FakeSummaryTexts).GetProperties().Where(p => p.PropertyType == typeof(string))
            .Select(p => (string)p.GetValue(FakeSummaryTexts.Polish)!).ToHashSet(StringComparer.Ordinal);
        var englishTexts = typeof(FakeSummaryTexts).GetProperties().Where(p => p.PropertyType == typeof(string))
            .Select(p => (string)p.GetValue(FakeSummaryTexts.English)!).ToHashSet(StringComparer.Ordinal);
        var policy = SummaryValidationPolicy.Default.ForLanguage("pl");

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sheet in AllSheets("pl"))
        {
            var draft = RuleBasedFakeChatClient.Write(sheet);
            used.UnionWith(draft.Texts().Select(t => t.Text));
            Assert.True(SummaryValidator.Validate(draft, sheet, policy).IsValid || sheet.Number("totals.runCount") is null or 0);
        }

        Assert.Equal(polishTexts.Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
        Assert.Empty(LanguageCheck.EnglishWords(polishTexts));

        foreach (var sheet in AllSheets("en"))
        {
            Assert.Subset(englishTexts, RuleBasedFakeChatClient.Write(sheet).Texts().Select(t => t.Text).ToHashSet(StringComparer.Ordinal));
        }

        Assert.Equal("Keep easy days easy and build load gradually.", FakeSummaryTexts.English.Suggestion);
    }

    [Theory]
    [MemberData(nameof(PlanEvalCases.Names), MemberType = typeof(PlanEvalCases))]
    public async Task The_fake_plan_in_polish_passes_the_pipeline_with_the_same_sessions_as_in_english(string name)
    {
        var c = PlanEvalCases.Get(name);
        var polishSheet = c.Sheet with { Language = "pl" };
        var harness = new PlanHarness(new RuleBasedFakeChatClient(), deployment: ChatClientFactory.FakeDeployment);

        var result = await harness.Generate(polishSheet);

        Assert.True(result.Status == GenerationStatus.Succeeded, $"{name}: {result.Status} {result.Failure}");
        Assert.Equal(1, result.Attempts);
        c.AssertPlan(result.Plan!, polishSheet);
        Assert.Equal(FakePlanTexts.Polish.Notes, result.Plan!.Notes);

        var english = RuleBasedFakePlanWriter.Write(PlanFactSheetJson.ToJson(c.Sheet));
        var polish = RuleBasedFakePlanWriter.Write(PlanFactSheetJson.ToJson(polishSheet));
        Assert.Equal(english.Sessions.Select(s => s with { Description = "" }), polish.Sessions.Select(s => s with { Description = "" }));
        Assert.Equal(RuleBasedFakePlanWriter.Notes, english.Notes);
        Assert.Empty(LanguageCheck.EnglishWords(polish.Sessions.Select(s => s.Description).Append(polish.Notes)));
        Assert.All(polish.Sessions, s => Assert.True(s.Description.Length <= 120, s.Description));
    }

    [Fact]
    public void Every_polish_plan_text_passes_the_polish_deny_list_and_is_gender_neutral()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.HalfMarathon10Weeks);
        var sheet = c.Sheet with { Language = "pl" };
        var texts = typeof(FakePlanTexts).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string)p.GetValue(FakePlanTexts.Polish)!).ToList();
        var draft = RuleBasedFakePlanWriter.Write(PlanFactSheetJson.ToJson(sheet));

        foreach (var text in texts)
        {
            var probe = draft with { Notes = text };
            Assert.DoesNotContain(TrainingPlanValidator.Validate(probe, sheet, PlanValidationPolicy.Default.ForLanguage("pl")).Violations, v => v.Rule is PlanRuleCode.ForbiddenTopic or PlanRuleCode.Markup or PlanRuleCode.GenderedForm);
        }
    }
}
