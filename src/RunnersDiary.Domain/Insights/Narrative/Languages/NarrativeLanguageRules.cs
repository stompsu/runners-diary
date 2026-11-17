using System.Collections.Frozen;
using RunnersDiary.Domain.Localization;
using RunnersDiary.Domain.Races;

namespace RunnersDiary.Domain.Insights.Narrative.Languages;

/// <summary>
/// The per-language parts of the narrative rules (ADR 0043 §3): which number words count as <c>literalNumber</c>, and how the
/// <c>forbiddenTopic</c> deny list is matched. Shared by the summary and the plan validator.
/// <list type="bullet">
/// <item><b>en</b> (and every v1/v2 policy): exactly the pre-M10 behaviour: lower-cased letter runs, the English number words, the
/// policy's deny list, no diacritic folding.</item>
/// <item><b>pl</b>: words are folded (lower case, diacritics removed, <c>ł</c> → <c>l</c>) on both sides; number words are the Polish
/// exact forms plus the English words that are not Polish words ("ten" is the Polish "this"); the deny list is the English list ∪ the
/// policy's Polish list.</item>
/// </list>
/// </summary>
public static class NarrativeLanguages
{
    public const string English = SupportedLanguages.English;

    public const string Polish = SupportedLanguages.Polish;

    /// <summary>The languages the validators have rules for: every <see cref="SupportedLanguages"/> tag (the prompt manifests may list fewer).</summary>
    public static bool IsSupported(string? language) => language is English or Polish;

    /// <summary>
    /// The Polish <c>forbiddenTopic</c> entries (ADR 0043 §3, as implemented in M10 C1; mirrored by the v3 manifests' <c>denyList.pl</c>).
    /// ASCII because matching folds both sides ("ból", "bol" and "bolu" all match). Exact forms where a stem collides with an ordinary or
    /// running word: <c>bol*</c> would match "bolid" and "Bolesławiec", <c>zawal*</c> "zawalony tydzień", <c>zapaleni*</c>
    /// "zapaleni biegacze", <c>zlaman*</c> "złamanie {x}" (breaking a time barrier); see the M10 plan §10. <c>zapaln*</c> (inflammatory) does not
    /// reach "zapał"/"zapalony" (enthusiasm); it does block "zapalniczka", which running prose never needs.
    /// </summary>
    public static IReadOnlyList<string> DefaultPolishDenyList { get; } =
    [
        "kontuzj*", "uraz*", "przetrenow*", "diagnoz*", "medyczn*", "chorob*", "zachorow*",
        "chory", "chora", "chore", "chorego", "chorej", "chorzy",
        "sercow*",
        "zawal", "zawalu", "zawalem", "zawaly", "zawalow", "zawalom", "zawalami", "zawalach",
        "lekarz*", "lekark*", "lekarsk*", "fizjoterap*",
        "bol", "bolu", "bolem", "bole", "bolow", "bolom", "bolami", "bolach", "boli", "bola", "bolal", "bolala", "bolalo", "bolaly", "bolec",
        "bolesn*", "obolal*",
        "naciagn*", "nadwyrez*", "sciegn*", "sciegien", "achilles*",
        "zapalenie", "zapalenia", "zapaleniu", "zapaleniem", "zapalen", "zapaleniom", "zapaleniami", "zapaleniach",
        "zlaman* zmeczeniow*", "zlaman* kosc*", "zlaman* przeciazeniow*", "zlaman* nog*",
        "peknieci*", "pekniet*",
        "naderw*", "zapaln*", "rehabilitac*", "ortoped*", "szpital*", "dolegliwos*",
        "ryzyko", "ryzyka", "ryzyku", "ryzykiem",
    ];

    /// <summary>The extra deny lists per language that a policy starts with (v3 manifests override them).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultLanguageDenyLists { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [Polish] = DefaultPolishDenyList }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static void EnsureSupported(string language)
    {
        if (!IsSupported(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "The validators have no rules for this language.");
        }
    }

    /// <summary>The words the rules look at: lower-case letter runs; for Polish, of the folded text.</summary>
    internal static List<string> Words(string text, string language) =>
        Placeholders.Words(language == English ? text : TextFolding.Fold(text));

    /// <summary>True when a word of <paramref name="words"/> (from <see cref="Words"/>) is a number word of the language.</summary>
    internal static bool HasNumberWord(IEnumerable<string> words, string language) => language == English
        ? words.Any(EnglishNumberWords.Contains)
        : words.Any(w => PolishNumberWords.IsNumberWord(w) || (EnglishNumberWords.Contains(w) && !EnglishNumberWords.PolishHomographs.Contains(w)));
}

/// <summary>
/// The <c>forbiddenTopic</c> matcher for one language. An entry is a word, a word prefix (<c>injur*</c>) or a phrase of such words
/// (<c>risk of</c>, <c>zlaman* zmeczeniow*</c>), case-insensitive; for Polish, diacritic-insensitive on both sides.
/// </summary>
internal sealed class DenyMatcher
{
    private readonly List<Pattern> patterns;

    private DenyMatcher(string language, List<Pattern> patterns)
    {
        Language = language;
        this.patterns = patterns;
    }

    public string Language { get; }

    /// <summary>
    /// English: <paramref name="denyList"/> only. Any other language: <paramref name="denyList"/> (the English list; models code-switch)
    /// ∪ <paramref name="languageDenyLists"/>[language], folded.
    /// </summary>
    public static DenyMatcher Create(string language, IReadOnlyList<string> denyList, IReadOnlyDictionary<string, IReadOnlyList<string>> languageDenyLists)
    {
        ArgumentNullException.ThrowIfNull(denyList);
        ArgumentNullException.ThrowIfNull(languageDenyLists);
        NarrativeLanguages.EnsureSupported(language);

        if (language == NarrativeLanguages.English)
        {
            return new DenyMatcher(language, Parse(denyList, fold: false));
        }

        var extra = languageDenyLists.GetValueOrDefault(language) ?? [];
        return new DenyMatcher(language, Parse([.. denyList, .. extra], fold: true));
    }

    /// <summary>Matches the words of a text as produced by <see cref="NarrativeLanguages.Words"/> for this matcher's language.</summary>
    public bool Matches(IReadOnlyList<string> words) => patterns.Any(p => p.Matches(words));

    private static List<Pattern> Parse(IEnumerable<string> entries, bool fold) =>
    [
        .. entries.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e =>
        {
            var parts = e.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new Pattern(
                [.. parts.Select(p => fold ? TextFolding.Fold(p.TrimEnd('*')) : p.TrimEnd('*'))],
                [.. parts.Select(p => p.EndsWith('*'))]);
        }),
    ];

    /// <param name="Words">The entry's words without the trailing <c>*</c>.</param>
    /// <param name="Prefix">Per word: true when it ended with <c>*</c> (matches any word starting with it).</param>
    private sealed record Pattern(string[] Words, bool[] Prefix)
    {
        public bool Matches(IReadOnlyList<string> words)
        {
            for (var start = 0; start + Words.Length <= words.Count; start++)
            {
                var match = true;
                for (var i = 0; i < Words.Length && match; i++)
                {
                    match = Prefix[i]
                        ? words[start + i].StartsWith(Words[i], StringComparison.Ordinal)
                        : words[start + i] == Words[i];
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
