using Microsoft.Extensions.AI;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>
/// One model attempt as the eval saw it: the raw response text and the validator's violations on it (rule code, fixed location,
/// well-formed key; never model text). Test output only: production code keeps neither.
/// </summary>
public sealed record EvalAttempt(string RawText, IReadOnlyList<SummaryViolation> Violations)
{
    public string Verdict => Violations.Count == 0
        ? "valid"
        : string.Join("; ", Violations.Select(v => $"{v.Rule.ToCode()}@{v.Location}{(v.Key is null ? string.Empty : "[" + v.Key + "]")}"));
}

/// <summary>The outcome of one eval run: the pipeline result, the case-level failures, the tokens spent and every attempt.</summary>
public sealed record EvalOutcome(
    EvalCase Case, GenerationResult Result, IReadOnlyList<string> Failures, int InputTokens, int OutputTokens, IReadOnlyList<EvalAttempt> Attempts)
{
    public bool Passed => Result.Status == GenerationStatus.Succeeded && Failures.Count == 0;

    /// <summary>gpt-5.4-mini Global Standard, USD per 1M tokens (ADR 0027 amendment 2026-11-04: 0.75 input, 4.50 output).</summary>
    public decimal EstimatedCostUsd => (InputTokens * 0.75m + OutputTokens * 4.50m) / 1_000_000m;

    public override string ToString() =>
        $"{Case.Name}: {(Passed ? "pass" : "FAIL")} ({Result.Status}{(Result.Failure is { } f ? "/" + f : string.Empty)}, attempts {Result.Summary?.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}), " +
        $"{InputTokens} in / {OutputTokens} out, ≈ ${EstimatedCostUsd.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}{(Failures.Count > 0 ? " — " + string.Join(" ", Failures) : string.Empty)}";

    /// <summary>The per-attempt detail for the live eval's test output: verdict and raw draft of each attempt.</summary>
    public string Details() => string.Join(
        "\n",
        Attempts.Select((a, i) => $"  attempt {i + 1}: {a.Verdict}\n    draft: {a.RawText.ReplaceLineEndings(" ")}"));
}

/// <summary>
/// Runs eval cases through the real pipeline (agent, validator, retry, ledger) against any <see cref="IChatClient"/>: the scripted
/// fake in CI, a Foundry deployment in live mode. Template cases are skipped: they never reach a model.
/// </summary>
internal static class EvalRunner
{
    public static async Task<EvalOutcome> RunAsync(EvalCase evalCase, IChatClient client, string deployment, Action<AiOptions>? configure = null)
    {
        if (evalCase.ExpectedTemplate is not null)
        {
            throw new InvalidOperationException($"{evalCase.Name} is a template case.");
        }

        // Not disposed: disposing the middleware would dispose the caller's client.
        var recorder = new RecordingChatClient(client);
        var harness = new AiHarness(recorder, configure, deployment);
        var result = await harness.Generate(evalCase.Sheet);
        var row = harness.Generations.Rows.SingleOrDefault();
        var failures = result.Summary is { } summary ? EvalAssertions.Failures(evalCase, summary.Draft) : [];
        var policy = harness.Agents.Agent!.Prompt.Policy;
        var attempts = recorder.Take().Select(text => new EvalAttempt(text, SummaryValidator.Validate(text, evalCase.Sheet, policy).Result.Violations)).ToList();
        return new EvalOutcome(evalCase, result, failures, row?.InputTokens ?? 0, row?.OutputTokens ?? 0, attempts);
    }
}
