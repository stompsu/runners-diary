using System.Text.Encodings.Web;
using System.Text.Json;

namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>A resolved placeholder value for the client: a number or a text (dates, codes, bands, "true"/"false"), plus its unit.</summary>
public sealed record SummaryValue(double? Number, string? Text, FactUnit Unit);

/// <summary>A text with its placeholders and the keys it cites (contract <c>SummaryText</c>).</summary>
public sealed record SummaryText(string Text, IReadOnlyList<string> Keys);

/// <summary>
/// A validated narrative ready to be stored and returned (contract <c>InsightsSummaryDto</c> minus metadata): every text with its keys
/// and the resolved <see cref="Values"/> of every cited key. Stored as <c>AiSummaries.ContentJson</c> (ADR 0028 §1), so a stale
/// summary keeps the numbers it was written with.
/// </summary>
public sealed record SummaryContent(
    SummaryText Headline,
    IReadOnlyList<SummaryText> WentWell,
    IReadOnlyList<SummaryText> WatchOuts,
    SummaryText? Suggestion,
    IReadOnlyList<SummaryText> DataNotes,
    IReadOnlyDictionary<string, SummaryValue> Values);

/// <summary>Resolves the placeholders of a draft from its fact sheet (ADR 0026 §2: grounding by construction).</summary>
public static class SummaryValues
{
    /// <summary>The largest stored content (ADR 0028 / plan §3.4: <c>ContentJson</c> ≤ 16 KB, enforced in code).</summary>
    public const int MaxContentBytes = 16 * 1024;

    /// <summary>Resolves a draft that passed <see cref="SummaryValidator"/>; throws when a cited key is missing (a caller bug).</summary>
    public static SummaryContent Resolve(SummaryDraft draft, FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(sheet);

        var values = new SortedDictionary<string, SummaryValue>(StringComparer.Ordinal);

        SummaryText Text(string text)
        {
            var scan = Placeholders.Scan(text);
            foreach (var key in scan.Keys)
            {
                var fact = sheet.Find(key) ?? throw new InvalidOperationException($"The fact sheet has no key '{key}'.");
                values[key] = ToValue(fact);
            }

            return new SummaryText(text, scan.Keys);
        }

        return new SummaryContent(
            Text(draft.Headline),
            [.. draft.WentWell.Select(i => Text(i.Text))],
            [.. draft.WatchOuts.Select(i => Text(i.Text))],
            draft.Suggestion is null ? null : Text(draft.Suggestion.Text),
            [.. draft.DataNotes.Select(i => Text(i.Text))],
            values);
    }

    public static SummaryValue ToValue(Fact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return fact.Value.Number is { } number
            ? new SummaryValue(number, null, fact.Unit)
            : new SummaryValue(null, fact.Value.ToString(), fact.Unit);
    }
}

/// <summary>The JSON of <see cref="SummaryContent"/> (<c>AiSummaries.ContentJson</c>): the contract's camelCase shape.</summary>
public static class SummaryContentJson
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToJson(SummaryContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("headline");
            WriteText(writer, content.Headline);
            WriteTexts(writer, "wentWell", content.WentWell);
            WriteTexts(writer, "watchOuts", content.WatchOuts);
            writer.WritePropertyName("suggestion");
            if (content.Suggestion is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WriteText(writer, content.Suggestion);
            }

            WriteTexts(writer, "dataNotes", content.DataNotes);
            writer.WriteStartObject("values");
            foreach (var (key, value) in content.Values)
            {
                writer.WriteStartObject(key);
                if (value.Number is { } number)
                {
                    writer.WriteNumber("number", number);
                }
                else
                {
                    writer.WriteNull("number");
                }

                writer.WriteString("text", value.Text);
                writer.WriteString("unit", value.Unit.ToWire());
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        var json = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        return buffer.WrittenCount <= SummaryValues.MaxContentBytes
            ? json
            : throw new InvalidOperationException($"The summary content is {buffer.WrittenCount} bytes, above {SummaryValues.MaxContentBytes}.");
    }

    public static SummaryContent Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var values = new SortedDictionary<string, SummaryValue>(StringComparer.Ordinal);
        foreach (var property in root.GetProperty("values").EnumerateObject())
        {
            var v = property.Value;
            var number = v.GetProperty("number");
            var text = v.GetProperty("text");
            values[property.Name] = new SummaryValue(
                number.ValueKind == JsonValueKind.Null ? null : number.GetDouble(),
                text.ValueKind == JsonValueKind.Null ? null : text.GetString(),
                FactUnits.FromWire(v.GetProperty("unit").GetString()!));
        }

        var suggestion = root.GetProperty("suggestion");
        return new SummaryContent(
            ReadText(root.GetProperty("headline")),
            ReadTexts(root.GetProperty("wentWell")),
            ReadTexts(root.GetProperty("watchOuts")),
            suggestion.ValueKind == JsonValueKind.Null ? null : ReadText(suggestion),
            ReadTexts(root.GetProperty("dataNotes")),
            values);
    }

    private static void WriteTexts(Utf8JsonWriter writer, string name, IReadOnlyList<SummaryText> texts)
    {
        writer.WriteStartArray(name);
        foreach (var text in texts)
        {
            WriteText(writer, text);
        }

        writer.WriteEndArray();
    }

    private static void WriteText(Utf8JsonWriter writer, SummaryText text)
    {
        writer.WriteStartObject();
        writer.WriteString("text", text.Text);
        writer.WriteStartArray("keys");
        foreach (var key in text.Keys)
        {
            writer.WriteStringValue(key);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static List<SummaryText> ReadTexts(JsonElement array) => [.. array.EnumerateArray().Select(ReadText)];

    private static SummaryText ReadText(JsonElement element) =>
        new(element.GetProperty("text").GetString()!, [.. element.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!)]);
}
