using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>The generation pipeline wired by hand around a scripted (or any) chat client, with in-memory stores and a fake clock.</summary>
internal sealed class AiHarness
{
    public AiHarness(IChatClient? client = null, Action<AiOptions>? configure = null, string deployment = "gpt-test")
    {
        Options = new AiOptions { Provider = AiProvider.Fake };
        configure?.Invoke(Options);
        Scripted = client as ScriptedChatClient;
        Prompts = PromptCatalog.Load(RepoPaths.PromptsInOutput);
        Clients = new AiChatClientProvider(client is null ? null : ChatClientFactory.Wrap(client, NullLoggerFactory.Instance), client is null ? null : deployment);
        Agents = new TrainingInsightsAgentProvider(Clients, Prompts, Microsoft.Extensions.Options.Options.Create(Options), NullLoggerFactory.Instance);
        Ledger = new AiUsageLedger(Generations, Microsoft.Extensions.Options.Options.Create(Options), Clock);
        Slots = new GenerationSlots(Microsoft.Extensions.Options.Options.Create(Options));
        Monitor = new TestOptionsMonitor<AiOptions>(Options);
        Generator = new SummaryGenerator(Agents, Ledger, Slots, InFlight, Monitor, Clock, NullLogger<SummaryGenerator>.Instance);
    }

    public AiOptions Options { get; }

    /// <summary>The options as the generator and the summary service read them (per call): mutate <see cref="Options"/> to flip settings at runtime.</summary>
    public TestOptionsMonitor<AiOptions> Monitor { get; }

    public ScriptedChatClient? Scripted { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));

    public PromptCatalog Prompts { get; }

    public AiChatClientProvider Clients { get; }

    public TrainingInsightsAgentProvider Agents { get; }

    public InMemoryAiGenerationStore Generations { get; } = new();

    public AiUsageLedger Ledger { get; }

    public GenerationSlots Slots { get; }

    public GenerationInFlightGuard InFlight { get; } = new();

    public SummaryGenerator Generator { get; }

    public Task<GenerationResult> Generate(FactSheet sheet, Guid? userId = null, bool guest = false) =>
        Generator.GenerateAsync(new GenerationRequest(userId ?? User, guest, sheet), TestContext.Current.CancellationToken);

    public Task<GenerationResult> GenerateWithToken(FactSheet sheet, CancellationToken cancellationToken) =>
        Generator.GenerateAsync(new GenerationRequest(User, false, sheet), cancellationToken);

    public static readonly Guid User = new("11111111-1111-1111-1111-111111111111");

    public InsightsSummaryService SummaryService(TestCurrentUser user, TestReportService reports, InMemoryAiSummaryStore store) =>
        new(reports, user, store, Generator, Ledger, Prompts, TemplateCatalog.Load(Prompts), DemoSummaryCatalog.Load(Prompts), Agents, Monitor);
}
