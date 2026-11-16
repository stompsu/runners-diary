using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>The plan pipeline (generator, shared runner, agent, validator) wired by hand around a scripted or fake client; no database.</summary>
internal sealed class PlanHarness
{
    public PlanHarness(IChatClient? client = null, Action<AiOptions>? configure = null, string deployment = "gpt-test")
    {
        Options = new AiOptions { Provider = AiProvider.Fake };
        configure?.Invoke(Options);
        Prompts = PlanPromptCatalog.Load(RepoPaths.PlanPromptsInOutput);
        var wrapped = Microsoft.Extensions.Options.Options.Create(Options);
        Clients = new AiChatClientProvider(client is null ? null : ChatClientFactory.Wrap(client, NullLoggerFactory.Instance), client is null ? null : deployment);
        Agents = new TrainingPlanAgentProvider(Clients, Prompts, wrapped, NullLoggerFactory.Instance);
        Ledger = new AiUsageLedger(Generations, wrapped, Clock);
        Slots = new GenerationSlots(wrapped);
        Runner = new AiGenerationRunner(Ledger, Slots, InFlight, Clock, NullLogger.Instance);
        Monitor = new TestOptionsMonitor<AiOptions>(Options);
        Generator = new TrainingPlanGenerator(Agents, Runner, Monitor, NullLogger<TrainingPlanGenerator>.Instance);
    }

    public static readonly Guid User = new("33333333-3333-3333-3333-333333333333");

    public AiOptions Options { get; }

    public TestOptionsMonitor<AiOptions> Monitor { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));

    public PlanPromptCatalog Prompts { get; }

    public AiChatClientProvider Clients { get; }

    public TrainingPlanAgentProvider Agents { get; }

    public InMemoryAiGenerationStore Generations { get; } = new();

    public AiUsageLedger Ledger { get; }

    public GenerationSlots Slots { get; }

    public GenerationInFlightGuard InFlight { get; } = new();

    public AiGenerationRunner Runner { get; }

    public TrainingPlanGenerator Generator { get; }

    public Task<PlanGenerationResult> Generate(PlanFactSheet sheet, Guid? userId = null, bool guest = false) =>
        Generator.GenerateAsync(new PlanGenerationRequest(userId ?? User, guest, sheet), TestContext.Current.CancellationToken);
}
