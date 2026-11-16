using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>
/// Live mode of the plan evals (ADR 0040 §7, gate G-P3), <b>off by default and never in CI; it costs money</b>: the six cases three times
/// against a real Foundry deployment through the real plan pipeline (agent, validator, retry). Run locally after <c>az login</c>:
/// <code>
/// RD_AI_LIVE_EVAL=1 RD_AI_ENDPOINT=https://&lt;resource&gt;.openai.azure.com/ RD_AI_DEPLOYMENT=&lt;deployment&gt; \
///   dotnet test --project tests/RunnersDiary.Ai.Tests -- --filter-class "*PlanLiveEvalTests"
/// </code>
/// Pass: valid within two attempts and the case's plan checks hold. Prints per run the attempts, input/output tokens (vs the 8,000 cap) and the
/// cost at gpt-5.4-mini Global Standard prices ($0.75 / $4.50 per 1M; failed attempts included), and per answered attempt the finish reason
/// (<c>length</c> = cut at the cap), kept/dropped sessions and the violated rules with counts and locations. <c>RD_AI_LIVE_CASES</c> and
/// <c>RD_AI_LIVE_RUNS</c> work as for the summary evals.
/// <para>
/// <c>RD_AI_LIVE_DUMP_DIR=&lt;dir&gt;</c> (optional, local inspection only) writes, per invocation, <c>&lt;dir&gt;/&lt;timestamp&gt;/&lt;case&gt;/</c>
/// with the fact sheet, the raw model JSON of every answered attempt and the retry note sent before each retry. The data is the synthetic
/// eval data; the files never leave the machine. Test code only: the app has no such switch.
/// </para>
/// </summary>
public sealed class PlanLiveEvalTests(ITestOutputHelper output)
{
    private const int DefaultRuns = 3;
    private const double InputPerMillion = 0.75;
    private const double OutputPerMillion = 4.50;

    /// <summary>The most locations printed per rule and attempt.</summary>
    private const int MaxPrintedLocations = 12;

    private static readonly string Stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private static bool Enabled => Environment.GetEnvironmentVariable("RD_AI_LIVE_EVAL") == "1";

    private static int Runs => int.TryParse(Environment.GetEnvironmentVariable("RD_AI_LIVE_RUNS"), out var runs) && runs is >= 1 and <= DefaultRuns ? runs : DefaultRuns;

    private static string? DumpRoot => Environment.GetEnvironmentVariable("RD_AI_LIVE_DUMP_DIR") is { Length: > 0 } dir ? dir : null;

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

        string? dumpDir = null;
        if (DumpRoot is { } root)
        {
            dumpDir = Path.Combine(root, Stamp, name);
            Directory.CreateDirectory(dumpDir);
            await File.WriteAllTextAsync(Path.Combine(dumpDir, "factsheet.json"), PlanFactSheetJson.ToJson(c.Sheet), TestContext.Current.CancellationToken);
            output.WriteLine($"{name}: dumping raw attempts to {dumpDir}");
        }

        var passed = 0;
        var rescued = 0;
        var totalCost = 0.0;
        for (var run = 0; run < Runs; run++)
        {
            // Not disposed: disposing a delegating client would dispose the shared inner client.
            IChatClient client = dumpDir is null ? inner : new DumpingChatClient(inner, dumpDir, $"run{run + 1}");

            // A real clock: the harness's fake clock would never time an attempt out.
            var harness = new PlanHarness(client, o =>
            {
                o.Provider = AiProvider.Foundry;
                o.Foundry = options.Foundry;
            }, deployment);
            var started = DateTimeOffset.UtcNow;
            var result = await harness.Generate(c.Sheet);
            var seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
            var cost = Cost(result.InputTokens, result.OutputTokens);
            totalCost += cost;
            var retried = result.Status == GenerationStatus.Succeeded && result.Attempts > 1;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name} #{run + 1}: {result.Status} {result.Failure} attempts {result.Attempts}{(retried ? " (rescued by the retry)" : "")}, {result.InputTokens} in / {result.OutputTokens} out tokens (cap {options.Plan.MaxOutputTokens}), {seconds:0} s, ${cost:0.0000}"));
            if (result.Status != GenerationStatus.Succeeded || result.Attempts > 1)
            {
                WriteAttempts(result, options.Plan.MaxOutputTokens);
            }

            if (result.Status == GenerationStatus.Succeeded)
            {
                c.AssertPlan(result.Plan!, c.Sheet);
                output.WriteLine($"  {result.Plan!.Kept.Count} sessions, {result.Plan.DroppedCount} dropped; notes: {result.Plan.Notes}");
                passed++;
                rescued += retried ? 1 : 0;
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: {passed}/{Runs} passed, {rescued} rescued by the retry, total ${totalCost:0.0000} (failed attempts included)"));
        Assert.Equal(Runs, passed);
    }

    private static double Cost(int inputTokens, int outputTokens) => (inputTokens * InputPerMillion + outputTokens * OutputPerMillion) / 1_000_000;

    /// <summary>Per attempt: usage, finish reason, sessions and the violated rules grouped by code with counts and locations (no model text).</summary>
    private void WriteAttempts(PlanGenerationResult result, int outputCap)
    {
        var details = result.AttemptDetails ?? [];
        foreach (var a in details)
        {
            var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
                $"  attempt {a.Number}: {a.InputTokens} in / {a.OutputTokens} out (cap {outputCap}), finish {a.FinishReason ?? "n/a"}{(a.Truncated ? " TRUNCATED" : "")}, ${Cost(a.InputTokens, a.OutputTokens):0.0000}, {a.KeptSessions} kept / {a.DroppedCount} dropped"));
            if (a.Violations.Count == 0)
            {
                line.Append("; valid");
            }

            output.WriteLine(line.ToString());
            foreach (var group in a.Violations.GroupBy(v => v.Rule).OrderBy(g => g.Key))
            {
                var locations = group.Select(v => v.Location).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                var shown = string.Join(", ", locations.Take(MaxPrintedLocations));
                var more = locations.Count > MaxPrintedLocations ? $", +{locations.Count - MaxPrintedLocations} more" : "";
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {group.Key.ToCode()} x{group.Count()}: {shown}{more}"));
            }
        }

        for (var n = details.Count + 1; n <= result.Attempts; n++)
        {
            output.WriteLine($"  attempt {n}: no answer ({result.Failure}); usage unknown, not in the cost");
        }
    }

    /// <summary>
    /// Writes the raw answer of each call (<c>&lt;prefix&gt;-attempt&lt;n&gt;.json</c>) and, for a retry, the note the validator sent
    /// (<c>&lt;prefix&gt;-attempt&lt;n&gt;-retry-note.txt</c>). Live eval only.
    /// </summary>
    private sealed class DumpingChatClient(IChatClient inner, string directory, string prefix) : DelegatingChatClient(inner)
    {
        private int _calls;

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages as IList<ChatMessage> ?? [.. messages];
            var number = Interlocked.Increment(ref _calls);
            var userMessages = list.Where(m => m.Role == ChatRole.User).ToList();
            if (userMessages.Count > 1)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory, $"{prefix}-attempt{number}-retry-note.txt"), userMessages[^1].Text, CancellationToken.None);
            }

            var response = await base.GetResponseAsync(list, options, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{prefix}-attempt{number}.json"), response.Text, CancellationToken.None);
            return response;
        }
    }
}
