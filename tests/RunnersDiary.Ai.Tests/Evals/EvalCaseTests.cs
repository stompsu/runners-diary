using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>
/// CI mode of the eval cases (ADR 0026 §7): golden fact sheets, the committed good draft of each model case passes validation and the
/// case's key requirements, every adversarial draft fails with its rule code, and template cases never need a draft.
/// Regenerate the golden files with <c>UPDATE_GOLDEN=1 dotnet test --project tests/RunnersDiary.Ai.Tests</c>.
/// </summary>
public sealed class EvalCaseTests
{
    [Theory]
    [MemberData(nameof(EvalCases.Names), MemberType = typeof(EvalCases))]
    public void Fact_sheet_matches_the_golden_file(string name)
    {
        var evalCase = EvalCases.Get(name);
        var actual = EvalFiles.Indented(evalCase.Sheet);
        var path = Path.Combine(EvalFiles.CaseDirectory(name), "fact-sheet.json");

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.True(File.Exists(path), $"Missing golden file for {name}; run with UPDATE_GOLDEN=1.");
        var golden = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal(golden, actual);

        // The golden file is the canonical sheet, only indented.
        Assert.Equal(FactSheetJson.ToJson(evalCase.Sheet), FactSheetJson.ToJson(FactSheetJson.Parse(golden)));
    }

    [Theory]
    [MemberData(nameof(EvalCases.Names), MemberType = typeof(EvalCases))]
    public void Template_selection_matches_the_case(string name)
    {
        var evalCase = EvalCases.Get(name);
        Assert.Equal(evalCase.ExpectedTemplate, SummaryTemplates.Select(evalCase.Report));
        Assert.Equal(evalCase.ExpectedTemplate is null, File.Exists(Path.Combine(EvalFiles.CaseDirectory(name), "good-draft.json")));
    }

    [Theory]
    [MemberData(nameof(EvalCases.ModelNames), MemberType = typeof(EvalCases))]
    public void Good_draft_is_valid_and_meets_the_case_requirements(string name)
    {
        var evalCase = EvalCases.Get(name);
        var (draft, result) = SummaryValidator.Validate(EvalFiles.GoodDraft(name), evalCase.Sheet, SummaryValidationPolicy.Default);

        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.Empty(EvalAssertions.Failures(evalCase, draft!));
    }

    [Theory]
    [MemberData(nameof(EvalFiles.BadDrafts), MemberType = typeof(EvalFiles))]
    public void Adversarial_draft_fails_with_its_rule_code(string name, string file)
    {
        var evalCase = EvalCases.Get(name);
        var json = File.ReadAllText(Path.Combine(EvalFiles.CaseDirectory(name), file));

        var (_, result) = SummaryValidator.Validate(json, evalCase.Sheet, SummaryValidationPolicy.Default);

        Assert.False(result.IsValid);
        Assert.Contains(EvalFiles.ExpectedCode(file), result.Codes.Select(c => c.ToCode()));
    }

    [Fact]
    public void Every_rule_code_has_an_adversarial_draft()
    {
        var covered = Directory.GetFiles(EvalFiles.CasesDirectory, "bad-*.json", SearchOption.AllDirectories).Select(f => EvalFiles.ExpectedCode(Path.GetFileName(f))).ToHashSet();
        foreach (var code in Enum.GetValues<ValidationRuleCode>())
        {
            Assert.Contains(code.ToCode(), covered);
        }
    }

    [Fact]
    public void No_case_sheet_contains_user_free_text_and_all_fit_the_size_cap()
    {
        foreach (var evalCase in EvalCases.All)
        {
            var json = FactSheetJson.ToJson(evalCase.Sheet);
            Assert.DoesNotContain("Canary", json, StringComparison.Ordinal);
            Assert.DoesNotContain("M30", json, StringComparison.Ordinal);
            Assert.True(json.Length <= 24_000, $"{evalCase.Name}: {json.Length} chars.");
        }
    }
}
