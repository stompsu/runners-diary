using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// The versioned part of the plan rules that lives in the plan prompt manifest (<c>prompts/training-plan/manifest.json</c>, ADR 0040 §4-§5):
/// the <c>forbiddenTopic</c> deny list (v1 = the summary deny list). Same matching as the summary: whole words, <c>word*</c> prefixes and
/// multi-word phrases, case-insensitive.
/// </summary>
public sealed record PlanValidationPolicy(IReadOnlyList<string> DenyList)
{
    /// <summary>Mirrors the manifest's v1 list.</summary>
    public static PlanValidationPolicy Default { get; } = new(SummaryValidationPolicy.DefaultDenyList);
}
