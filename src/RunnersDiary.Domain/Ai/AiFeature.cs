namespace RunnersDiary.Domain.Ai;

/// <summary>The generation feature a ledger row belongs to (<c>AiGenerations.Feature</c>, ADR 0040 §6). Values are stored.</summary>
public enum AiFeature : byte
{
    Summary = 0,
    Plan = 1,
}
