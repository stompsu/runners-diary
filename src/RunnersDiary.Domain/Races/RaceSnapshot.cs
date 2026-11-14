using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Races;

/// <summary>Read projection of a catalogue race: the input of <see cref="RaceProposalBuilder"/> and <see cref="RaceRunMapper"/>.</summary>
public sealed record RaceSnapshot(
    Guid Id,
    RaceSource Source,
    string Name,
    DateOnly Date,
    TimeOnly? StartTime,
    string? TimeZoneId,
    string? LocationName,
    GeoPoint? Point,
    string CountryCode,
    string? RegionCode,
    RaceKind Kind,
    Surface? Surface,
    IReadOnlyList<double> DistancesMeters,
    double? ElevationGainMeters,
    DateTime ImportedUtc,
    DateTime? RemovedUtc = null,
    Guid? DuplicateOfRaceId = null);
