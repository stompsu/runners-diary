using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>
/// Case-level checks shared by the CI (scripted) and live evals: required keys are cited, forbidden ones are not, and a Polish case is written
/// in Polish (<see cref="LanguageCheck"/>, a prompt-quality metric, stricter than the validator's <c>wrongLanguage</c> rule; ADR 0043 §5).
/// </summary>
internal static class EvalAssertions
{
    public static List<string> Failures(EvalCase evalCase, SummaryDraft draft)
    {
        var failures = new List<string>();
        foreach (var requirement in evalCase.Required)
        {
            var keys = CitedKeys(draft, requirement.Section);
            if (!requirement.AnyOf.Any(keys.Contains))
            {
                failures.Add($"{requirement.Section ?? "any section"} must cite one of [{string.Join(", ", requirement.AnyOf)}].");
            }
        }

        foreach (var prohibition in evalCase.Forbidden)
        {
            var hits = CitedKeys(draft, prohibition.Section).Intersect(prohibition.Keys, StringComparer.Ordinal).ToList();
            if (hits.Count > 0)
            {
                failures.Add($"{prohibition.Section} must not cite [{string.Join(", ", hits)}].");
            }
        }

        if (evalCase.Language == "pl" && LanguageCheck.PolishFailure(draft.Texts().Select(t => t.Text)) is { } language)
        {
            failures.Add(language);
        }

        return failures;
    }

    private static HashSet<string> CitedKeys(SummaryDraft draft, string? section)
    {
        var texts = draft.Texts().Where(t => section is null || t.Location == section || t.Location.StartsWith(section + ".", StringComparison.Ordinal));
        return [.. texts.SelectMany(t => Placeholders.Scan(t.Text).Keys)];
    }
}
