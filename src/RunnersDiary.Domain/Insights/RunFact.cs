using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Insights;

/// <summary>The fields of a run that the metrics need (a projection; no notes, locations or tracks).</summary>
public sealed record RunFact(
    Guid Id,
    DateOnly Date,
    RunStatus Status,
    short RunTypeId,
    double? DistanceMeters,
    int? DurationSeconds,
    double? ElevationGainMeters,
    int? Rpe,
    Surface? Surface,
    int? AvgHeartRate);

/// <summary>A race-category run with its race details and rating, for the results history.</summary>
public sealed record RaceFact(
    Guid Id,
    DateOnly Date,
    RunStatus Status,
    short RunTypeId,
    string Name,
    RaceDistanceCategory Category,
    double? OfficialDistanceMeters,
    double? DistanceMeters,
    int? DurationSeconds,
    int? ResultTimeSeconds,
    int? TargetTimeSeconds,
    int? OverallPlace,
    int? CategoryPlace,
    string? CategoryName,
    int? RatingStars);
