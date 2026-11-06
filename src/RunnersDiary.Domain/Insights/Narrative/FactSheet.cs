using System.Globalization;

namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>The value of a fact: exactly one of a number, a text (dates, codes, bands) or a flag.</summary>
public readonly record struct FactValue
{
    private FactValue(double? number, string? text, bool? flag)
    {
        Number = number;
        Text = text;
        Flag = flag;
    }

    public double? Number { get; }

    public string? Text { get; }

    public bool? Flag { get; }

    public static FactValue Of(double number) => new(number, null, null);

    public static FactValue Of(string text) => new(null, text ?? throw new ArgumentNullException(nameof(text)), null);

    public static FactValue Of(bool flag) => new(null, null, flag);

    public static FactValue Of(DateOnly date) => Of(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    public override string ToString() =>
        Number is { } n ? n.ToString("R", CultureInfo.InvariantCulture)
        : Flag is { } f ? (f ? "true" : "false")
        : Text ?? string.Empty;
}

/// <summary>One fact: a key (ADR 0026 §1 grammar), its verbatim value, its unit and the English metric display string.</summary>
public sealed record Fact(string Key, FactValue Value, FactUnit Unit, string Display);

/// <summary>
/// The only data the model sees (ADR 0026 §1): a flat, ordered list of facts projected from an <see cref="InsightsReport"/>.
/// It never contains user free text (notes, titles, race names, location, gear, weather), heart rate or ids.
/// </summary>
public sealed class FactSheet
{
    /// <summary>
    /// The projection version; a change to the projection bumps it and makes cached summaries stale (ADR 0028 §2).
    /// 2: the <c>week.N.taper</c> and <c>week.N.loadSpike</c> flags.
    /// </summary>
    public const int CurrentVersion = 2;

    private readonly Dictionary<string, Fact> _byKey;

    public FactSheet(int factSheetVersion, int metricsVersion, string language, IReadOnlyList<Fact> facts)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(facts);

        FactSheetVersion = factSheetVersion;
        MetricsVersion = metricsVersion;
        Language = language;
        Facts = facts;
        _byKey = new Dictionary<string, Fact>(facts.Count, StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            if (!FactKey.IsValid(fact.Key))
            {
                throw new ArgumentException($"Invalid fact key '{fact.Key}'.", nameof(facts));
            }

            if (!_byKey.TryAdd(fact.Key, fact))
            {
                throw new ArgumentException($"Duplicate fact key '{fact.Key}'.", nameof(facts));
            }
        }
    }

    public int FactSheetVersion { get; }

    public int MetricsVersion { get; }

    public string Language { get; }

    public IReadOnlyList<Fact> Facts { get; }

    public bool Contains(string key) => _byKey.ContainsKey(key);

    public Fact? Find(string key) => _byKey.GetValueOrDefault(key);

    public double? Number(string key) => Find(key)?.Value.Number;

    public bool? Flag(string key) => Find(key)?.Value.Flag;

    public string? Text(string key) => Find(key)?.Value.Text;
}

/// <summary>The key grammar of ADR 0026 §1: <c>segment(.segment)*</c>, a segment is <c>[a-z][A-Za-z0-9]*</c> or a 1-based index.</summary>
public static class FactKey
{
    public const int MaxLength = 80;

    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxLength)
        {
            return false;
        }

        foreach (var segment in key.Split('.'))
        {
            if (!IsSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        if (char.IsAsciiDigit(segment[0]))
        {
            // A 1-based index: digits without a leading zero.
            return segment[0] != '0' && segment.All(char.IsAsciiDigit);
        }

        return char.IsAsciiLetterLower(segment[0]) && segment.All(char.IsAsciiLetterOrDigit);
    }
}
