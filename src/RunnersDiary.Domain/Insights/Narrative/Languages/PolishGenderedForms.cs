using System.Text;
using RunnersDiary.Domain.Races;

namespace RunnersDiary.Domain.Insights.Narrative.Languages;

/// <summary>
/// The Polish <c>genderedForm</c> rule (M10 G-L1 follow-up, ADR 0043 §3): the v3 prompts ask for gender-neutral wording, and this rule enforces
/// the part a word list can check reliably, a second-person singular past or conditional verb, whose ending carries the reader's gender:
/// <list type="bullet">
/// <item>past tense: a word ending in <c>łeś</c> (masculine: "ukończyłeś", "byłeś", "mogłeś") or <c>łaś</c> (feminine: "zrobiłaś",
/// "byłaś"), matched on the lower-cased text <b>with</b> diacritics;</item>
/// <item>conditional: a word ending in <c>łbyś</c> ("mógłbyś", "zrobiłbyś") or <c>łabyś</c> ("mogłabyś"), matched on the folded word
/// (<c>lbys</c>/<c>labys</c>), so a draft without diacritics ("zrobilbys") is caught as well;</item>
/// <item>the modal "powinieneś"/"powinnaś", with or without diacritics.</item>
/// </list>
/// Not covered (no word-level test without false positives): the past tense without diacritics ("zrobiles", "zrobilas": <c>-les</c>/<c>-las</c>
/// also end "las", "atlas", "miles", "Wales"); a gendered participle or adjective agreeing with the reader ("gotowy"/"gotowa", "sam"/"sama");
/// a past form after a separate "byś"/"żebyś"/"gdybyś" ("gdybyś zrobił"); and plural forms. The prompt covers those.
/// The first person and the third person ("zrobiłem", "zrobił", "tydzień był") never match; neither do neutral second-person forms
/// ("jesteś", "możesz", "masz", "zrobisz") or words like "cześć" and "wieś".
/// </summary>
public static class PolishGenderedForms
{
    /// <summary>
    /// What the retry note says when <c>genderedForm</c> was violated (fixed text, never model text): the rule code alone does not tell the
    /// model what to change, and the v3 prompts (hashed) phrase the rule as a style instruction without the code.
    /// </summary>
    public const string RetryHint =
        "genderedForm means a past-tense or conditional verb addressed to the runner whose ending shows a gender "
        + "(-łeś, -łaś, -łbyś, -łabyś, powinieneś, powinnaś; e.g. \"ukończyłeś\", \"zrobiłaś\", \"mógłbyś\"): rewrite those sentences "
        + "in the present tense (\"masz za sobą\"), the future (\"zapiszesz\"), the imperative, an impersonal form (\"zapisano\") "
        + "or with a thing as the subject (\"tydzień był lżejszy\")";

    private static readonly string[] PastSuffixes = ["łeś", "łaś"];

    private static readonly string[] FoldedConditionalSuffixes = ["lbys", "labys"];

    private static readonly string[] FoldedModals = ["powinienes", "powinnas"];

    /// <summary>True when a word of <paramref name="text"/> (prose, placeholders already removed or not) is a gendered second-person form.</summary>
    public static bool Contains(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // NFC first: a decomposed "ś" (s + combining acute) would otherwise split the word before the mark.
        return Placeholders.Words(text.Normalize(NormalizationForm.FormC)).Any(IsGendered);
    }

    /// <summary>One lower-case word (as produced by <see cref="Placeholders.Words"/>), with its diacritics.</summary>
    public static bool IsGendered(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (PastSuffixes.Any(s => word.Length > s.Length && word.EndsWith(s, StringComparison.Ordinal)))
        {
            return true;
        }

        var folded = TextFolding.Fold(word);
        return FoldedModals.Contains(folded, StringComparer.Ordinal)
            || FoldedConditionalSuffixes.Any(s => folded.Length > s.Length && folded.EndsWith(s, StringComparison.Ordinal));
    }
}
