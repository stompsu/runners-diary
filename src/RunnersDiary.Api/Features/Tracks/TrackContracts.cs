using System.Text.Json;
using System.Text.Json.Serialization;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Tracks;

namespace RunnersDiary.Api.Features.Tracks;

/// <summary>Why a track carries a caveat; derived from the stored warning flags.</summary>
public enum TrackWarningDto
{
    [JsonStringEnumMemberName("timestampsIgnored")]
    TimestampsIgnored,

    [JsonStringEnumMemberName("noElevation")]
    NoElevation,

    [JsonStringEnumMemberName("multipleSegments")]
    MultipleSegments,

    [JsonStringEnumMemberName("routeConverted")]
    RouteConverted,
}

public sealed record TrackPointDto
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }
}

/// <summary>The GPX start time in the run's current zone; <c>time</c> is HH:mm.</summary>
public sealed record TrackStartLocalDto
{
    public required DateOnly Date { get; init; }

    public required TimeOnly Time { get; init; }

    public required string TimeZoneId { get; init; }
}

public sealed record TrackSummaryDto
{
    public required int PointCount { get; init; }

    public required double DistanceMeters { get; init; }

    public required double? ElevationGainMeters { get; init; }

    public required double? ElevationLossMeters { get; init; }

    public required double? MinElevationMeters { get; init; }

    public required double? MaxElevationMeters { get; init; }

    public required int? ElapsedSeconds { get; init; }

    public required int? MovingSeconds { get; init; }

    public required DateTime? StartUtc { get; init; }

    public required DateTime? EndUtc { get; init; }

    public required TrackPointDto Start { get; init; }

    public required TrackPointDto Finish { get; init; }

    public required TrackStartLocalDto? StartLocal { get; init; }
}

/// <summary>A track of a run. <see cref="Summary"/> is null unless <see cref="Status"/> is <c>Processed</c> (pre-M4 demo rows stay <c>Pending</c>).</summary>
public sealed record TrackDto
{
    public required Guid Id { get; init; }

    public required Guid RunId { get; init; }

    public required GpxTrackKind Kind { get; init; }

    public required GpxTrackStatus Status { get; init; }

    public required string OriginalFileName { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required TrackSummaryDto? Summary { get; init; }

    public required IReadOnlyList<TrackWarningDto> Warnings { get; init; }
}

/// <summary>Multipart form of <c>uploadTrack</c>. Documents the request body; the endpoint reads the form itself so the size checks come first.</summary>
public sealed record UploadTrackForm
{
    public required GpxTrackKind Kind { get; init; }

    public required IFormFile File { get; init; }

    /// <summary>Must be true to replace an existing track of the same kind (otherwise 409 track-exists).</summary>
    public bool? Replace { get; init; }
}

// GeoJSON document of ADR 0021 section 5. It is streamed from blob storage and never serialized by the API; these records exist
// so the OpenAPI document (and the generated TS client) describes it.

public sealed record TrackGeoJsonProperties
{
    public required string Role { get; init; }
}

/// <summary><c>coordinates</c> is <c>[lon, lat]</c> for a Point and <c>[[[lon, lat], ...], ...]</c> for the MultiLineString.</summary>
public sealed record TrackGeoJsonGeometry
{
    public required string Type { get; init; }

    public required JsonElement Coordinates { get; init; }
}

public sealed record TrackGeoJsonFeature
{
    public required string Type { get; init; }

    public required string Id { get; init; }

    public required TrackGeoJsonProperties Properties { get; init; }

    public required TrackGeoJsonGeometry Geometry { get; init; }
}

public sealed record TrackGeoJsonSplit
{
    public required int Index { get; init; }

    public required double DistanceMeters { get; init; }

    public required int? ElapsedSeconds { get; init; }

    public required double? ElevationDeltaMeters { get; init; }
}

public sealed record TrackGeoJson
{
    public required string Type { get; init; }

    public required int FormatVersion { get; init; }

    /// <summary><c>[minLon, minLat, maxLon, maxLat]</c>.</summary>
    public required IReadOnlyList<double> Bbox { get; init; }

    public required IReadOnlyList<TrackGeoJsonFeature> Features { get; init; }

    /// <summary>Samples of <c>[distanceMeters, elevationMeters, lon, lat]</c>; null when the file has no elevation.</summary>
    public required IReadOnlyList<IReadOnlyList<double>>? Profile { get; init; }

    public required IReadOnlyList<TrackGeoJsonSplit> Splits { get; init; }
}

internal static class TrackMapping
{
    public static TrackDto ToDto(GpxTrack track, string runTimeZoneId) => new()
    {
        Id = track.Id,
        RunId = track.RunId,
        Kind = track.Kind,
        Status = track.Status,
        OriginalFileName = track.OriginalFileName,
        SizeBytes = track.SizeBytes,
        CreatedAt = track.CreatedAt,
        Summary = SummaryOf(track, runTimeZoneId),
        Warnings = track.Status == GpxTrackStatus.Processed ? WarningsOf(track.WarningFlags) : [],
    };

    private static TrackSummaryDto? SummaryOf(GpxTrack t, string zone)
    {
        if (t.Status != GpxTrackStatus.Processed || t.DistanceMeters is not { } distance || t.PointCount is not { } points
            || t.StartPoint is not { } start || t.FinishPoint is not { } finish)
        {
            return null;
        }

        var local = TrackStartLocal.For(t.StartUtc, zone);
        return new TrackSummaryDto
        {
            PointCount = points,
            DistanceMeters = distance,
            ElevationGainMeters = t.ElevationGainMeters,
            ElevationLossMeters = t.ElevationLossMeters,
            MinElevationMeters = t.MinElevationMeters,
            MaxElevationMeters = t.MaxElevationMeters,
            ElapsedSeconds = t.ElapsedSeconds,
            MovingSeconds = t.MovingSeconds,
            StartUtc = t.StartUtc,
            EndUtc = t.EndUtc,
            Start = new TrackPointDto { Latitude = start.Latitude, Longitude = start.Longitude },
            Finish = new TrackPointDto { Latitude = finish.Latitude, Longitude = finish.Longitude },
            StartLocal = local is null ? null : new TrackStartLocalDto { Date = local.Date, Time = local.Time, TimeZoneId = local.TimeZoneId },
        };
    }

    private static List<TrackWarningDto> WarningsOf(TrackWarnings flags)
    {
        var list = new List<TrackWarningDto>();
        if (flags.HasFlag(TrackWarnings.TimestampsIgnored)) { list.Add(TrackWarningDto.TimestampsIgnored); }
        if (flags.HasFlag(TrackWarnings.NoElevation)) { list.Add(TrackWarningDto.NoElevation); }
        if (flags.HasFlag(TrackWarnings.MultipleSegments)) { list.Add(TrackWarningDto.MultipleSegments); }
        if (flags.HasFlag(TrackWarnings.RouteConverted)) { list.Add(TrackWarningDto.RouteConverted); }
        return list;
    }
}
