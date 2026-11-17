using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>
/// The largest output of one answered attempt per language, against <c>Ai:Plan:MaxOutputTokens</c> (M10 review L5): Polish plans are
/// longer than English ones, and this is the number that decides whether the cap must rise. Fed by <see cref="PlanLiveEvalTests"/>
/// after each run; thread-safe because test classes may run in parallel. Attempts without an answer have no usage and are not counted.
/// </summary>
internal sealed class PlanOutputTokenStats
{
    private readonly ConcurrentDictionary<string, LanguageStats> byLanguage = new(StringComparer.Ordinal);

    /// <summary>Records the answered attempts of one run (<paramref name="where"/> names it, e.g. <c>half-marathon #2</c>).</summary>
    public void Record(string language, string where, IReadOnlyList<PlanAttemptDetails> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        var stats = byLanguage.GetOrAdd(language, _ => new LanguageStats());
        lock (stats)
        {
            foreach (var a in attempts)
            {
                stats.Attempts++;
                stats.Truncated += a.Truncated ? 1 : 0;
                if (a.OutputTokens > stats.Max)
                {
                    stats.Max = a.OutputTokens;
                    stats.MaxWhere = $"{where} attempt {a.Number}";
                }
            }
        }
    }

    /// <summary>The largest single-attempt output for the language, or null when nothing was recorded.</summary>
    public int? Max(string language) => byLanguage.TryGetValue(language, out var s) && s.Attempts > 0 ? s.Max : null;

    /// <summary>One line per language, ordinal order: <c>pl: max 6120 / cap 8000 (77%) at … ; 18 answered attempts, 0 truncated</c>.</summary>
    public string Report(int cap)
    {
        var sb = new StringBuilder();
        foreach (var (language, s) in byLanguage.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            lock (s)
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture,
                    $"max output tokens per attempt, {language}: {s.Max} / cap {cap} ({(cap > 0 ? 100.0 * s.Max / cap : 0):0}%) at {s.MaxWhere ?? "n/a"}; {s.Attempts} answered attempts, {s.Truncated} truncated (finish length)"));
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private sealed class LanguageStats
    {
        public int Attempts { get; set; }

        public int Truncated { get; set; }

        public int Max { get; set; }

        public string? MaxWhere { get; set; }
    }
}
