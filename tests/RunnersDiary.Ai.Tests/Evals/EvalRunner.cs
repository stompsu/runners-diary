using Microsoft.Extensions.AI;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>The outcome of one eval run: the pipeline result, the case-level failures and the tokens spent.</summary>
public sealed record EvalOutcome(EvalCase Case, GenerationResult Result, IReadOnlyList<string> Failures, int InputTokens, int OutputTokens)
{
    public bool Passed => Result.Status == GenerationStatus.Succeeded && Failures.Count == 0;

    /// <summary>gpt-5.4-mini Data Zone Standard EU, USD per 1M tokens (ADR 0027 §6: 0.825 input, 4.95 output).</summary>
    public decimal EstimatedCostUsd => (InputTokens * 0.825m + OutputTokens * 4.95m) / 1_000_000m;

    public override string ToString() =>
        $"{Case.Name}: {(Passed ? "pass" : "FAIL")} ({Result.Status}{(Result.Failure is { } f ? "/" + f : string.Empty)}, attempts {Result.Summary?.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}), " +
        $"{InputTokens} in / {OutputTokens} out, ≈ ${EstimatedCostUsd:0.0000}{(Failures.Count > 0 ? " — " + string.Join(" ", Failures) : string.Empty)}";
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

        var harness = new AiHarness(client, configure, deployment);
        var result = await harness.Generate(evalCase.Sheet);
        var row = harness.Generations.Rows.SingleOrDefault();
        var failures = result.Summary is { } summary ? EvalAssertions.Failures(evalCase, summary.Draft) : [];
        return new EvalOutcome(evalCase, result, failures, row?.InputTokens ?? 0, row?.OutputTokens ?? 0);
    }
}
