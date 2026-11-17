using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Insights.Narrative.Languages;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// The versioned part of the plan rules that lives in the plan prompt manifest (<c>prompts/training-plan/manifest.json</c>, ADR 0040 §4-§5):
/// the <c>forbiddenTopic</c> deny list (v1 = the summary deny list), plus the request language (ADR 0043 §3). Same matching as the summary:
/// whole words, <c>word*</c> prefixes and multi-word phrases, case-insensitive; for Polish also diacritic-insensitive, with the English list
/// ∪ the Polish list.
/// </summary>
public sealed record PlanValidationPolicy(IReadOnlyList<string> DenyList)
{
    /// <summary>The language the plan texts are written in (<see cref="NarrativeLanguages"/>). Default English, exactly the v1/v2 behaviour.</summary>
    public string Language { get; init; } = NarrativeLanguages.English;

    /// <summary>Extra deny entries per non-English language (see <see cref="SummaryValidationPolicy.LanguageDenyLists"/>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LanguageDenyLists { get; init; } = NarrativeLanguages.DefaultLanguageDenyLists;

    /// <summary>Mirrors the manifest's v1 list.</summary>
    public static PlanValidationPolicy Default { get; } = new(SummaryValidationPolicy.DefaultDenyList);

    /// <summary>This policy for plan texts in <paramref name="language"/> (en/pl).</summary>
    public PlanValidationPolicy ForLanguage(string language)
    {
        NarrativeLanguages.EnsureSupported(language);
        return this with { Language = language };
    }
}
