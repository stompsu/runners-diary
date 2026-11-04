namespace RunnersDiary.Domain.Gpx;

/// <summary>Raised on the processed track as bit flags; stored as a tinyint (ADR 0021).</summary>
[Flags]
public enum TrackWarnings : byte
{
    None = 0,
    TimestampsIgnored = 1,
    NoElevation = 2,
    MultipleSegments = 4,
    RouteConverted = 8,
}

/// <summary>One track point. <see cref="Elevation"/> and <see cref="TimeUtc"/> are null when absent or unusable.</summary>
public readonly record struct GpxPoint(double Latitude, double Longitude, double? Elevation, DateTime? TimeUtc);

public sealed record GpxSegment(GpxPoint[] Points);

/// <summary>A validated GPX file: segments of points plus the flags derived while reading.</summary>
public sealed class GpxDocument
{
    public GpxDocument(IReadOnlyList<GpxSegment> segments, bool isTimed, TrackWarnings warnings)
    {
        Segments = segments;
        IsTimed = isTimed;
        Warnings = warnings;
        PointCount = segments.Sum(s => s.Points.Length);
    }

    public IReadOnlyList<GpxSegment> Segments { get; }

    /// <summary>True only when every point has a usable, non-decreasing timestamp.</summary>
    public bool IsTimed { get; }

    public TrackWarnings Warnings { get; }

    public int PointCount { get; }

    public bool HasElevation => Segments.Any(s => s.Points.Any(p => p.Elevation is not null));
}

/// <summary>Result of <see cref="GpxReader.Read"/>: exactly one of <see cref="Document"/> and <see cref="Rejection"/> is set.</summary>
public sealed record GpxReadResult(GpxDocument? Document, GpxRejection? Rejection)
{
    public bool IsSuccess => Document is not null;

    public static GpxReadResult Ok(GpxDocument document) => new(document, null);

    public static GpxReadResult Reject(GpxRejectionCode code) => new(null, GpxRejection.For(code));
}
