namespace RunnersDiary.Domain.Runs;

/// <summary>Race-specific data of a run whose type is in the Race category.</summary>
public sealed record RaceDetails(
    string Name,
    RaceDistanceCategory DistanceCategory,
    double? OfficialDistanceMeters = null,
    string? BibNumber = null,
    int? TargetTimeSeconds = null,
    int? ResultTimeSeconds = null,
    int? OverallPlace = null,
    int? CategoryPlace = null,
    string? CategoryName = null,
    string? ResultsUrl = null);
