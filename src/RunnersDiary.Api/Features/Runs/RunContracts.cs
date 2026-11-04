using System.ComponentModel.DataAnnotations;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Api.Features.Runs;

public sealed record LocationDto
{
    [Range(-90.0, 90.0)]
    public required double Latitude { get; init; }

    [Range(-180.0, 180.0)]
    public required double Longitude { get; init; }

    [StringLength(200)]
    public string? Name { get; init; }
}

public sealed record RaceDto
{
    [StringLength(150)]
    public required string Name { get; init; }

    public required RaceDistanceCategory DistanceCategory { get; init; }

    public double? OfficialDistanceMeters { get; init; }

    [StringLength(20)]
    public string? BibNumber { get; init; }

    public int? TargetTimeSeconds { get; init; }

    public int? ResultTimeSeconds { get; init; }

    public int? OverallPlace { get; init; }

    public int? CategoryPlace { get; init; }

    [StringLength(50)]
    public string? CategoryName { get; init; }

    [StringLength(2048)]
    public string? ResultsUrl { get; init; }
}

public sealed record RatingDto
{
    [Range(1, 5)]
    public required int Stars { get; init; }

    [StringLength(500)]
    public string? Comment { get; init; }
}

/// <summary>Body of <c>POST /api/runs</c> and <c>PUT /api/runs/{id}</c> (full replace). Never carries a user id.</summary>
public sealed record RunWriteRequest : IValidatableObject
{
    private const string FallbackZone = "Etc/UTC";

    public required DateOnly Date { get; init; }

    /// <summary>Local wall-clock start, <c>HH:mm</c>.</summary>
    public TimeOnly? StartTime { get; init; }

    /// <summary>IANA zone; defaults to the profile zone.</summary>
    public string? TimeZoneId { get; init; }

    public required RunStatus Status { get; init; }

    public required short RunTypeId { get; init; }

    [StringLength(120)]
    public string? Title { get; init; }

    [StringLength(4000)]
    public string? Notes { get; init; }

    public LocationDto? Location { get; init; }

    public double? DistanceMeters { get; init; }

    public int? DurationSeconds { get; init; }

    public double? ElevationGainMeters { get; init; }

    public double? ElevationLossMeters { get; init; }

    public int? AvgHeartRate { get; init; }

    public int? MaxHeartRate { get; init; }

    public int? Rpe { get; init; }

    [StringLength(100)]
    public string? Gear { get; init; }

    [StringLength(200)]
    public string? Weather { get; init; }

    public Surface? Surface { get; init; }

    public RaceDto? Race { get; init; }

    public RatingDto? Rating { get; init; }

    /// <summary>Required on update: the <c>version</c> of the <c>RunDto</c> being replaced.</summary>
    public string? Version { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var locationValid = Location is null || GeoPoint.IsValid(Location.Latitude, Location.Longitude);
        if (!locationValid)
        {
            yield return new ValidationResult("Latitude must be in -90..90 and longitude in -180..180.", ["location"]);
        }

        // The profile zone is only known to the handler; any valid zone is enough to evaluate the other rules here.
        foreach (var error in RunRules.Validate(ToValues(TimeZoneId ?? FallbackZone, locationValid)))
        {
            yield return new ValidationResult(error.Message, [error.Path]);
        }
    }

    internal RunValues ToValues(string timeZoneId, bool includeLocation = true) => new()
    {
        Date = Date,
        StartTime = StartTime,
        TimeZoneId = timeZoneId,
        Status = Status,
        RunTypeId = RunTypeId,
        Title = Title,
        Notes = Notes,
        Location = includeLocation && Location is { } l ? new RunLocation(new GeoPoint(l.Latitude, l.Longitude), l.Name) : null,
        DistanceMeters = DistanceMeters,
        DurationSeconds = DurationSeconds,
        ElevationGainMeters = ElevationGainMeters,
        ElevationLossMeters = ElevationLossMeters,
        AvgHeartRate = AvgHeartRate,
        MaxHeartRate = MaxHeartRate,
        Rpe = Rpe,
        Gear = Gear,
        Weather = Weather,
        Surface = Surface,
        Race = Race is { } r
            ? new RaceDetails(
                r.Name, r.DistanceCategory, r.OfficialDistanceMeters, r.BibNumber, r.TargetTimeSeconds, r.ResultTimeSeconds,
                r.OverallPlace, r.CategoryPlace, r.CategoryName, r.ResultsUrl)
            : null,
        Rating = Rating is { } g ? new RunRating(g.Stars, g.Comment) : null,
    };
}

public sealed record RunTrackDto
{
    public required Guid Id { get; init; }

    public required Domain.Tracks.GpxTrackKind Kind { get; init; }

    public required Domain.Tracks.GpxTrackStatus Status { get; init; }

    public required string OriginalFileName { get; init; }

    public required DateTime CreatedAt { get; init; }
}

/// <summary>A run as returned by every read and write endpoint (list and detail share the shape).</summary>
public sealed record RunDto
{
    public required Guid Id { get; init; }

    public required DateOnly Date { get; init; }

    public required TimeOnly? StartTime { get; init; }

    public required string TimeZoneId { get; init; }

    /// <summary>Derived instant (UTC) for ordering; null without a start time.</summary>
    public required DateTime? StartUtc { get; init; }

    public required RunStatus Status { get; init; }

    public required short RunTypeId { get; init; }

    public required string? Title { get; init; }

    public required string? Notes { get; init; }

    public required LocationDto? Location { get; init; }

    public required double? DistanceMeters { get; init; }

    public required int? DurationSeconds { get; init; }

    public required double? ElevationGainMeters { get; init; }

    public required double? ElevationLossMeters { get; init; }

    public required int? AvgHeartRate { get; init; }

    public required int? MaxHeartRate { get; init; }

    public required int? Rpe { get; init; }

    public required string? Gear { get; init; }

    public required string? Weather { get; init; }

    public required Surface? Surface { get; init; }

    public required RaceDto? Race { get; init; }

    public required RatingDto? Rating { get; init; }

    /// <summary>Opaque concurrency token (base64 rowversion); send it back as <c>version</c> on update.</summary>
    public required string Version { get; init; }

    public required IReadOnlyList<RunTrackDto> Tracks { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public sealed record RunListResponse
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required IReadOnlyList<RunDto> Items { get; init; }
}
