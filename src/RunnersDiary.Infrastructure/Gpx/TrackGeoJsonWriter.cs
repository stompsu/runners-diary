using System.Text.Json;
using RunnersDiary.Domain.Gpx;

namespace RunnersDiary.Infrastructure.Gpx;

/// <summary>
/// Serializes a <see cref="ProcessedTrack"/> into the <c>track.json</c> document of ADR 0021 §5: an RFC 7946 FeatureCollection
/// (line, start, finish) with the foreign members <c>formatVersion</c>, <c>profile</c> and <c>splits</c>. Member order is fixed,
/// numbers are written culture-independently by <see cref="Utf8JsonWriter"/>, and the output is compact UTF-8.
/// </summary>
public static class TrackGeoJsonWriter
{
    public const int FormatVersion = 1;

    public const string ContentType = "application/geo+json";

    public static byte[] Write(ProcessedTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WriteNumber("formatVersion", FormatVersion);

            w.WriteStartArray("bbox");
            foreach (var v in track.BoundingBox)
            {
                w.WriteNumberValue(v);
            }

            w.WriteEndArray();

            w.WriteStartArray("features");
            w.WriteStartObject();
            WriteFeatureHead(w, "line");
            w.WriteStartObject("geometry");
            w.WriteString("type", "MultiLineString");
            w.WriteStartArray("coordinates");
            foreach (var segment in track.Line)
            {
                w.WriteStartArray();
                foreach (var p in segment)
                {
                    WritePosition(w, p.Longitude, p.Latitude);
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
            WritePointFeature(w, "start", track.Start);
            WritePointFeature(w, "finish", track.Finish);
            w.WriteEndArray();

            if (track.Profile is null)
            {
                w.WriteNull("profile");
            }
            else
            {
                w.WriteStartArray("profile");
                foreach (var s in track.Profile)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(s.DistanceMeters);
                    w.WriteNumberValue(s.ElevationMeters);
                    w.WriteNumberValue(s.Longitude);
                    w.WriteNumberValue(s.Latitude);
                    w.WriteEndArray();
                }

                w.WriteEndArray();
            }

            w.WriteStartArray("splits");
            foreach (var s in track.Splits)
            {
                w.WriteStartObject();
                w.WriteNumber("index", s.Index);
                w.WriteNumber("distanceMeters", s.DistanceMeters);
                WriteNullable(w, "elapsedSeconds", s.ElapsedSeconds);
                WriteNullable(w, "elevationDeltaMeters", s.ElevationDeltaMeters);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteFeatureHead(Utf8JsonWriter w, string role)
    {
        w.WriteString("type", "Feature");
        w.WriteString("id", role);
        w.WriteStartObject("properties");
        w.WriteString("role", role);
        w.WriteEndObject();
    }

    private static void WritePointFeature(Utf8JsonWriter w, string role, LonLat point)
    {
        w.WriteStartObject();
        WriteFeatureHead(w, role);
        w.WriteStartObject("geometry");
        w.WriteString("type", "Point");
        w.WritePropertyName("coordinates");
        WritePosition(w, point.Longitude, point.Latitude);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WritePosition(Utf8JsonWriter w, double longitude, double latitude)
    {
        w.WriteStartArray();
        w.WriteNumberValue(longitude);
        w.WriteNumberValue(latitude);
        w.WriteEndArray();
    }

    private static void WriteNullable(Utf8JsonWriter w, string name, int? value)
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

    private static void WriteNullable(Utf8JsonWriter w, string name, double? value)
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
}
