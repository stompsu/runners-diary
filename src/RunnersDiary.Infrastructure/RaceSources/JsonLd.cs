using System.Text.Json;
using AngleSharp.Dom;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary>
/// Reads schema.org JSON-LD out of an HTML document (ADR 0035 §6). The blocks are untrusted input: each one is parsed with <c>System.Text.Json</c>
/// at a nesting depth of at most 32, a malformed block is skipped and counted (never fatal), and the elements are cloned so no
/// <see cref="JsonDocument"/> outlives the call.
/// </summary>
internal static class JsonLd
{
    public const int MaxDepth = 32;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = MaxDepth, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>The JSON objects of every <c>application/ld+json</c> block, with <c>@graph</c> members and top-level arrays flattened.</summary>
    public static (IReadOnlyList<JsonElement> Objects, int MalformedBlocks) Extract(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var objects = new List<JsonElement>();
        var malformed = 0;
        foreach (var script in document.QuerySelectorAll("script[type=\"application/ld+json\"]"))
        {
            var text = script.TextContent;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(text, Options);
                Flatten(json.RootElement, objects);
            }
            catch (JsonException)
            {
                malformed++;
            }
        }

        return (objects, malformed);
    }

    private static void Flatten(JsonElement element, List<JsonElement> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, into);
                }

                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in graph.EnumerateArray())
                    {
                        Flatten(item, into);
                    }
                }

                into.Add(element.Clone());
                break;
            default:
                break;
        }
    }

    /// <summary>True when the object's <c>@type</c> (a string or an array of strings) contains <paramref name="type"/>.</summary>
    public static bool HasType(JsonElement element, string type)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("@type", out var t))
        {
            return false;
        }

        return t.ValueKind switch
        {
            JsonValueKind.String => string.Equals(t.GetString(), type, StringComparison.Ordinal),
            JsonValueKind.Array => t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && string.Equals(x.GetString(), type, StringComparison.Ordinal)),
            _ => false,
        };
    }

    public static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static JsonElement? GetObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    /// <summary>A number that is a JSON number or a numeric string (some sites quote coordinates).</summary>
    public static double? GetDouble(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
