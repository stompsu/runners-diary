using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>
/// CI mode of the Polish eval cases (ADR 0043 §5, M10 C2): the same cases and fact sheets as English with <c>language: pl</c>; a Polish good
/// draft per model case passes the Polish rules and the case checks; Polish adversarial drafts (number words in several cases and families,
/// forbidden topics with and without diacritics, English words inside Polish text) fail with their rule code; a placeholder in the wrong
/// case is a known limit (valid for the validator); an English draft for <c>pl</c> fails the case's language check, not the validator.
/// </summary>
public sealed class PolishEvalCaseTests
{
    private const string Polish = "pl";

    private static SummaryValidationPolicy PolishPolicy => SummaryValidationPolicy.Default.ForLanguage(Polish);

    private static string PolishFile(string name, string file) => File.ReadAllText(Path.Combine(EvalFiles.CaseDirectory(name, Polish), file));

    [Theory]
    [MemberData(nameof(EvalCases.Names), MemberType = typeof(EvalCases))]
    public void Polish_fact_sheet_is_the_golden_sheet_with_language_pl(string name)
    {
        var english = File.ReadAllText(Path.Combine(EvalFiles.CaseDirectory(name), "fact-sheet.json")).ReplaceLineEndings("\n");
        var polish = EvalCases.Get(name, Polish);

        Assert.Equal(Polish, polish.Sheet.Language);
        Assert.Equal(english.Replace("\"language\": \"en\"", "\"language\": \"pl\"", StringComparison.Ordinal), EvalFiles.Indented(polish.Sheet));
        Assert.Equal(polish.ExpectedTemplate, EvalCases.Get(name).ExpectedTemplate);
    }

    [Theory]
    [MemberData(nameof(EvalCases.ModelNames), MemberType = typeof(EvalCases))]
    public void Polish_good_draft_is_valid_and_meets_the_case_requirements(string name)
    {
        var evalCase = EvalCases.Get(name, Polish);
        var (draft, result) = SummaryValidator.Validate(EvalFiles.GoodDraft(name, Polish), evalCase.Sheet, PolishPolicy);

        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.Empty(EvalAssertions.Failures(evalCase, draft!));
        Assert.Empty(LanguageCheck.EnglishWords(draft!.Texts().Select(t => t.Text)));
    }

    [Theory]
    [MemberData(nameof(EvalFiles.PolishBadDrafts), MemberType = typeof(EvalFiles))]
    public void Polish_adversarial_draft_fails_with_its_rule_code(string name, string file)
    {
        var evalCase = EvalCases.Get(name, Polish);

        var (_, result) = SummaryValidator.Validate(PolishFile(name, file), evalCase.Sheet, PolishPolicy);

        Assert.False(result.IsValid);
        Assert.Contains(EvalFiles.ExpectedCode(file), result.Codes.Select(c => c.ToCode()));
    }

    /// <summary>Without the Polish rules (the v1/v2 English policy) the Polish number words and topics would pass: only code-switched English words are caught.</summary>
    [Theory]
    [MemberData(nameof(EvalFiles.PolishBadDrafts), MemberType = typeof(EvalFiles))]
    public void Polish_adversarial_draft_needs_the_polish_rules_unless_it_code_switches(string name, string file)
    {
        var (_, result) = SummaryValidator.Validate(PolishFile(name, file), EvalCases.Get(name, Polish).Sheet, SummaryValidationPolicy.Default);

        Assert.Equal(!file.Contains("-codeswitch", StringComparison.Ordinal), result.IsValid);
    }

    [Fact]
    public void Polish_adversarial_drafts_cover_number_words_and_forbidden_topics()
    {
        var files = EvalFiles.PolishBadDrafts().Select(row => row.Data.Item2).ToList();

        foreach (var variant in new[] { "cardinal", "genitive", "ordinal", "multiplicative", "compound", "nodiacritics", "codeswitch" })
        {
            Assert.Contains($"bad-literalNumber-{variant}.json", files);
        }

        foreach (var variant in new[] { "bol", "nodiacritics", "lekarz", "ryzyko", "sciegno", "codeswitch" })
        {
            Assert.Contains($"bad-forbiddenTopic-{variant}.json", files);
        }
    }

    /// <summary>ADR 0043 Consequences: a placeholder after a preposition that needs another case ("przed Bieg uliczny") is a style defect the prompt prevents, not a validator rule.</summary>
    [Fact]
    public void A_placeholder_in_the_wrong_case_is_a_known_limit_of_the_validator()
    {
        var evalCase = EvalCases.Get(EvalCases.TaperBeforeRace, Polish);

        var (draft, result) = SummaryValidator.Validate(PolishFile(EvalCases.TaperBeforeRace, "known-limit-placeholderCase.json"), evalCase.Sheet, PolishPolicy);

        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.Contains("przed {week.6.raceType}", draft!.Headline, StringComparison.Ordinal);
    }

    /// <summary>ADR 0043 §5: an English answer to a Polish request passes the validator but fails the case (a prompt-quality metric).</summary>
    [Fact]
    public void An_english_draft_for_pl_passes_the_validator_but_fails_the_language_check()
    {
        var evalCase = EvalCases.Get(EvalCases.TaperBeforeRace, Polish);

        var (draft, result) = SummaryValidator.Validate(PolishFile(EvalCases.TaperBeforeRace, "english-draft.json"), evalCase.Sheet, PolishPolicy);

        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.Contains(EvalAssertions.Failures(evalCase, draft!), f => f.Contains("English words", StringComparison.Ordinal));
        Assert.Empty(EvalAssertions.Failures(EvalCases.Get(EvalCases.TaperBeforeRace), draft!));
    }

    [Theory]
    [MemberData(nameof(EvalCases.ModelNames), MemberType = typeof(EvalCases))]
    public async Task Polish_case_passes_end_to_end_with_its_good_draft_and_the_request_names_the_language(string name)
    {
        var scripted = new ScriptedChatClient().Reply(EvalFiles.GoodDraft(name, Polish), 5500, 900);

        var outcome = await EvalRunner.RunAsync(EvalCases.Get(name, Polish), scripted, "gpt-test");

        Assert.True(outcome.Passed, outcome.ToString());
        Assert.Contains("Language: pl", Assert.Single(scripted.Requests).Messages[0].Text, StringComparison.Ordinal);
        Assert.Equal(3, outcome.Result.Summary!.PromptVersion);
    }

    /// <summary>The pipeline applies the Polish rules to a Polish sheet: a draft with a Polish number word is rejected, the retry passes.</summary>
    [Theory]
    [MemberData(nameof(EvalFiles.PolishBadDrafts), MemberType = typeof(EvalFiles))]
    public async Task Polish_case_recovers_from_an_adversarial_first_attempt(string name, string badFile)
    {
        var scripted = new ScriptedChatClient().Reply(PolishFile(name, badFile)).Reply(EvalFiles.GoodDraft(name, Polish));

        var outcome = await EvalRunner.RunAsync(EvalCases.Get(name, Polish), scripted, "gpt-test");

        Assert.True(outcome.Passed, outcome.ToString());
        Assert.Equal(2, outcome.Result.Summary!.Attempts);
        Assert.Contains(EvalFiles.ExpectedCode(badFile), scripted.Requests[1].Messages[^1].Text, StringComparison.Ordinal);
        Assert.Contains(outcome.Attempts[0].Violations, v => v.Rule.ToCode() == EvalFiles.ExpectedCode(badFile));
    }

    [Fact]
    public async Task An_english_answer_for_pl_succeeds_in_the_pipeline_but_the_eval_case_fails()
    {
        var scripted = new ScriptedChatClient().Reply(PolishFile(EvalCases.TaperBeforeRace, "english-draft.json"));

        var outcome = await EvalRunner.RunAsync(EvalCases.Get(EvalCases.TaperBeforeRace, Polish), scripted, "gpt-test");

        Assert.Equal(Infrastructure.Ai.GenerationStatus.Succeeded, outcome.Result.Status);
        Assert.False(outcome.Passed);
    }
}
