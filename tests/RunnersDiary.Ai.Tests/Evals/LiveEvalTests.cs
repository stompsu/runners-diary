using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>
/// Live mode (ADR 0026 §7), <b>off by default and never in CI</b>: the same cases three times against a real Foundry deployment.
/// Run locally after <c>az login</c> (role Cognitive Services User on the resource; no keys):
/// <code>
/// RD_AI_LIVE_EVAL=1 RD_AI_ENDPOINT=https://&lt;resource&gt;.openai.azure.com/ RD_AI_DEPLOYMENT=&lt;deployment&gt; \
///   dotnet test --project tests/RunnersDiary.Ai.Tests -- --filter-class "*LiveEvalTests"
/// </code>
/// Pass criteria per run: valid within two attempts and the case's required/forbidden keys hold. Tokens and cost are printed, and
/// for every attempt the validator's verdict (rule codes, locations, keys) and the raw draft (test output only, live mode only).
/// Targeted runs (tuning): <c>RD_AI_LIVE_CASES=taper-before-race,load-spike</c> skips the other cases, <c>RD_AI_LIVE_RUNS=1</c> runs
/// each case fewer times (a pass then proves less), <c>RD_AI_REASONING_EFFORT=medium</c> overrides the effort, and
/// <c>RD_AI_LIVE_LOG=/path/file.log</c> also appends the output to a file (the test platform shows passing tests' output only on failure).
/// </summary>
public sealed class LiveEvalTests(ITestOutputHelper output)
{
    private const int DefaultRuns = 3;

    private static readonly Lock LogLock = new();

    private static bool Enabled => Environment.GetEnvironmentVariable("RD_AI_LIVE_EVAL") == "1";

    private static int Runs => int.TryParse(Environment.GetEnvironmentVariable("RD_AI_LIVE_RUNS"), out var runs) && runs is >= 1 and <= DefaultRuns
        ? runs
        : DefaultRuns;

    private static bool Selected(string name) =>
        Environment.GetEnvironmentVariable("RD_AI_LIVE_CASES") is not { Length: > 0 } cases
        || cases.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(EvalCases.ModelNames), MemberType = typeof(EvalCases))]
    public async Task Live_case_passes_three_times(string name)
    {
        Assert.SkipUnless(Enabled, "Live eval is opt-in: set RD_AI_LIVE_EVAL=1, RD_AI_ENDPOINT and RD_AI_DEPLOYMENT.");
        Assert.SkipUnless(Selected(name), "Not selected by RD_AI_LIVE_CASES.");
        var endpoint = Environment.GetEnvironmentVariable("RD_AI_ENDPOINT") ?? throw new InvalidOperationException("RD_AI_ENDPOINT is not set.");
        var deployment = Environment.GetEnvironmentVariable("RD_AI_DEPLOYMENT") ?? throw new InvalidOperationException("RD_AI_DEPLOYMENT is not set.");
        var options = new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = endpoint, Deployment = deployment } };
        var effort = Environment.GetEnvironmentVariable("RD_AI_REASONING_EFFORT") is { Length: > 0 } e ? e : options.ReasoningEffort;
        // The raw Foundry client; the harness adds the same OpenTelemetry/logging middleware as the host.
        using var inner = ChatClientFactory.CreateFoundryClient(options);
        var outcomes = new List<EvalOutcome>();
        for (var run = 0; run < Runs; run++)
        {
            var outcome = await EvalRunner.RunAsync(EvalCases.Get(name), inner, deployment, o =>
            {
                o.Provider = AiProvider.Foundry;
                o.Foundry = options.Foundry;
                o.ReasoningEffort = effort;
            });
            Write(outcome.ToString());
            Write(outcome.Details());
            outcomes.Add(outcome);
        }

        Write(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{name}: {outcomes.Count(o => o.Passed)}/{Runs} passed, first-attempt {outcomes.Count(o => o.Result.Summary?.Attempts == 1)}/{Runs}, effort {effort}, total ≈ ${outcomes.Sum(o => o.EstimatedCostUsd):0.0000}"));
        Assert.All(outcomes, o => Assert.True(o.Passed, o.ToString()));
    }

    private void Write(string line)
    {
        output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("RD_AI_LIVE_LOG") is { Length: > 0 } path)
        {
            lock (LogLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
    }
}
