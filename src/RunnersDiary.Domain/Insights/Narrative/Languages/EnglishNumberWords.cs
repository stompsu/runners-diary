using System.Collections.Frozen;

namespace RunnersDiary.Domain.Insights.Narrative.Languages;

/// <summary>
/// The English number words of the <c>literalNumber</c> rule (ADR 0026 §3; unchanged by ADR 0043): matched case-insensitively as whole
/// words against the lower-case letter runs of the prose.
/// </summary>
internal static class EnglishNumberWords
{
    /// <summary>English cardinals from "two" upward (and their plurals). "one" and "half" stay allowed ("one week", "half marathon").</summary>
    public static readonly FrozenSet<string> Cardinals = new[]
    {
        "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
        "sixteen", "seventeen", "eighteen", "nineteen", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
        "hundred", "hundreds", "thousand", "thousands", "million", "millions", "billion", "billions", "dozen", "dozens", "tens",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Ordinal and multiplicative number words: a position or a ratio is a number the model must not make up.</summary>
    public static readonly FrozenSet<string> Ordinals = new[]
    {
        "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth",
        "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth", "thirtieth",
        "fortieth", "fiftieth", "sixtieth", "seventieth", "eightieth", "ninetieth", "hundredth", "thousandth", "twice", "thrice",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// English number words that are ordinary Polish words once folded, so they are left out when English words are checked inside a
    /// Polish text (ADR 0043 §3 as implemented): "ten" is the Polish "this" ("ten tydzień").
    /// </summary>
    public static readonly FrozenSet<string> PolishHomographs = new[] { "ten" }.ToFrozenSet(StringComparer.Ordinal);

    public static bool Contains(string lowerWord) => Cardinals.Contains(lowerWord) || Ordinals.Contains(lowerWord);
}
