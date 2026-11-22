using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// The canonical JSON of a <see cref="PlanFactSheet"/> (ADR 0040 §3) and its SHA-256 fingerprint: members in a fixed order, no whitespace,
/// numbers in the shortest round-trip invariant form, UTF-8, minimal escaping; these bytes are exactly what the model receives. Members that
/// carry no information are omitted (null values, <c>fromThisGoal: false</c>, a <c>planned</c> kind) to keep 200 commitments within the size cap.
/// Enums are lowerCamel codes (the schema codes of ADR 0040 §4); weekdays are lowercase English names.
/// </summary>
public static class PlanFactSheetJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] ToUtf8Bytes(PlanFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var buffer = new ArrayBufferWriter<byte>(8192);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("factSheetVersion", sheet.FactSheetVersion);
            writer.WriteString("today", Date(sheet.Today));
            writer.WriteString("language", sheet.Language);
            WriteGoal(writer, sheet.Goal);
            WriteHistory(writer, sheet.History);
            writer.WriteStartArray("commitments");
            foreach (var c in sheet.Commitments)
            {
                WriteCommitment(writer, c);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("dismissed");
            foreach (var d in sheet.Dismissed)
            {
                writer.WriteStartObject();
                writer.WriteString("date", Date(d.Date));
                writer.WriteString("runType", Code(d.RunTypeId));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteEnvelope(writer, sheet.Envelope);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static string ToJson(PlanFactSheet sheet) => System.Text.Encoding.UTF8.GetString(ToUtf8Bytes(sheet));

    /// <summary>SHA-256 of the canonical bytes.</summary>
    public static byte[] Fingerprint(PlanFactSheet sheet) => SHA256.HashData(ToUtf8Bytes(sheet));

    private static void WriteGoal(Utf8JsonWriter w, PlanGoalFacts g)
    {
        w.WriteStartObject("goal");
        w.WriteString("type", Lower(g.Type.ToString()));
        w.WriteNumber("distanceMeters", g.DistanceMeters);
        Number(w, "elevationGainMeters", g.ElevationGainMeters);
        if (g.Surface is { } surface)
        {
            w.WriteString("surface", Lower(surface.ToString()));
        }

        w.WriteString("targetDate", Date(g.TargetDate));
        Number(w, "targetDurationSeconds", g.TargetDurationSeconds);
        Number(w, "targetPaceSecondsPerKm", g.TargetPaceSecondsPerKm);
        Number(w, "predictedDurationSeconds", g.PredictedDurationSeconds);
        if (g.PredictionBasis is { } basis)
        {
            w.WriteString("predictionBasis", BasisCode(basis));
        }

        if (g.PredictionConfidence is { } confidence)
        {
            w.WriteString("predictionConfidence", Lower(confidence.ToString()));
        }

        Number(w, "fitnessPaceSecondsPerKm", g.FitnessPaceSecondsPerKm);
        w.WriteBoolean("ambitiousTarget", g.AmbitiousTarget);
        w.WriteNumber("weeksToGoal", g.WeeksToGoal);
        w.WriteEndObject();
    }

    private static void WriteHistory(Utf8JsonWriter w, PlanHistoryFacts h)
    {
        w.WriteStartObject("history");
        w.WriteStartArray("weeks");
        foreach (var week in h.Weeks)
        {
            w.WriteStartObject();
            w.WriteString("start", Date(week.Start));
            w.WriteNumber("distanceMeters", week.DistanceMeters);
            w.WriteNumber("durationSeconds", week.DurationSeconds);
            w.WriteNumber("sessions", week.Sessions);
            w.WriteNumber("longestRunMeters", week.LongestRunMeters);
            w.WriteNumber("hardSessions", week.HardSessions);
            w.WriteNumber("load", week.Load);
            Number(w, "acwrAtEnd", week.AcwrAtEnd);
            Band(w, "acwrBand", week.AcwrBand);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("usualWeekdays");
        foreach (var day in h.UsualWeekdays)
        {
            w.WriteStringValue(Lower(day.ToString()));
        }

        w.WriteEndArray();
        w.WriteNumber("sessionsPerWeek", h.SessionsPerWeek);
        Number(w, "recentEasyPaceSecondsPerKm", h.RecentEasyPaceSecondsPerKm);
        w.WriteStartObject("latestAcwr");
        Number(w, "value", h.LatestAcwr.Value);
        Band(w, "band", h.LatestAcwr.Band);
        w.WriteEndObject();
        w.WriteStartArray("races");
        foreach (var race in h.Races)
        {
            w.WriteStartObject();
            w.WriteString("date", Date(race.Date));
            w.WriteNumber("distanceMeters", race.DistanceMeters);
            w.WriteNumber("durationSeconds", race.DurationSeconds);
            w.WriteString("runType", race.RunType);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteCommitment(Utf8JsonWriter w, CommitmentFact c)
    {
        w.WriteStartObject();
        w.WriteString("date", Date(c.Date));
        if (c.RunTypeId is { } typeId)
        {
            w.WriteString("runType", Code(typeId));
        }

        Number(w, "distanceMeters", c.DistanceMeters);
        Number(w, "durationSeconds", c.DurationSeconds);
        if (c.Kind != CommitmentKind.Planned)
        {
            w.WriteString("kind", Lower(c.Kind.ToString()));
        }

        if (c.FromThisGoal)
        {
            w.WriteBoolean("fromThisGoal", true);
        }

        w.WriteEndObject();
    }

    private static void WriteEnvelope(Utf8JsonWriter w, PlanEnvelope e)
    {
        w.WriteStartObject("envelope");
        w.WriteString("planStart", Date(e.PlanStart));
        w.WriteString("planEnd", Date(e.PlanEnd));
        w.WriteBoolean("horizonCapped", e.HorizonCapped);
        w.WriteNumber("maxSessionsPerWeek", e.MaxSessionsPerWeek);
        Number(w, "referencePaceSecondsPerKm", e.ReferencePaceSecondsPerKm);
        Number(w, "easyPaceMinSecondsPerKm", e.EasyPaceMinSecondsPerKm);
        w.WriteStartArray("allowedRunTypes");
        foreach (var code in e.AllowedRunTypes)
        {
            w.WriteStringValue(code);
        }

        w.WriteEndArray();
        w.WriteStartArray("weeks");
        foreach (var week in e.Weeks)
        {
            w.WriteStartObject();
            w.WriteString("start", Date(week.Start));
            w.WriteString("phase", week.Phase.ToWire());
            w.WriteNumber("maxDistanceMeters", week.MaxDistanceMeters);
            w.WriteNumber("fixedDistanceMeters", week.FixedDistanceMeters);
            w.WriteNumber("availableDistanceMeters", week.AvailableDistanceMeters);
            w.WriteNumber("maxLongRunMeters", week.MaxLongRunMeters);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void Number(Utf8JsonWriter w, string name, double? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
    }

    private static void Number(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
    }

    private static void Band(Utf8JsonWriter w, string name, AcwrBand? band)
    {
        if (band is { } b)
        {
            w.WriteString(name, Lower(b.ToString()));
        }
    }

    /// <summary>v3 basis codes: <c>race</c>, <c>hardTraining</c> (a single RPE ≥ 8 run), <c>recentTraining</c> (the M9.1 fallback).</summary>
    private static string BasisCode(EffortKind kind) => kind switch
    {
        EffortKind.Race => "race",
        EffortKind.Training => "hardTraining",
        EffortKind.RecentTraining => "recentTraining",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Code(short runTypeId) => RunTypeCatalog.Get(runTypeId).Code;

    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
