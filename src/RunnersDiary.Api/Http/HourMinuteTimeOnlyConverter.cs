using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunnersDiary.Api.Http;

/// <summary>Wire format of a local start time: exactly <c>HH:mm</c> (ADR 0008); seconds are never exposed.</summary>
public sealed class HourMinuteTimeOnlyConverter : JsonConverter<TimeOnly>
{
    private const string Format = "HH':'mm";

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && TimeOnly.TryParseExact(reader.GetString(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        throw new JsonException("Expected a time of day formatted as HH:mm.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }
}
