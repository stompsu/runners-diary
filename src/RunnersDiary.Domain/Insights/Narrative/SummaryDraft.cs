using System.Text.Encodings.Web;
using System.Text.Json;

namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>One narrative item; numbers, dates and categories appear only as <c>{key}</c> placeholders (ADR 0026 §2).</summary>
public sealed record SummaryItem(string Text);

/// <summary>
/// The model's output shape (ADR 0026 §2), also used by templates and demo fixtures. The JSON schema sent to the model is
/// generated from this type: every property required, no additional properties, <see cref="Suggestion"/> nullable.
/// Lengths and item counts are not expressible in a strict schema; <see cref="SummaryValidator"/> enforces them (rule <c>schema</c>).
/// </summary>
public sealed record SummaryDraft(
    string Headline,
    IReadOnlyList<SummaryItem> WentWell,
    IReadOnlyList<SummaryItem> WatchOuts,
    SummaryItem? Suggestion,
    IReadOnlyList<SummaryItem> DataNotes)
{
    /// <summary>Every text of the draft with its location (<c>headline</c>, <c>wentWell.1</c>, …, <c>suggestion</c>).</summary>
    public IEnumerable<(string Location, string Text)> Texts()
    {
        yield return ("headline", Headline);
        foreach (var (location, text) in Items())
        {
            yield return (location, text);
        }
    }

    /// <summary>The list items and the suggestion (not the headline).</summary>
    public IEnumerable<(string Location, string Text)> Items()
    {
        for (var i = 0; i < WentWell.Count; i++)
        {
            yield return ($"wentWell.{i + 1}", WentWell[i].Text);
        }

        for (var i = 0; i < WatchOuts.Count; i++)
        {
            yield return ($"watchOuts.{i + 1}", WatchOuts[i].Text);
        }

        if (Suggestion is not null)
        {
            yield return ("suggestion", Suggestion.Text);
        }

        for (var i = 0; i < DataNotes.Count; i++)
        {
            yield return ($"dataNotes.{i + 1}", DataNotes[i].Text);
        }
    }
}

/// <summary>Strict JSON reading and writing of <see cref="SummaryDraft"/> (the exact strict-schema shape, camelCase).</summary>
public static class SummaryDraftJson
{
    private static readonly string[] Members = ["headline", "wentWell", "watchOuts", "suggestion", "dataNotes"];

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Parses the model output. Returns null when it is not exactly the schema's shape: a JSON object with the five members (no
    /// others, no duplicates), strings where strings are expected, <c>{ "text": string }</c> items and a nullable suggestion.
    /// </summary>
    public static SummaryDraft? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static SummaryDraft Parse(string json) =>
        TryParse(json) ?? throw new JsonException("The text is not a valid summary draft.");

    internal static SummaryDraft? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !HasExactly(root, Members))
        {
            return null;
        }

        var headline = root.GetProperty("headline");
        if (headline.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var wentWell = Items(root.GetProperty("wentWell"));
        var watchOuts = Items(root.GetProperty("watchOuts"));
        var dataNotes = Items(root.GetProperty("dataNotes"));
        var suggestionElement = root.GetProperty("suggestion");
        SummaryItem? suggestion = null;
        if (suggestionElement.ValueKind != JsonValueKind.Null)
        {
            suggestion = Item(suggestionElement);
            if (suggestion is null)
            {
                return null;
            }
        }

        if (wentWell is null || watchOuts is null || dataNotes is null)
        {
            return null;
        }

        return new SummaryDraft(headline.GetString()!, wentWell, watchOuts, suggestion, dataNotes);
    }

    public static string ToJson(SummaryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            Write(writer, draft);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static void Write(Utf8JsonWriter writer, SummaryDraft draft)
    {
        writer.WriteStartObject();
        writer.WriteString("headline", draft.Headline);
        WriteItems(writer, "wentWell", draft.WentWell);
        WriteItems(writer, "watchOuts", draft.WatchOuts);
        writer.WritePropertyName("suggestion");
        if (draft.Suggestion is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteString("text", draft.Suggestion.Text);
            writer.WriteEndObject();
        }

        WriteItems(writer, "dataNotes", draft.DataNotes);
        writer.WriteEndObject();
    }

    private static void WriteItems(Utf8JsonWriter writer, string name, IReadOnlyList<SummaryItem> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items)
        {
            writer.WriteStartObject();
            writer.WriteString("text", item.Text);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static List<SummaryItem>? Items(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<SummaryItem>();
        foreach (var element in array.EnumerateArray())
        {
            if (Item(element) is not { } item)
            {
                return null;
            }

            items.Add(item);
        }

        return items;
    }

    private static SummaryItem? Item(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !HasExactly(element, ["text"]))
        {
            return null;
        }

        var text = element.GetProperty("text");
        return text.ValueKind == JsonValueKind.String ? new SummaryItem(text.GetString()!) : null;
    }

    private static bool HasExactly(JsonElement element, string[] names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return seen.Count == names.Length;
    }
}
