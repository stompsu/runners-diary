using System.Collections.Frozen;

namespace RunnersDiary.Domain.Insights.Narrative.Languages;

/// <summary>
/// The Polish <c>wrongLanguage</c> rule (M10 G-L1 follow-up, ADR 0043 §3): a text requested in Polish must not be written in English. The G-L1
/// re-run had one Polish summary written entirely in English ("So far, you kept the load ratio in the {acwr.asOf.band} range ..."), which every
/// other rule accepted.
/// <para>The test is a stop list of English <b>function words</b> (<see cref="EnglishStopWords"/>) that are neither Polish words nor loanwords
/// or abbreviations used in Polish running prose, matched on whole lower-case words (with diacritics, so no Polish word can fold onto one)
/// of the prose outside <c>{placeholders}</c>. Content words a Polish text may borrow ("easy", "tapering", "trail", "cross", "long", "pace",
/// "load") and acronyms ("ACWR", "RPE", "OCR") are not on the list, and neither are short words that are also Polish or common in Polish:
/// "a", "i", "to", "do", "na", "no", "go", "on", "we", "by", "as", "was", "ten", "but", "tak"; nor "it" ("branża IT"), "in" ("in plus"),
/// "or", "an", "be", "so", "if", "not", "per".</para>
/// <para>Thresholds (see <see cref="Flagged"/>): a text field is rejected when it has at least <see cref="FieldThreshold"/> distinct stop words
/// (any English sentence of more than a few words has two: "the" + "of", "you" + "with"); and when the whole draft has at least
/// <see cref="DraftThreshold"/> distinct stop words, every field with at least one is rejected too, which catches short English plan
/// descriptions ("Easy run at conversational pace.") that a per-field test alone would miss. One stray English word never triggers.
/// Free text from users (run titles, race names, notes) never reaches the model (fact sheets carry codes and placeholders only), so a
/// literal English race name cannot appear in a valid draft.</para>
/// </summary>
public static class PolishWrongLanguage
{
    /// <summary>Distinct stop words that reject one text field on their own.</summary>
    public const int FieldThreshold = 2;

    /// <summary>Distinct stop words across a whole draft above which every field with one of them is rejected.</summary>
    public const int DraftThreshold = 3;

    /// <summary>
    /// What the retry note says when <c>wrongLanguage</c> was violated (fixed text, never model text): the v3 prompts (hashed) name the language
    /// but not the rule code.
    /// </summary>
    public const string RetryHint =
        "wrongLanguage means the text is written (partly) in English although the requested language is Polish (Language: pl): "
        + "write every text field in Polish, including notes and session descriptions; keep the placeholders and the glossary terms unchanged";

    /// <summary>English function words that never occur in Polish prose (see the class summary for what is left out and why).</summary>
    public static IReadOnlySet<string> EnglishStopWords { get; } = new[]
    {
        "the", "and", "you", "your", "yours", "with", "without", "of", "is", "are", "were", "been", "this", "that", "these", "those",
        "from", "for", "have", "has", "had", "will", "would", "should", "could", "its", "at", "into", "than", "then", "they", "their",
        "there", "which", "what", "when", "about", "after", "before", "during", "our",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The distinct stop words of one text (placeholders ignored), in order of appearance.</summary>
    public static IReadOnlyList<string> EnglishWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<string>();
        foreach (var word in Placeholders.Scan(text).Words)
        {
            if (EnglishStopWords.Contains(word) && !found.Contains(word, StringComparer.Ordinal))
            {
                found.Add(word);
            }
        }

        return found;
    }

    /// <summary>
    /// The indexes of <paramref name="texts"/> (the text fields of one draft) that the rule rejects: a field with at least
    /// <see cref="FieldThreshold"/> distinct stop words; and, when the draft has at least <see cref="DraftThreshold"/> distinct stop words
    /// in total, every field with at least one.
    /// </summary>
    public static IReadOnlyList<int> Flagged(IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var perField = texts.Select(EnglishWords).ToList();
        var draftWide = perField.SelectMany(w => w).Distinct(StringComparer.Ordinal).Count() >= DraftThreshold;
        return [.. Enumerable.Range(0, perField.Count).Where(i => perField[i].Count >= FieldThreshold || (draftWide && perField[i].Count > 0))];
    }

    /// <summary>True when the rule rejects at least one of <paramref name="texts"/>.</summary>
    public static bool IsWrongLanguage(IReadOnlyList<string> texts) => Flagged(texts).Count > 0;
}
