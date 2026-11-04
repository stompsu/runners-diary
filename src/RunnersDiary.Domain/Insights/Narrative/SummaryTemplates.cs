using System.Text.Json;
using RunnersDiary.Domain.Common;

namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>Periods that get a deterministic template instead of the model (ADR 0026 §4).</summary>
public enum SummaryTemplateKind
{
    /// <summary>No completed session in <c>[from, asOf]</c>.</summary>
    Empty,

    /// <summary>A wholly future period (<c>elapsedDays = 0</c>).</summary>
    PlannedOnly,

    /// <summary>Fewer than <see cref="SummaryTemplates.MinSessionsForModel"/> completed sessions.</summary>
    FewSessions,
}

/// <summary>Template selection; the texts live in <c>prompts/training-insights/templates/&lt;lang&gt;.json</c>.</summary>
public static class SummaryTemplates
{
    /// <summary>The model is called only from this many completed sessions (versioned with <see cref="FactSheet.CurrentVersion"/>).</summary>
    public const int MinSessionsForModel = 3;

    /// <summary>The template for <paramref name="report"/>, or null when the period is worth a model call.</summary>
    public static SummaryTemplateKind? Select(InsightsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Period.ElapsedDays == 0)
        {
            return SummaryTemplateKind.PlannedOnly;
        }

        var sessions = report.DataQuality.CompletedSessions;
        return sessions == 0 ? SummaryTemplateKind.Empty
            : sessions < MinSessionsForModel ? SummaryTemplateKind.FewSessions
            : null;
    }

    public static string ToCode(this SummaryTemplateKind kind) => kind switch
    {
        SummaryTemplateKind.Empty => "empty",
        SummaryTemplateKind.PlannedOnly => "plannedOnly",
        SummaryTemplateKind.FewSessions => "fewSessions",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown template kind."),
    };
}

/// <summary>One wording of a template: used when every <see cref="When"/> predicate holds for the period's fact sheet (an empty list always holds).</summary>
public sealed record SummaryTemplateVariant(IReadOnlyList<FactPredicate> When, SummaryDraft Draft)
{
    public bool Applies(FactSheet sheet) => When.All(p => p.Holds(sheet));
}

/// <summary>
/// The templates of one language. Their placeholders resolve from the period's own fact sheet, like a model draft. A template is either
/// one draft or an ordered list of variants (<c>{ "when": [predicates], "summary": draft }</c>), so a text can depend on facts such as
/// <c>period.isPartial</c> ("so far") or <c>dq.completedSessions</c> (singular/plural); the first applicable variant wins and the last
/// one must be unconditional.
/// </summary>
public sealed record SummaryTemplateSet(int Version, string Language, IReadOnlyDictionary<SummaryTemplateKind, IReadOnlyList<SummaryTemplateVariant>> Templates)
{
    public const int SupportedVersion = 1;

    public IReadOnlyList<SummaryTemplateVariant> Variants(SummaryTemplateKind kind) =>
        Templates.TryGetValue(kind, out var variants) ? variants : throw new KeyNotFoundException($"No '{kind.ToCode()}' template for '{Language}'.");

    /// <summary>The first variant of <paramref name="kind"/> whose predicates hold for <paramref name="sheet"/>.</summary>
    public SummaryDraft Get(SummaryTemplateKind kind, FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Variants(kind).First(v => v.Applies(sheet)).Draft;
    }

    /// <summary>Strict parse: every kind present, nothing else; each draft in the exact model-output shape; a final unconditional variant.</summary>
    public static SummaryTemplateSet Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var version = root.GetProperty("version").GetInt32();
            if (version != SupportedVersion)
            {
                throw new DomainRuleException($"Unsupported template file version {version}.");
            }

            var language = root.GetProperty("language").GetString() ?? throw new DomainRuleException("Templates need a language.");
            var templates = new Dictionary<SummaryTemplateKind, IReadOnlyList<SummaryTemplateVariant>>();
            foreach (var property in root.GetProperty("templates").EnumerateObject())
            {
                var kind = Enum.GetValues<SummaryTemplateKind>().Cast<SummaryTemplateKind?>().FirstOrDefault(k => k!.Value.ToCode() == property.Name)
                    ?? throw new DomainRuleException($"Unknown template '{property.Name}'.");
                templates[kind] = ParseVariants(property.Name, property.Value);
            }

            foreach (var kind in Enum.GetValues<SummaryTemplateKind>())
            {
                if (!templates.ContainsKey(kind))
                {
                    throw new DomainRuleException($"Template '{kind.ToCode()}' is missing.");
                }
            }

            return new SummaryTemplateSet(version, language, templates);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new DomainRuleException("The template file is not valid: " + ex.Message, ex);
        }
    }

    private static List<SummaryTemplateVariant> ParseVariants(string name, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            return [new SummaryTemplateVariant([], Draft(name, value))];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new DomainRuleException($"Template '{name}' must be a draft or a list of variants.");
        }

        var variants = new List<SummaryTemplateVariant>();
        foreach (var item in value.EnumerateArray())
        {
            foreach (var member in item.EnumerateObject())
            {
                if (member.Name is not ("when" or "summary"))
                {
                    throw new DomainRuleException($"Template '{name}' has an unknown variant member '{member.Name}'.");
                }
            }

            var when = item.TryGetProperty("when", out var predicates) ? FactPredicate.ParseAll(predicates) : [];
            variants.Add(new SummaryTemplateVariant(when, Draft(name, item.GetProperty("summary"))));
        }

        return variants.Count > 0 && variants[^1].When.Count == 0
            ? variants
            : throw new DomainRuleException($"The last variant of template '{name}' must have no 'when'.");
    }

    private static SummaryDraft Draft(string name, JsonElement element) =>
        SummaryDraftJson.Read(element) ?? throw new DomainRuleException($"Template '{name}' is not a valid draft.");
}
