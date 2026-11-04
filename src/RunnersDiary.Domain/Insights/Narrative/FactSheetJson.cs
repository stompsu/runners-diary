using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>
/// The canonical JSON of a <see cref="FactSheet"/> and its SHA-256 fingerprint (ADR 0026 §1, ADR 0028 §2). Canonical means: facts in
/// builder order, members in the fixed order <c>key, value, unit, display</c>, no whitespace, numbers in the shortest round-trip
/// invariant form, UTF-8, minimal escaping. The bytes are culture-independent, and they are exactly what the model receives.
/// </summary>
public static class FactSheetJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        // Readable for the model ("+6.4 %" rather than "+6.4 %"); still valid JSON, and the output is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] ToUtf8Bytes(FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("factSheetVersion", sheet.FactSheetVersion);
            writer.WriteNumber("metricsVersion", sheet.MetricsVersion);
            writer.WriteString("language", sheet.Language);
            writer.WriteStartArray("facts");
            foreach (var fact in sheet.Facts)
            {
                writer.WriteStartObject();
                writer.WriteString("key", fact.Key);
                writer.WritePropertyName("value");
                WriteValue(writer, fact.Value);
                writer.WriteString("unit", fact.Unit.ToWire());
                writer.WriteString("display", fact.Display);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static string ToJson(FactSheet sheet) => System.Text.Encoding.UTF8.GetString(ToUtf8Bytes(sheet));

    /// <summary>SHA-256 of the canonical bytes: the data part of a cached summary's validity tuple.</summary>
    public static byte[] Fingerprint(FactSheet sheet) => SHA256.HashData(ToUtf8Bytes(sheet));

    /// <summary>Reads a canonical fact sheet back (tests, the rule-based fake client). Throws <see cref="JsonException"/> when malformed.</summary>
    public static FactSheet Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var facts = new List<Fact>();
        foreach (var item in root.GetProperty("facts").EnumerateArray())
        {
            var value = item.GetProperty("value");
            var factValue = value.ValueKind switch
            {
                JsonValueKind.Number => FactValue.Of(value.GetDouble()),
                JsonValueKind.String => FactValue.Of(value.GetString()!),
                JsonValueKind.True => FactValue.Of(true),
                JsonValueKind.False => FactValue.Of(false),
                _ => throw new JsonException("A fact value must be a number, a string or a boolean."),
            };
            facts.Add(new Fact(
                item.GetProperty("key").GetString()!,
                factValue,
                FactUnits.FromWire(item.GetProperty("unit").GetString()!),
                item.GetProperty("display").GetString()!));
        }

        return new FactSheet(
            root.GetProperty("factSheetVersion").GetInt32(),
            root.GetProperty("metricsVersion").GetInt32(),
            root.GetProperty("language").GetString()!,
            facts);
    }

    internal static void WriteValue(Utf8JsonWriter writer, FactValue value)
    {
        if (value.Number is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else if (value.Flag is { } flag)
        {
            writer.WriteBooleanValue(flag);
        }
        else
        {
            writer.WriteStringValue(value.Text);
        }
    }
}
