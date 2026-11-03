using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;

namespace RunnersDiary.Domain.Demo;

/// <summary>
/// Parses <c>seed/demo/runs.json</c> into a <see cref="DemoTemplate"/>. Strict on purpose: unknown members, unknown enum names and
/// malformed values throw <see cref="DomainRuleException"/>, so a typo in the dataset fails a test instead of silently seeding wrong data.
/// </summary>
public static class DemoTemplateParser
{
    public const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static DemoTemplate Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var file = JsonSerializer.Deserialize<FileDto>(json, Options)
                ?? throw new DomainRuleException("The demo dataset is empty.");
            return Map(file);
        }
        catch (JsonException ex)
        {
            throw new DomainRuleException("The demo dataset is not valid: " + ex.Message, ex);
        }
    }

    private static DemoTemplate Map(FileDto file)
    {
        if (file.Version != SupportedVersion)
        {
            throw new DomainRuleException($"Unsupported demo dataset version {file.Version}.");
        }

        if (string.IsNullOrWhiteSpace(file.TimeZoneId) || file.Runs is null)
        {
            throw new DomainRuleException("The demo dataset needs timeZoneId and runs.");
        }

        return new DemoTemplate(file.Version, file.TimeZoneId, [.. file.Runs.Select(MapRun)]);
    }

    private static DemoRunTemplate MapRun(RunDto r) => new(
        r.Week,
        ParseDay(r.Day),
        r.Type ?? throw new DomainRuleException("A demo run needs a type."),
        r.Status ?? RunStatus.Completed,
        r.StartTime is null ? null : ParseTime(r.StartTime),
        r.Title,
        r.Notes,
        r.Location is null ? null : new RunLocation(new GeoPoint(r.Location.Latitude, r.Location.Longitude), r.Location.Name),
        r.DistanceMeters,
        r.DurationSeconds,
        r.ElevationGainMeters,
        r.ElevationLossMeters,
        r.AvgHeartRate,
        r.MaxHeartRate,
        r.Rpe,
        r.Gear,
        r.Weather,
        r.Surface,
        r.Race,
        r.Rating,
        r.Gpx);

    private static DayOfWeek ParseDay(string? day) => day switch
    {
        "Mon" => DayOfWeek.Monday,
        "Tue" => DayOfWeek.Tuesday,
        "Wed" => DayOfWeek.Wednesday,
        "Thu" => DayOfWeek.Thursday,
        "Fri" => DayOfWeek.Friday,
        "Sat" => DayOfWeek.Saturday,
        "Sun" => DayOfWeek.Sunday,
        _ => throw new DomainRuleException($"Unknown day '{day}' (use Mon..Sun)."),
    };

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new DomainRuleException($"Invalid startTime '{value}' (use HH:mm).");

    private sealed record FileDto(int Version, string? TimeZoneId, List<RunDto>? Runs);

    private sealed record LocationDto(double Latitude, double Longitude, string? Name);

    private sealed record RunDto(
        int Week,
        string? Day,
        string? Type,
        RunStatus? Status,
        string? StartTime,
        string? Title,
        string? Notes,
        LocationDto? Location,
        double? DistanceMeters,
        int? DurationSeconds,
        double? ElevationGainMeters,
        double? ElevationLossMeters,
        int? AvgHeartRate,
        int? MaxHeartRate,
        int? Rpe,
        string? Gear,
        string? Weather,
        Surface? Surface,
        RaceDetails? Race,
        RunRating? Rating,
        DemoGpxRef? Gpx);
}
