using System.Globalization;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>
/// Live mode of the plan evals (ADR 0040 §7, gate G-P3), <b>off by default and never in CI; it costs money</b>: the six cases three times
/// against a real Foundry deployment through the real plan pipeline (agent, validator, retry). Run locally after <c>az login</c>:
/// <code>
/// RD_AI_LIVE_EVAL=1 RD_AI_ENDPOINT=https://&lt;resource&gt;.openai.azure.com/ RD_AI_DEPLOYMENT=&lt;deployment&gt; \
///   dotnet test --project tests/RunnersDiary.Ai.Tests -- --filter-class "*PlanLiveEvalTests"
/// </code>
/// Pass: valid within two attempts and the case's plan checks hold. Prints attempts, input/output tokens (vs the 8,000 cap) and the cost at
/// gpt-5.4-mini Global Standard prices ($0.75 / $4.50 per 1M). <c>RD_AI_LIVE_CASES</c> and <c>RD_AI_LIVE_RUNS</c> work as for the summary evals.
/// </summary>
public sealed class PlanLiveEvalTests(ITestOutputHelper output)
{
    private const int DefaultRuns = 3;
    private const double InputPerMillion = 0.75;
    private const double OutputPerMillion = 4.50;

    private static bool Enabled => Environment.GetEnvironmentVariable("RD_AI_LIVE_EVAL") == "1";

    private static int Runs => int.TryParse(Environment.GetEnvironmentVariable("RD_AI_LIVE_RUNS"), out var runs) && runs is >= 1 and <= DefaultRuns ? runs : DefaultRuns;

    private static bool Selected(string name) =>
        Environment.GetEnvironmentVariable("RD_AI_LIVE_CASES") is not { Length: > 0 } cases
        || cases.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(PlanEvalCases.Names), MemberType = typeof(PlanEvalCases))]
    public async Task Live_plan_case_passes_three_times(string name)
    {
        Assert.SkipUnless(Enabled, "Live eval is opt-in: set RD_AI_LIVE_EVAL=1, RD_AI_ENDPOINT and RD_AI_DEPLOYMENT.");
        Assert.SkipUnless(Selected(name), "Not selected by RD_AI_LIVE_CASES.");
        var endpoint = Environment.GetEnvironmentVariable("RD_AI_ENDPOINT") ?? throw new InvalidOperationException("RD_AI_ENDPOINT is not set.");
        var deployment = Environment.GetEnvironmentVariable("RD_AI_DEPLOYMENT") ?? throw new InvalidOperationException("RD_AI_DEPLOYMENT is not set.");
        var options = new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = endpoint, Deployment = deployment } };
        using var inner = ChatClientFactory.CreateFoundryClient(options);
        var c = PlanEvalCases.Get(name);

        var passed = 0;
        for (var run = 0; run < Runs; run++)
        {
            // A real clock: the harness's fake clock would never time an attempt out.
            var harness = new PlanHarness(inner, o =>
            {
                o.Provider = AiProvider.Foundry;
                o.Foundry = options.Foundry;
            }, deployment);
            var started = DateTimeOffset.UtcNow;
            var result = await harness.Generate(c.Sheet);
            var seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
            var cost = (result.InputTokens * InputPerMillion + result.OutputTokens * OutputPerMillion) / 1_000_000;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name} #{run + 1}: {result.Status} {result.Failure} attempts {result.Attempts}, {result.InputTokens} in / {result.OutputTokens} out tokens (cap {options.Plan.MaxOutputTokens}), {seconds:0} s, ${cost:0.0000}"));
            if (result.Status == GenerationStatus.Succeeded)
            {
                c.AssertPlan(result.Plan!, c.Sheet);
                output.WriteLine($"  {result.Plan!.Kept.Count} sessions, {result.Plan.DroppedCount} dropped; notes: {result.Plan.Notes}");
                passed++;
            }
        }

        Assert.Equal(Runs, passed);
    }
}
