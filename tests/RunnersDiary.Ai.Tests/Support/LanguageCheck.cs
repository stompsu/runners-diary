using System.Text.RegularExpressions;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>
/// The eval's "written in the requested language" check (ADR 0043 §5): a prompt-quality metric, not a validator rule. A Polish text fails
/// when it contains at least <see cref="Threshold"/> distinct English function words. The list leaves out words that are also Polish
/// ("a", "i", "to", "on", "was", "ten").
/// </summary>
internal static partial class LanguageCheck
{
    public const int Threshold = 2;

    private static readonly HashSet<string> EnglishFunctionWords = new(StringComparer.Ordinal)
    {
        "the", "and", "your", "you", "with", "were", "of", "from", "this", "that", "is", "are", "for", "in", "at", "after", "before", "week",
        "weeks", "run", "runs", "load", "ratio", "easy", "pace",
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
