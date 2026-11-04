using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>The stable rule codes of ADR 0026 §3; <see cref="ValidationRuleCodes.ToCode"/> gives the wire/log names.</summary>
public enum ValidationRuleCode
{
    Schema,
    UnknownKey,
    LiteralNumber,
    MissingCitation,
    ForbiddenTopic,
    RaceWeekPeak,
    EstimatedLoad,
    Markup,
}

public static class ValidationRuleCodes
{
    public static string ToCode(this ValidationRuleCode code) => code switch
    {
        ValidationRuleCode.Schema => "schema",
        ValidationRuleCode.UnknownKey => "unknownKey",
        ValidationRuleCode.LiteralNumber => "literalNumber",
        ValidationRuleCode.MissingCitation => "missingCitation",
        ValidationRuleCode.ForbiddenTopic => "forbiddenTopic",
        ValidationRuleCode.RaceWeekPeak => "raceWeekPeak",
        ValidationRuleCode.EstimatedLoad => "estimatedLoad",
        ValidationRuleCode.Markup => "markup",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown rule code."),
    };
}

/// <summary>
/// One violation. <see cref="Location"/> is a fixed path (<c>headline</c>, <c>watchOuts.2</c>, <c>draft</c>); <see cref="Key"/> is a fact
/// key (only well-formed keys, never arbitrary model text), so a violation can be logged or sent back to the model safely.
/// </summary>
public sealed record SummaryViolation(ValidationRuleCode Rule, string Location, string? Key = null);

public sealed record SummaryValidationResult(IReadOnlyList<SummaryViolation> Violations)
{
    public static SummaryValidationResult Valid { get; } = new([]);

    public bool IsValid => Violations.Count == 0;

    /// <summary>The distinct rule codes, in rule order.</summary>
    public IReadOnlyList<ValidationRuleCode> Codes => [.. Violations.Select(v => v.Rule).Distinct().Order()];
}

/// <summary>The versioned parts of the rules that live in the prompt manifest (ADR 0026 §3, §6).</summary>
public sealed record SummaryValidationPolicy(double EstimatedLoadThreshold, IReadOnlyList<string> DenyList)
{
    /// <summary>
    /// Mirrors the manifest's v1 list. <c>strain*</c> forbids the bare word in prose (the injury sense: "a strain", "strained"); a strain
    /// metric could only ever be cited as a <c>{key}</c> placeholder, and placeholders are removed before this scan.
    /// </summary>
    public static IReadOnlyList<string> DefaultDenyList { get; } =
    [
        "injur*", "overtrain*", "diagnos*", "medical", "illness", "disease", "cardiac", "risk of", "doctor", "physio*",
        "pain*", "hurt*", "strain*", "tendon*", "tendin*", "fractur*",
    ];

    public static SummaryValidationPolicy Default { get; } = new(0.25, DefaultDenyList);
}

/// <summary>
/// Pure server-side validation of a narrative against its fact sheet (ADR 0026 §3). It runs on every model output before anything
/// is stored or returned, and on templates and demo fixtures.
/// <para>
/// <c>literalNumber</c> flags, outside placeholders: any Unicode digit or number character (incl. <c>Ⅻ</c>), <c>%</c> <c>‰</c> <c>°</c>;
/// cardinal words from "two" up (<see cref="NumberWords"/>); ordinal words "first" to "twentieth", the tens ordinals, "hundredth",
/// "thousandth", and "twice"/"thrice" (<see cref="OrdinalWords"/>), case-insensitive as whole words ("third week", "First,");
/// and ASCII Roman numerals as standalone tokens: a run of letters bounded by non-letters, <b>at least two letters, all upper case</b>,
/// and a well-formed numeral (<c>II</c>, <c>IV</c>, <c>XII</c>). So "I" (the pronoun), single capitals and lower-case words such as
/// "mix", "civil" or "did" never match; an all-capitals word that happens to be a valid numeral ("MIX", "XC") does, which the
/// prompt's plain-prose rule makes acceptable.
/// </para>
/// </summary>
public static class SummaryValidator
{
    public const int MaxHeadlineLength = 160;
    public const int MaxItemLength = 280;
    public const int MaxWentWell = 3;
    public const int MaxWatchOuts = 3;
    public const int MaxDataNotes = 2;
    public const int MaxItems = 9;

    /// <summary>English cardinals from "two" upward (and their plurals). "one" and "half" stay allowed ("one week", "half marathon").</summary>
    private static readonly FrozenSet<string> NumberWords = new[]
    {
        "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
        "sixteen", "seventeen", "eighteen", "nineteen", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
        "hundred", "hundreds", "thousand", "thousands", "million", "millions", "billion", "billions", "dozen", "dozens", "tens",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Ordinal and multiplicative number words: a position or a ratio is a number the model must not make up.</summary>
    private static readonly FrozenSet<string> OrdinalWords = new[]
    {
        "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth",
        "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth", "thirtieth",
        "fortieth", "fiftieth", "sixtieth", "seventieth", "eightieth", "ninetieth", "hundredth", "thousandth", "twice", "thrice",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>A well-formed Roman numeral (1–3999), upper case; the caller requires at least two letters.</summary>
    private static readonly Regex RomanNumeral = new(
        "^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    /// <summary>Parses the raw model output and validates it; an unparsable or wrongly shaped output is a <c>schema</c> violation.</summary>
    public static (SummaryDraft? Draft, SummaryValidationResult Result) Validate(string? json, FactSheet sheet, SummaryValidationPolicy policy)
    {
        var draft = SummaryDraftJson.TryParse(json);
        return draft is null
            ? (null, new SummaryValidationResult([new SummaryViolation(ValidationRuleCode.Schema, "draft")]))
            : (draft, Validate(draft, sheet, policy));
    }

    public static SummaryValidationResult Validate(SummaryDraft draft, FactSheet sheet, SummaryValidationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(policy);

        var violations = new List<SummaryViolation>();
        CheckSchema(draft, violations);

        var texts = draft.Texts().ToList();
        var cited = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var deny = DenyPattern.ParseAll(policy.DenyList);

        foreach (var (location, text) in texts)
        {
            var scan = Placeholders.Scan(text);
            cited[location] = scan.Keys;

            if (!scan.Balanced)
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.UnknownKey, location));
            }

            foreach (var key in scan.Keys.Where(k => !sheet.Contains(k)))
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.UnknownKey, location, FactKey.IsValid(key) ? key : null));
            }

            if (HasLiteralNumber(scan.Prose))
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.LiteralNumber, location));
            }

            if (deny.Any(p => p.Matches(scan.Words)))
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.ForbiddenTopic, location));
            }

            if (HasMarkup(text))
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.Markup, location));
            }

            var isListItem = location.StartsWith("wentWell.", StringComparison.Ordinal)
                || location.StartsWith("watchOuts.", StringComparison.Ordinal)
                || location.StartsWith("dataNotes.", StringComparison.Ordinal);
            if (isListItem && scan.Keys.Count == 0)
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.MissingCitation, location));
            }
        }

        CheckRaceWeekPeak(draft, sheet, cited, violations);
        CheckEstimatedLoad(sheet, policy, cited, violations);

        return violations.Count == 0 ? SummaryValidationResult.Valid : new SummaryValidationResult(violations);
    }

    private static void CheckSchema(SummaryDraft draft, List<SummaryViolation> violations)
    {
        if (!IsLength(draft.Headline, MaxHeadlineLength))
        {
            violations.Add(new SummaryViolation(ValidationRuleCode.Schema, "headline"));
        }

        foreach (var (location, text) in draft.Items())
        {
            if (!IsLength(text, MaxItemLength))
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.Schema, location));
            }
        }

        if (draft.WentWell.Count > MaxWentWell)
        {
            violations.Add(new SummaryViolation(ValidationRuleCode.Schema, "wentWell"));
        }

        if (draft.WatchOuts.Count > MaxWatchOuts)
        {
            violations.Add(new SummaryViolation(ValidationRuleCode.Schema, "watchOuts"));
        }

        if (draft.DataNotes.Count > MaxDataNotes)
        {
            violations.Add(new SummaryViolation(ValidationRuleCode.Schema, "dataNotes"));
        }

        var total = draft.WentWell.Count + draft.WatchOuts.Count + draft.DataNotes.Count + (draft.Suggestion is null ? 0 : 1);
        if (total is < 1 or > MaxItems)
        {
            violations.Add(new SummaryViolation(ValidationRuleCode.Schema, "draft"));
        }

        static bool IsLength(string text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max;
    }

    /// <summary>
    /// A ratio that a race drove must name the race (ADR 0024 note for M6), wherever it could read as a training spike: the headline,
    /// a watch-out or the suggestion (a went-well item or a data note may cite a ratio without that framing). Two cases:
    /// <list type="bullet">
    /// <item>the ACWR peak (<c>acwr.peak.*</c>) when <c>acwr.peak.isRaceWeek</c>: the same text must cite <c>acwr.peak.raceType</c>,
    /// the peak week's <c>week.N.raceType</c> or a <c>race.*</c> key;</item>
    /// <item>a week's ratio (<c>week.N.acwrAtEnd</c> or <c>week.N.acwrBand</c>) when <c>week.N.raceType</c> exists, i.e. a completed race
    /// lies in that week, which is exactly the acute window of its end-of-week ratio: the same text must cite <c>week.N.raceType</c> or a
    /// <c>race.*</c> key.</item>
    /// </list>
    /// </summary>
    private static void CheckRaceWeekPeak(
        SummaryDraft draft, FactSheet sheet, Dictionary<string, IReadOnlyList<string>> cited, List<SummaryViolation> violations)
    {
        var peakIsRace = sheet.Flag("acwr.peak.isRaceWeek") == true;
        var peakWeekRace = sheet.Number("acwr.peak.week") is { } peakWeek
            ? string.Create(CultureInfo.InvariantCulture, $"week.{peakWeek}.raceType")
            : null;

        foreach (var (location, _) in draft.Texts())
        {
            var framing = location is "headline" or "suggestion" || location.StartsWith("watchOuts.", StringComparison.Ordinal);
            if (!framing)
            {
                continue;
            }

            var keys = cited[location];
            var namesAnyRace = keys.Any(k => k.StartsWith("race.", StringComparison.Ordinal));

            if (peakIsRace
                && keys.Any(k => k.StartsWith("acwr.peak.", StringComparison.Ordinal))
                && !namesAnyRace
                && !keys.Any(k => k == "acwr.peak.raceType" || k == peakWeekRace))
            {
                violations.Add(new SummaryViolation(ValidationRuleCode.RaceWeekPeak, location, "acwr.peak.raceType"));
            }

            foreach (var week in keys.Select(WeekOfRatio).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                var raceKey = week + ".raceType";
                if (sheet.Contains(raceKey) && !namesAnyRace && !keys.Contains(raceKey, StringComparer.Ordinal))
                {
                    violations.Add(new SummaryViolation(ValidationRuleCode.RaceWeekPeak, location, raceKey));
                }
            }
        }

        // "week.N" when the key is week.N.acwrAtEnd or week.N.acwrBand.
        static string? WeekOfRatio(string key)
        {
            var parts = key.Split('.');
            return parts is ["week", var index, "acwrAtEnd" or "acwrBand"] ? "week." + index : null;
        }
    }

    /// <summary>A large share of load from estimated RPE must be disclosed (ADR 0024 §2).</summary>
    private static void CheckEstimatedLoad(
        FactSheet sheet, SummaryValidationPolicy policy, Dictionary<string, IReadOnlyList<string>> cited, List<SummaryViolation> violations)
    {
        if (sheet.Number("dq.estimatedLoadShare") is not { } share || share < policy.EstimatedLoadThreshold)
        {
            return;
        }

        var disclosed = cited.Values.Any(keys => keys.Any(k => k is "dq.estimatedLoadShare" or "dq.sessionsWithEstimatedRpe"));
        if (!disclosed)
        {
            violations.Add(new SummaryViolation(ValidationRuleCode.EstimatedLoad, "draft", "dq.estimatedLoadShare"));
        }
    }

    private static bool HasLiteralNumber(string prose)
    {
        foreach (var c in prose)
        {
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
                || c is '%' or '‰' or '°')
            {
                return true;
            }
        }

        return Placeholders.Words(prose).Any(w => NumberWords.Contains(w) || OrdinalWords.Contains(w)) || HasRomanNumeral(prose);
    }

    /// <summary>A standalone, all-capitals, well-formed Roman numeral of at least two letters (see the class remarks).</summary>
    private static bool HasRomanNumeral(string prose)
    {
        var start = -1;
        for (var i = 0; i <= prose.Length; i++)
        {
            var letter = i < prose.Length && char.IsLetter(prose[i]);
            if (letter && start < 0)
            {
                start = i;
            }
            else if (!letter && start >= 0)
            {
                var token = prose[start..i];
                start = -1;
                if (token.Length >= 2 && token.All(c => c is 'I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M') && RomanNumeral.IsMatch(token))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasMarkup(string text)
    {
        if (text.Any(char.IsControl))
        {
            return true;
        }

        return text.Contains('<', StringComparison.Ordinal)
            || text.Contains("](", StringComparison.Ordinal)
            || text.Contains("**", StringComparison.Ordinal)
            || text.Contains("://", StringComparison.Ordinal)
            || text.Contains("www.", StringComparison.OrdinalIgnoreCase)
            || text.TrimStart().StartsWith('#');
    }

    /// <summary>A deny-list entry: a word, a word prefix (<c>injur*</c>) or a phrase of words (<c>risk of</c>), case-insensitive.</summary>
    private sealed record DenyPattern(string[] Words, bool Prefix)
    {
        public static List<DenyPattern> ParseAll(IReadOnlyList<string> entries) =>
        [
            .. entries.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e =>
            {
                var trimmed = e.Trim().ToLowerInvariant();
                var prefix = trimmed.EndsWith('*');
                return new DenyPattern(trimmed.TrimEnd('*').Split(' ', StringSplitOptions.RemoveEmptyEntries), prefix);
            }),
        ];

        public bool Matches(IReadOnlyList<string> words)
        {
            for (var start = 0; start + Words.Length <= words.Count; start++)
            {
                var match = true;
                for (var i = 0; i < Words.Length && match; i++)
                {
                    var last = i == Words.Length - 1;
                    match = last && Prefix
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

/// <summary>Placeholder scanning shared by the validator, the value resolver and templates.</summary>
public static class Placeholders
{
    /// <param name="Keys">The distinct placeholder contents in order of appearance (not necessarily valid keys).</param>
    /// <param name="Balanced">False when a brace is unmatched or nested.</param>
    /// <param name="Prose">The text with every placeholder replaced by a space.</param>
    /// <param name="Words">The lower-case letter runs of <paramref name="Prose"/>.</param>
    public sealed record Result(IReadOnlyList<string> Keys, bool Balanced, string Prose, IReadOnlyList<string> Words);

    public static Result Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var keys = new List<string>();
        var prose = new StringBuilder(text.Length);
        var balanced = true;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '}')
            {
                balanced = false;
                i++;
                continue;
            }

            if (c != '{')
            {
                prose.Append(c);
                i++;
                continue;
            }

            var close = text.IndexOf('}', i + 1);
            var nextOpen = text.IndexOf('{', i + 1);
            if (close < 0 || (nextOpen >= 0 && nextOpen < close))
            {
                balanced = false;
                i++;
                continue;
            }

            var key = text[(i + 1)..close];
            if (!keys.Contains(key, StringComparer.Ordinal))
            {
                keys.Add(key);
            }

            prose.Append(' ');
            i = close + 1;
        }

        var proseText = prose.ToString();
        return new Result(keys, balanced, proseText, Words(proseText));
    }

    public static List<string> Words(string prose)
    {
        ArgumentNullException.ThrowIfNull(prose);
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in prose)
        {
            if (char.IsLetter(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
