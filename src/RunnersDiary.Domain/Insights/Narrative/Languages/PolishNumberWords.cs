using System.Collections.Frozen;
using RunnersDiary.Domain.Races;

namespace RunnersDiary.Domain.Insights.Narrative.Languages;

/// <summary>
/// The Polish number words of the <c>literalNumber</c> rule (ADR 0043 §3). Matching is on <b>folded</b> whole words (lower case, diacritics
/// removed, <c>ł</c> → <c>l</c>, see <see cref="TextFolding"/>) so "dwóch" and "dwoch" both match, and it uses <b>exact inflected forms,
/// never stems</b>: Polish stems collide with everyday words ("piąt*" would match "piątek", "trzy*" would match "trzymaj").
/// <para>Covered: cardinals from two upward in all cases (incl. the <c>-naście</c>, <c>-dzieści</c>/<c>-dziesiąt</c> and hundreds
/// families, "tysiąc", "milion", "miliard", "tuzin"); the vague counts that English expresses with banned words ("kilkanaście",
/// "kilkadziesiąt", "kilkaset" ~ "dozens"/"hundreds"); "półtora"; collective numerals ("dwoje", "czworga"); number nouns ("dwójka",
/// "setka", "dziesiątka", "dycha"); ordinals "pierwszy" to "dwudziesty", the tens ordinals, "setny", "tysięczny" in every gender and case;
/// the multiplicative adverbs "podwójnie", "potrójnie", "poczwórnie"; and number compounds: a numeral combining form followed by a unit or
/// "-krotny" stem ("dwukrotnie", "trzytygodniowy", "dziesięciokilometrowy"; <see cref="IsNumberCompound"/>).</para>
/// <para>Allowed, like "one" and "half" in English: "jeden"/"jedna"/"jedno", "pół", "połowa", "półmaraton", "kilka", "oba", "raz".
/// Deliberately not listed: "piątki", "piątek", "piątkom", "piątkami", "piątkach" (also the plural of "piątek", Friday) and "dysze".</para>
/// <para>The list cannot be exhaustive (rare forms, typos); ADR 0043 Consequences.</para>
/// </summary>
internal static class PolishNumberWords
{
    /// <summary>Numeral combining forms of compounds ("dwu-", "pięcio-", "stu-"); "jedno-", "pół-" and "kilku-" are not numbers we ban.</summary>
    private static readonly string[] CombiningForms = Fold(
    [
        "dwu", "trzy", "cztero", "pięcio", "sześcio", "siedmio", "ośmio", "dziewięcio", "dziesięcio",
        "jedenasto", "dwunasto", "trzynasto", "czternasto", "piętnasto", "szesnasto", "siedemnasto", "osiemnasto", "dziewiętnasto",
        "dwudziesto", "trzydziesto", "czterdziesto", "pięćdziesięcio", "sześćdziesięcio", "siedemdziesięcio", "osiemdziesięcio",
        "dziewięćdziesięcio", "stu", "dwustu", "trzystu", "czterystu", "pięćset", "pięciuset", "tysiąc", "półtora",
    ]);

    /// <summary>
    /// What may follow a combining form for the word to be a number compound: units of time, distance and share, and "-krotn-" (multiplicatives).
    /// A closed list, so "dwutlenek" or "trzymaj" never match.
    /// </summary>
    private static readonly string[] CompoundStems = Fold(
    [
        "krotn", "dniow", "tygodniow", "miesięczn", "roczn", "letni", "kilometrow", "metrow", "minutow", "godzinn", "sekundow", "procentow",
        "seryjn", "etapow",
    ]);

    public static readonly FrozenSet<string> Words = Build().ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True when the folded, lower-case word is a Polish number word or number compound.</summary>
    public static bool IsNumberWord(string foldedWord) => Words.Contains(foldedWord) || IsNumberCompound(foldedWord);

    /// <summary>A numeral combining form directly followed by a closed-list stem ("dwukrotnie", "trzydniowy", "stumetrowy").</summary>
    public static bool IsNumberCompound(string foldedWord)
    {
        foreach (var form in CombiningForms)
        {
            if (foldedWord.Length > form.Length && foldedWord.StartsWith(form, StringComparison.Ordinal))
            {
                var rest = foldedWord.AsSpan(form.Length);
                foreach (var stem in CompoundStems)
                {
                    if (rest.StartsWith(stem, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static List<string> Build()
    {
        var words = new List<string>
        {
            // 2-10: nominative, oblique and the masculine-personal forms.
            "dwa", "dwie", "dwaj", "dwóch", "dwu", "dwóm", "dwom", "dwoma", "dwiema",
            "trzy", "trzej", "trzech", "trzem", "trzema",
            "cztery", "czterej", "czterech", "czterem", "czterema",
            "pięć", "pięciu", "pięcioma",
            "sześć", "sześciu", "sześcioma",
            "siedem", "siedmiu", "siedmioma",
            "osiem", "ośmiu", "ośmioma",
            "dziewięć", "dziewięciu", "dziewięcioma",
            "dziesięć", "dziesięciu", "dziesięcioma",

            // Hundreds.
            "sto", "stu", "stoma",
            "dwieście", "dwustu", "dwustoma",
            "trzysta", "trzystu", "trzystoma",
            "czterysta", "czterystu", "czterystoma",
            "pięćset", "pięciuset", "sześćset", "sześciuset", "siedemset", "siedmiuset", "osiemset", "ośmiuset", "dziewięćset", "dziewięciuset",

            // Vague counts that English says with banned words (dozens, hundreds), and one and a half.
            "kilkanaście", "kilkunastu", "kilkunastoma", "kilkadziesiąt", "kilkudziesięciu", "kilkudziesięcioma", "kilkaset", "kilkuset",
            "półtora", "półtorej",

            // Multiplicative adverbs (the "-krotnie" family is a compound, below).
            "podwójnie", "potrójnie", "poczwórnie",

            // 5 as a noun: only the forms that are not also the plural of "piątek" (Friday).
            "piątka", "piątkę", "piątką", "piątce",

            // "dycha" (a 10K, ten); "dysze" (also "nozzles") left out.
            "dycha", "dychy", "dychę", "dychą", "dych", "dychom", "dychami", "dychach",
        };

        // 11-19: nominative, genitive/dative/locative, instrumental.
        foreach (var (nominative, stem) in new[]
        {
            ("jedenaście", "jedenast"), ("dwanaście", "dwunast"), ("trzynaście", "trzynast"), ("czternaście", "czternast"),
            ("piętnaście", "piętnast"), ("szesnaście", "szesnast"), ("siedemnaście", "siedemnast"), ("osiemnaście", "osiemnast"),
            ("dziewiętnaście", "dziewiętnast"),

            // 20-90.
            ("dwadzieścia", "dwudziest"), ("trzydzieści", "trzydziest"), ("czterdzieści", "czterdziest"),
            ("pięćdziesiąt", "pięćdziesięci"), ("sześćdziesiąt", "sześćdziesięci"), ("siedemdziesiąt", "siedemdziesięci"),
            ("osiemdziesiąt", "osiemdziesięci"), ("dziewięćdziesiąt", "dziewięćdziesięci"),
        })
        {
            words.AddRange([nominative, stem + "u", stem + "oma"]);
        }

        // Masculine nouns: thousand, million, billion, dozen.
        words.AddRange(["tysiąc", "tysiąca", "tysiącowi", "tysiącem", "tysiącu", "tysiące", "tysięcy", "tysiącom", "tysiącami", "tysiącach"]);
        foreach (var noun in new[] { "milion", "miliard", "tuzin" })
        {
            words.AddRange([noun, noun + "a", noun + "owi", noun + "em", noun + "ie", noun + "y", noun + "ów", noun + "om", noun + "ami", noun + "ach"]);
        }

        // Feminine number nouns in -ka: all cases, both numbers.
        foreach (var (stem, genitivePlural) in new[]
        {
            ("dwój", "dwójek"), ("trój", "trójek"), ("czwór", "czwórek"), ("szóst", "szóstek"), ("siódem", "siódemek"), ("ósem", "ósemek"),
            ("dziewiąt", "dziewiątek"), ("dziesiąt", "dziesiątek"), ("set", "setek"),
        })
        {
            words.AddRange([stem + "ka", stem + "ki", stem + "ce", stem + "kę", stem + "ką", stem + "ko", stem + "kom", stem + "kami", stem + "kach", genitivePlural]);
        }

        // Collective numerals: dwoje, dwojga, dwojgu, dwojgiem; ...
        foreach (var (nominative, stem) in new[]
        {
            ("dwoje", "dwoj"), ("troje", "troj"), ("czworo", "czwor"), ("pięcioro", "pięcior"), ("sześcioro", "sześcior"),
            ("siedmioro", "siedmior"), ("ośmioro", "ośmior"), ("dziewięcioro", "dziewięcior"), ("dziesięcioro", "dziesięcior"),
        })
        {
            words.AddRange([nominative, stem + "ga", stem + "gu", stem + "giem"]);
        }

        // Ordinals: "drugi" (velar stem), "trzeci" (soft stem), the rest hard-stem adjectives.
        words.AddRange(["drugi", "druga", "drugie", "drugiego", "drugiej", "drugiemu", "drugą", "drugim", "drugich", "drugimi", "drudzy"]);
        words.AddRange(["trzeci", "trzecia", "trzecie", "trzeciego", "trzeciej", "trzeciemu", "trzecią", "trzecim", "trzecich", "trzecimi"]);
        foreach (var stem in new[]
        {
            "pierwsz", "czwart", "piąt", "szóst", "siódm", "ósm", "dziewiąt", "dziesiąt",
            "jedenast", "dwunast", "trzynast", "czternast", "piętnast", "szesnast", "siedemnast", "osiemnast", "dziewiętnast",
            "dwudziest", "trzydziest", "czterdziest", "pięćdziesiąt", "sześćdziesiąt", "siedemdziesiąt", "osiemdziesiąt", "dziewięćdziesiąt",
            "setn", "tysięczn",
        })
        {
            words.AddRange(
            [
                stem + "y", stem + "a", stem + "e", stem + "ego", stem + "ej", stem + "emu", stem + "ą", stem + "ym", stem + "ych", stem + "ymi",
                MasculinePersonalPlural(stem),
            ]);
        }

        return Fold(words).ToList();
    }

    /// <summary>"pierwsz" → "pierwsi", "jedenast" → "jedenaści", "piąt" → "piąci", "siódm" → "siódmi", "setn" → "setni".</summary>
    private static string MasculinePersonalPlural(string stem) =>
        stem.EndsWith("sz", StringComparison.Ordinal) ? stem[..^2] + "si"
        : stem.EndsWith("st", StringComparison.Ordinal) ? stem[..^2] + "ści"
        : stem.EndsWith('t') ? stem[..^1] + "ci"
        : stem + "i";

    private static string[] Fold(IEnumerable<string> words) => [.. words.Select(TextFolding.Fold).Distinct(StringComparer.Ordinal)];
}
