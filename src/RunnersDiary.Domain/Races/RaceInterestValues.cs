using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Races;

/// <summary>The writable part of a race interest profile: the single input shape for <see cref="RaceInterestRules"/> and <see cref="RaceInterestProfile"/> (ADR 0037 §1).</summary>
public sealed record RaceInterestValues
{
    public IReadOnlyList<RaceKind> Kinds { get; init; } = [];

    public IReadOnlyList<string> RegionCodes { get; init; } = [];

    public GeoPoint? Center { get; init; }

    public string? CenterName { get; init; }

    public int? RadiusMeters { get; init; }

    public double? MinDistanceMeters { get; init; }

    public double? MaxDistanceMeters { get; init; }

    public int HorizonDays { get; init; }

    public string? Keywords { get; init; }

    public bool ShowInCalendar { get; init; }

    /// <summary>The profile a new user starts from in the UI (ADR 0037 §1 defaults).</summary>
    public static RaceInterestValues Defaults { get; } = new()
    {
        Kinds = [RaceKind.Road, RaceKind.Trail],
        MinDistanceMeters = 5_000,
        MaxDistanceMeters = 42_195,
        HorizonDays = 120,
        ShowInCalendar = true,
    };
}
