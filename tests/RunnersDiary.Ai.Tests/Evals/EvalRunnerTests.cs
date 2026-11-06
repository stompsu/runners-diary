using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>Plan AC5, CI mode: every model case passes end to end against the scripted fake, also after one rejected attempt.</summary>
public sealed class EvalRunnerTests
{
    [Theory]
    [MemberData(nameof(EvalCases.ModelNames), MemberType = typeof(EvalCases))]
    public async Task Case_passes_with_its_good_draft(string name)
    {
        var scripted = new ScriptedChatClient().Reply(EvalFiles.GoodDraft(name), 5500, 700);
        var outcome = await EvalRunner.RunAsync(EvalCases.Get(name), scripted, "gpt-test");

        Assert.True(outcome.Passed, outcome.ToString());
        Assert.Equal(1, scripted.CallCount);
        Assert.Equal(5500, outcome.InputTokens);
        Assert.InRange(outcome.EstimatedCostUsd, 0.007m, 0.009m);
    }

    [Theory]
    [MemberData(nameof(EvalFiles.BadDrafts), MemberType = typeof(EvalFiles))]
    public async Task Case_recovers_from_an_adversarial_first_attempt(string name, string badFile)
    {
        var scripted = new ScriptedChatClient()
            .Reply(File.ReadAllText(Path.Combine(EvalFiles.CaseDirectory(name), badFile)))
            .Reply(EvalFiles.GoodDraft(name));

        var outcome = await EvalRunner.RunAsync(EvalCases.Get(name), scripted, "gpt-test");

        Assert.True(outcome.Passed, outcome.ToString());
        Assert.Equal(2, outcome.Result.Summary!.Attempts);
        Assert.Contains(EvalFiles.ExpectedCode(badFile), scripted.Requests[1].Messages[^1].Text, StringComparison.Ordinal);

        // The live eval's per-attempt detail: the rejected draft with its rule code, then the valid one.
        Assert.Equal(2, outcome.Attempts.Count);
        Assert.Contains(outcome.Attempts[0].Violations, v => v.Rule.ToCode() == EvalFiles.ExpectedCode(badFile));
        Assert.Equal("valid", outcome.Attempts[1].Verdict);
        Assert.Contains("attempt 1: ", outcome.Details(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Taper_case_flags_a_draft_that_calls_the_taper_a_watch_out()
    {
        var evalCase = EvalCases.Get(EvalCases.TaperBeforeRace);
        const string draft = """
            {"headline":"A build to your {race.1.category}.","wentWell":[{"text":"You ran the race in {race.1.resultTime}."}],
             "watchOuts":[{"text":"Your load dropped to {week.5.load} in the week of {week.5.start}."}],"suggestion":null,"dataNotes":[]}
            """;
        var outcome = await EvalRunner.RunAsync(evalCase, new ScriptedChatClient().Reply(draft), "gpt-test");

        Assert.Equal(GenerationStatus.Succeeded, outcome.Result.Status);
        Assert.False(outcome.Passed);
        Assert.Contains(outcome.Failures, f => f.Contains("week.5.load", StringComparison.Ordinal));
    }
}
