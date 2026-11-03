using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;

namespace RunnersDiary.Domain.Demo;

/// <summary>A week-relative demo dataset (seed/demo/runs.json). Parsing the JSON is the loader's job; this is the pure model.</summary>
public sealed record DemoTemplate(int Version, string TimeZoneId, IReadOnlyList<DemoRunTemplate> Runs);

/// <summary>GPX file reference (path relative to seed/demo/) and its kind.</summary>
public sealed record DemoGpxRef(string File, GpxTrackKind Kind);

/// <summary>
/// One template entry. <see cref="Week"/> 0 is the current ISO week in the dataset zone, negative = past, positive = future.
/// <see cref="TypeCode"/> is a run type code from the catalog.
/// </summary>
public sealed record DemoRunTemplate(
    int Week,
    DayOfWeek Day,
    string TypeCode,
    RunStatus Status = RunStatus.Completed,
    TimeOnly? StartTime = null,
    string? Title = null,
    string? Notes = null,
    RunLocation? Location = null,
    double? DistanceMeters = null,
    int? DurationSeconds = null,
    double? ElevationGainMeters = null,
    double? ElevationLossMeters = null,
    int? AvgHeartRate = null,
    int? MaxHeartRate = null,
    int? Rpe = null,
    string? Gear = null,
    string? Weather = null,
    Surface? Surface = null,
    RaceDetails? Race = null,
    RunRating? Rating = null,
    DemoGpxRef? Gpx = null);

/// <summary>A concrete dated run produced from a template.</summary>
public sealed record DemoRun(RunValues Values, DemoGpxRef? Gpx);
