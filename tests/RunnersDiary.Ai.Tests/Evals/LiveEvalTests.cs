using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>
/// Live mode (ADR 0026 §7), <b>off by default and never in CI</b>: the same cases three times against a real Foundry deployment.
/// Run locally after <c>az login</c> (role Cognitive Services User on the resource; no keys):
/// <code>
/// RD_AI_LIVE_EVAL=1 RD_AI_ENDPOINT=https://&lt;resource&gt;.openai.azure.com/ RD_AI_DEPLOYMENT=&lt;deployment&gt; \
///   dotnet test --project tests/RunnersDiary.Ai.Tests -- --filter-class "*LiveEvalTests"
/// </code>
/// Pass criteria per run: valid within two attempts and the case's required/forbidden keys hold. Tokens and cost are printed.
/// </summary>
public sealed class LiveEvalTests(ITestOutputHelper output)
{
    private const int Runs = 3;

    private static bool Enabled => Environment.GetEnvironmentVariable("RD_AI_LIVE_EVAL") == "1";

    [Theory]
    [MemberData(nameof(EvalCases.ModelNames), MemberType = typeof(EvalCases))]
    public async Task Live_case_passes_three_times(string name)
    {
        Assert.SkipUnless(Enabled, "Live eval is opt-in: set RD_AI_LIVE_EVAL=1, RD_AI_ENDPOINT and RD_AI_DEPLOYMENT.");
        var endpoint = Environment.GetEnvironmentVariable("RD_AI_ENDPOINT") ?? throw new InvalidOperationException("RD_AI_ENDPOINT is not set.");
        var deployment = Environment.GetEnvironmentVariable("RD_AI_DEPLOYMENT") ?? throw new InvalidOperationException("RD_AI_DEPLOYMENT is not set.");
        var options = new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = endpoint, Deployment = deployment } };
        // The raw Foundry client; the harness adds the same OpenTelemetry/logging middleware as the host.
        using var inner = ChatClientFactory.CreateFoundryClient(options);
        var outcomes = new List<EvalOutcome>();
        for (var run = 0; run < Runs; run++)
        {
            var outcome = await EvalRunner.RunAsync(EvalCases.Get(name), inner, deployment, o =>
            {
                o.Provider = AiProvider.Foundry;
                o.Foundry = options.Foundry;
            });
            output.WriteLine(outcome.ToString());
            outcomes.Add(outcome);
        }

        output.WriteLine($"{name}: {outcomes.Count(o => o.Passed)}/{Runs} passed, first-attempt {outcomes.Count(o => o.Result.Summary?.Attempts == 1)}/{Runs}, total ≈ ${outcomes.Sum(o => o.EstimatedCostUsd):0.0000}");
        Assert.All(outcomes, o => Assert.True(o.Passed, o.ToString()));
    }
}
