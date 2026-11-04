using System.Diagnostics.CodeAnalysis;
using RunnersDiary.Domain.Common;

namespace RunnersDiary.Domain.Ai;

/// <summary>
/// One stored model summary of a user (<c>AiSummaries</c>, ADR 0028 §1): the validated narrative with its resolved values plus the
/// validity tuple it was generated for. Holds no user free text (the model never saw any). The natural key is
/// (<see cref="UserId"/>, period, compare, language); a regeneration replaces the row.
/// </summary>
public sealed class AiSummary : IUserOwned
{
    private AiSummary()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    /// <summary>Stored value of the comparison kind: None 0, Previous 1, PreviousYear 2.</summary>
    public byte Compare { get; private set; }

    public string Language { get; private set; } = string.Empty;

    public short MetricsVersion { get; private set; }

    public short FactSheetVersion { get; private set; }

    public short PromptVersion { get; private set; }

    /// <summary>The configured deployment the summary was generated for (part of its validity).</summary>
    public string ModelDeployment { get; private set; } = string.Empty;

    /// <summary>The model id the provider reported (audit only).</summary>
    public string? ModelId { get; private set; }

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Raw SHA-256 digest mapped to binary(32); never mutated.")]
    public byte[] Fingerprint { get; private set; } = [];

    public string ContentJson { get; private set; } = string.Empty;

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static AiSummary Create(Guid userId, DateOnly from, DateOnly to, byte compare, string language) =>
        new() { Id = Guid.CreateVersion7(), UserId = userId, PeriodFrom = from, PeriodTo = to, Compare = compare, Language = language };

    /// <summary>Sets everything that a (re)generation produces; the key members never change.</summary>
    public void Replace(
        short metricsVersion, short factSheetVersion, short promptVersion, string modelDeployment, string? modelId,
        byte[] fingerprint, string contentJson, int inputTokens, int outputTokens, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        MetricsVersion = metricsVersion;
        FactSheetVersion = factSheetVersion;
        PromptVersion = promptVersion;
        ModelDeployment = modelDeployment;
        ModelId = modelId;
        Fingerprint = fingerprint;
        ContentJson = contentJson;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CreatedAt = createdAtUtc;
    }
}
