using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// Reads and writes the JSON of a <see cref="TrainingPlanDraft"/> in the shape of the strict output schema (ADR 0040 §4). Parsing is as strict as
/// the schema: an object with exactly <c>notes</c> and <c>sessions</c>, each session with exactly the nine members and their types. Anything else
/// (a truncated answer, a missing or extra member, a wrong type) fails as a whole: the validator reports <c>schema</c>.
/// </summary>
public static class TrainingPlanDraftJson
{
    public static readonly IReadOnlyList<string> SessionMembers =
        ["date", "runType", "intensity", "phase", "distanceMeters", "durationSeconds", "paceMinSecondsPerKm", "paceMaxSecondsPerKm", "description"];

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The draft, or null when the text is not a schema-shaped plan.</summary>
    public static TrainingPlanDraft? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactly(root, ["notes", "sessions"]))
            {
                return null;
            }

            var notes = root.GetProperty("notes");
            var sessions = root.GetProperty("sessions");
            if (notes.ValueKind != JsonValueKind.String || sessions.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var list = new List<DraftSession>(sessions.GetArrayLength());
            foreach (var s in sessions.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object || !HasExactly(s, SessionMembers)
                    || !TryString(s, "date", out var date) || !TryString(s, "runType", out var runType)
                    || !TryString(s, "intensity", out var intensity) || !TryString(s, "phase", out var phase)
                    || !TryString(s, "description", out var description)
                    || !TryNumber(s, "distanceMeters", out var distance)
                    || !TryInteger(s, "durationSeconds", out var duration)
                    || !TryInteger(s, "paceMinSecondsPerKm", out var paceMin)
                    || !TryInteger(s, "paceMaxSecondsPerKm", out var paceMax))
                {
                    return null;
                }

                list.Add(new DraftSession(date, runType, intensity, phase, distance, duration, paceMin, paceMax, description));
            }

            return new TrainingPlanDraft(notes.GetString()!, list);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The draft as the model would write it (schema member order, compact). Used by the fake model and the fixtures.</summary>
    public static string ToJson(TrainingPlanDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("notes", draft.Notes);
            w.WriteStartArray("sessions");
            foreach (var s in draft.Sessions)
            {
                w.WriteStartObject();
                w.WriteString("date", s.Date);
                w.WriteString("runType", s.RunType);
                w.WriteString("intensity", s.Intensity);
                w.WriteString("phase", s.Phase);
                if (s.DistanceMeters is { } d)
                {
                    w.WriteNumber("distanceMeters", d);
                }
                else
                {
                    w.WriteNull("distanceMeters");
                }

                WriteInt(w, "durationSeconds", s.DurationSeconds);
                WriteInt(w, "paceMinSecondsPerKm", s.PaceMinSecondsPerKm);
                WriteInt(w, "paceMaxSecondsPerKm", s.PaceMaxSecondsPerKm);
                w.WriteString("description", s.Description);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteInt(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static bool HasExactly(JsonElement obj, IReadOnlyList<string> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return seen.Count == names.Count;
    }

    private static bool TryString(JsonElement obj, string name, out string value)
    {
        var element = obj.GetProperty(name);
        value = element.ValueKind == JsonValueKind.String ? element.GetString()! : string.Empty;
        return element.ValueKind == JsonValueKind.String;
    }

    private static bool TryNumber(JsonElement obj, string name, out double? value)
    {
        var element = obj.GetProperty(name);
        value = null;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var d) && double.IsFinite(d))
        {
            value = d;
            return true;
        }

        return false;
    }

    private static bool TryInteger(JsonElement obj, string name, out int? value)
    {
        var element = obj.GetProperty(name);
        value = null;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var i))
        {
            value = i;
            return true;
        }

        return false;
    }
}
