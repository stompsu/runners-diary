namespace RunnersDiary.Domain.Ai;

/// <summary>
/// One row of the AI usage ledger (<c>AiGenerations</c>, ADR 0028 §4): a generation request that reached the model, with its outcome
/// and token counts, never any content. A <b>system table</b>: deliberately not <c>IUserOwned</c> (no owner filter) and without a
/// foreign key to users, so global budgets survive guest cleanup; it is read and written only by the usage-ledger store with an
/// explicit user id. Rows older than 30 days are purged.
/// </summary>
public sealed class AiGeneration
{
    private AiGeneration()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public bool IsGuest { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public AiGenerationOutcome Outcome { get; private set; }

    public byte Attempts { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public int DurationMs { get; private set; }

    public short PromptVersion { get; private set; }

    public string ModelDeployment { get; private set; } = string.Empty;

    public static AiGeneration Create(
        Guid userId, bool isGuest, DateTime createdAtUtc, AiGenerationOutcome outcome, byte attempts, int inputTokens, int outputTokens,
        int durationMs, short promptVersion, string modelDeployment) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            IsGuest = isGuest,
            CreatedAt = createdAtUtc,
            Outcome = outcome,
            Attempts = attempts,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            DurationMs = durationMs,
            PromptVersion = promptVersion,
            ModelDeployment = modelDeployment,
        };
}
