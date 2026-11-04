using System.Text.Json;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Common;

namespace RunnersDiary.Domain.Insights.Narrative;

public enum PredicateOperator
{
    Eq,
    Lt,
    Lte,
    Gt,
    Gte,
    Exists,
}

/// <summary>
/// A claim a text makes, checked against a fact sheet: the <c>requires</c> of a demo fixture (ADR 0028 §5) and the <c>when</c> of a
/// template variant (ADR 0026 §4).
/// </summary>
public sealed record FactPredicate(string Key, PredicateOperator Op, FactValue? Value)
{
    /// <summary>Parses <c>[{ "key": ..., "op": "eq|lt|lte|gt|gte|exists", "value": number|string|bool }]</c>.</summary>
    public static List<FactPredicate> ParseAll(JsonElement array)
    {
        var predicates = new List<FactPredicate>();
        foreach (var item in array.EnumerateArray())
        {
            var key = item.GetProperty("key").GetString()!;
            if (!FactKey.IsValid(key))
            {
                throw new DomainRuleException($"Invalid predicate key '{key}'.");
            }

            var op = ParseOperator(item.GetProperty("op").GetString());
            FactValue? value = null;
            if (op != PredicateOperator.Exists)
            {
                var element = item.GetProperty("value");
                value = element.ValueKind switch
                {
                    JsonValueKind.Number => FactValue.Of(element.GetDouble()),
                    JsonValueKind.String => FactValue.Of(element.GetString()!),
                    JsonValueKind.True => FactValue.Of(true),
                    JsonValueKind.False => FactValue.Of(false),
                    _ => throw new DomainRuleException($"Predicate '{key}' needs a value."),
                };
            }

            predicates.Add(new FactPredicate(key, op, value));
        }

        return predicates;
    }

    private static PredicateOperator ParseOperator(string? op) => op switch
    {
        "eq" => PredicateOperator.Eq,
        "lt" => PredicateOperator.Lt,
        "lte" => PredicateOperator.Lte,
        "gt" => PredicateOperator.Gt,
        "gte" => PredicateOperator.Gte,
        "exists" => PredicateOperator.Exists,
        _ => throw new DomainRuleException($"Unknown predicate operator '{op}'."),
    };

    public bool Holds(FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.Find(Key) is not { } fact)
        {
            return false;
        }

        if (Op == PredicateOperator.Exists)
        {
            return true;
        }

        var actual = fact.Value;
        var expected = Value!.Value;
        if (Op == PredicateOperator.Eq)
        {
            return actual.Equals(expected);
        }

        // Ordering: numbers numerically, texts (ISO dates) ordinally; flags cannot be ordered.
        int? comparison = actual.Number is { } a && expected.Number is { } e ? a.CompareTo(e)
            : actual.Text is { } at && expected.Text is { } et ? string.CompareOrdinal(at, et)
            : null;
        return comparison is { } c && Op switch
        {
            PredicateOperator.Lt => c < 0,
            PredicateOperator.Lte => c <= 0,
            PredicateOperator.Gt => c > 0,
            PredicateOperator.Gte => c >= 0,
            _ => false,
        };
    }
}

/// <summary>The period presets that can have a demo fixture; their ranges match the SPA's presets (<c>insightsRange.ts</c>).</summary>
public static class DemoPresets
{
    /// <summary>"Last 12 weeks" (Monday 77 days back to this Sunday) compared with the previous 12 weeks: the dashboard default.</summary>
    public const string TwelveWeeksPrevious = "12w-previous";

    public static (DateOnly From, DateOnly To, ComparisonKind Compare)? Range(string preset, DateOnly today)
    {
        if (preset != TwelveWeeksPrevious)
        {
            return null;
        }

        var monday = IsoWeek.MondayOf(today);
        return (monday.AddDays(-77), monday.AddDays(6), ComparisonKind.Previous);
    }

    public static bool Matches(string preset, DateOnly today, DateOnly from, DateOnly to, ComparisonKind compare) =>
        Range(preset, today) is { } range && range.From == from && range.To == to && range.Compare == compare;
}

/// <summary>
/// A hand-written example summary for a demo preset (ADR 0028 §5). Its text uses the placeholder format and resolves from the guest's
/// own fact sheet; it is served only while every <see cref="Requires"/> predicate holds, so it never contradicts the guest's data.
/// </summary>
public sealed record DemoSummaryFixture(string Preset, string Language, int Version, int FactSheetVersion, IReadOnlyList<FactPredicate> Requires, SummaryDraft Draft)
{
    public bool Holds(FactSheet sheet) => FirstFailing(sheet) is null;

    /// <summary>The first predicate that does not hold, or null.</summary>
    public FactPredicate? FirstFailing(FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return sheet.FactSheetVersion != FactSheetVersion
            ? new FactPredicate("factSheetVersion", PredicateOperator.Eq, FactValue.Of(FactSheetVersion))
            : Requires.FirstOrDefault(p => !p.Holds(sheet));
    }

    public static DemoSummaryFixture Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var requires = FactPredicate.ParseAll(root.GetProperty("requires"));
            var draft = SummaryDraftJson.Read(root.GetProperty("summary"))
                ?? throw new DomainRuleException("The fixture summary is not a valid draft.");
            return new DemoSummaryFixture(
                root.GetProperty("preset").GetString()!,
                root.GetProperty("language").GetString()!,
                root.GetProperty("version").GetInt32(),
                root.GetProperty("factSheetVersion").GetInt32(),
                requires,
                draft);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new DomainRuleException("The demo summary fixture is not valid: " + ex.Message, ex);
        }
    }
}
