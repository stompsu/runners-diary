namespace RunnersDiary.Domain.Ai;

/// <summary>The outcome of one generation that reached the model (ledger <c>AiGenerations.Outcome</c>, ADR 0028 §4). Values are stored.</summary>
public enum AiGenerationOutcome : byte
{
    Succeeded = 0,
    InvalidOutput = 1,
    ProviderError = 2,
    Timeout = 3,
    Cancelled = 4,

    /// <summary>
    /// The provider answered 429 after the transport retry. Written only when an earlier attempt of the same generation was billed
    /// (its tokens and attempts count for the global budgets); it never counts toward the caller's own quota.
    /// </summary>
    Throttled = 5,
}
