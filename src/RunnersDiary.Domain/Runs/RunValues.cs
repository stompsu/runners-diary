namespace RunnersDiary.Domain.Runs;

/// <summary>The writable part of a run: the single input shape for <see cref="RunRules"/>, <see cref="Run.Create"/> and <see cref="Run.Update"/>.</summary>
public sealed record RunValues
{
    public DateOnly Date { get; init; }

    public TimeOnly? StartTime { get; init; }

    public string TimeZoneId { get; init; } = string.Empty;

    public RunStatus Status { get; init; }

    public short RunTypeId { get; init; }

    public string? Title { get; init; }

    public string? Notes { get; init; }

    public RunLocation? Location { get; init; }

    public double? DistanceMeters { get; init; }

    public int? DurationSeconds { get; init; }

    public double? ElevationGainMeters { get; init; }

    public double? ElevationLossMeters { get; init; }

    public int? AvgHeartRate { get; init; }

    public int? MaxHeartRate { get; init; }

    public int? Rpe { get; init; }

    public string? Gear { get; init; }

    public string? Weather { get; init; }

    public Surface? Surface { get; init; }

    public RaceDetails? Race { get; init; }

    public RunRating? Rating { get; init; }
}
