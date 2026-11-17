using System.Text.RegularExpressions;
using RunnersDiary.Domain.Insights.Narrative.Languages;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>
/// The eval's "written in the requested language" check (ADR 0043 §5): a prompt-quality metric. A Polish text fails when it contains at least
/// <see cref="Threshold"/> distinct English words (all texts together). The word set is the production <c>wrongLanguage</c> stop list
/// (<see cref="PolishWrongLanguage.EnglishStopWords"/>, G-L1 follow-up) plus a few words the validator leaves out because they can occur in
/// Polish prose ("in" as in "in plus") or are running words a Polish text may borrow ("easy", "pace", "load"), so the eval is stricter than
/// the validator and every draft the validator rejects as <c>wrongLanguage</c> also fails here. Words that are also Polish ("a", "i", "to",
/// "on", "was", "ten") are on neither list.
/// </summary>
internal static partial class LanguageCheck
{
    public const int Threshold = 2;

    private static readonly HashSet<string> EnglishFunctionWords = new(PolishWrongLanguage.EnglishStopWords, StringComparer.Ordinal)
    {
        "in", "week", "weeks", "run", "runs", "load", "ratio", "easy", "pace",
    };

    /// <summary>The distinct English function words in <paramref name="texts"/> (placeholders removed), in order of appearance.</summary>
    public static List<string> EnglishWords(IEnumerable<string> texts)
    {
        var found = new List<string>();
        foreach (var text in texts)
        {
            foreach (Match match in Word().Matches(Placeholder().Replace(text, " ")))
            {
                var word = match.Value.ToLowerInvariant();
                if (EnglishFunctionWords.Contains(word) && !found.Contains(word))
                {
                    found.Add(word);
                }
            }
        }

        return found;
    }

    /// <summary>A failure message when Polish texts read as English, else null.</summary>
    public static string? PolishFailure(IEnumerable<string> texts) =>
        EnglishWords(texts) is { Count: >= Threshold } words ? $"the pl texts contain English words [{string.Join(", ", words)}]." : null;

    [GeneratedRegex(@"\{[^{}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
