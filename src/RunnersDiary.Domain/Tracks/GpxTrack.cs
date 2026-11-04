using System.Diagnostics.CodeAnalysis;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tracks;

/// <summary>GPX track metadata plus the blob reference (M2) and, once processed, the stored summary of the track (M4, ADR 0021 §7).</summary>
public sealed class GpxTrack : IUserOwned
{
    public const long MaxSizeBytes = 20L * 1024 * 1024;

    private GpxTrack()
    {
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    /// <summary>Denormalized owner (query filter, blob path). Intentionally has no foreign key.</summary>
    public Guid UserId { get; private set; }

    public GpxTrackKind Kind { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Raw SHA-256 digest mapped to binary(32); never mutated.")]
    public byte[] Sha256 { get; private set; } = [];

    public string RawBlobName { get; private set; } = string.Empty;

    public GpxTrackStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // Processed summary: all null while Pending (CK_GpxTracks_Processed requires the core members when Status = Processed).

    /// <summary>Blob holding the GeoJSON document (line, profile, splits); null until processed.</summary>
    public string? ProcessedBlobName { get; private set; }

    public byte? ProcessorVersion { get; private set; }

    public DateTime? ProcessedAt { get; private set; }

    public int? PointCount { get; private set; }

    public double? DistanceMeters { get; private set; }

    /// <summary>Null when the file has no elevation.</summary>
    public double? ElevationGainMeters { get; private set; }

    public double? ElevationLossMeters { get; private set; }

    public double? MinElevationMeters { get; private set; }

    public double? MaxElevationMeters { get; private set; }

    /// <summary>Null when the file is untimed.</summary>
    public int? ElapsedSeconds { get; private set; }

    public int? MovingSeconds { get; private set; }

    public DateTime? StartUtc { get; private set; }

    public DateTime? EndUtc { get; private set; }

    public GeoPoint? StartPoint { get; private set; }

    public GeoPoint? FinishPoint { get; private set; }

    public TrackWarnings WarningFlags { get; private set; }

    /// <summary>Creates a track that is already processed: the original and the GeoJSON blobs exist and the summary is stored.</summary>
    public static GpxTrack CreateProcessed(
        Guid id, Guid runId, Guid userId, GpxTrackKind kind, string originalFileName, long sizeBytes, byte[] sha256, string rawBlobName,
        string processedBlobName, ProcessedTrack processed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(processed);
        if (string.IsNullOrWhiteSpace(processedBlobName) || processedBlobName.Length > 400)
        {
            throw new DomainRuleException([new DomainError("processedBlobName", "Required, at most 400 characters.")]);
        }

        var track = CreatePending(id, runId, userId, kind, originalFileName, sizeBytes, sha256, rawBlobName, now);
        var s = processed.Summary;
        if (!GeoPoint.IsValid(s.StartLatitude, s.StartLongitude) || !GeoPoint.IsValid(s.FinishLatitude, s.FinishLongitude))
        {
            throw new DomainRuleException([new DomainError("summary", "Start and finish must be valid coordinates.")]);
        }

        track.Status = GpxTrackStatus.Processed;
        track.ProcessedBlobName = processedBlobName;
        track.ProcessorVersion = checked((byte)processed.ProcessorVersion);
        track.ProcessedAt = track.CreatedAt;
        track.PointCount = s.PointCount;
        track.DistanceMeters = s.DistanceMeters;
        track.ElevationGainMeters = s.ElevationGainMeters;
        track.ElevationLossMeters = s.ElevationLossMeters;
        track.MinElevationMeters = s.MinElevationMeters;
        track.MaxElevationMeters = s.MaxElevationMeters;
        track.ElapsedSeconds = s.ElapsedSeconds;
        track.MovingSeconds = s.MovingSeconds;
        track.StartUtc = ToStoredPrecision(s.StartUtc);
        track.EndUtc = ToStoredPrecision(s.EndUtc);
        track.StartPoint = new GeoPoint(s.StartLatitude, s.StartLongitude);
        track.FinishPoint = new GeoPoint(s.FinishLatitude, s.FinishLongitude);
        track.WarningFlags = s.Warnings;
        return track;
    }

    /// <summary>
    /// Whole seconds, halves rounded up like SQL Server's <c>datetime2(0)</c>, so the entity returned by the upload equals what a later read
    /// returns (otherwise a start just below a minute boundary would differ between the 201 and the GET).
    /// </summary>
    private static DateTime? ToStoredPrecision(DateTime? value) =>
        value is { } v ? new DateTime((v.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, v.Kind) : null;

    public static GpxTrack CreatePending(
        Guid id, Guid runId, Guid userId, GpxTrackKind kind, string originalFileName, long sizeBytes, byte[] sha256, string rawBlobName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        var errors = new List<DomainError>();
        if (id == Guid.Empty || runId == Guid.Empty || userId == Guid.Empty)
        {
            errors.Add(new DomainError("id", "Track, run and user ids are required."));
        }

        if (!Enum.IsDefined(kind))
        {
            errors.Add(new DomainError("kind", "Unknown track kind."));
        }

        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 255)
        {
            errors.Add(new DomainError("originalFileName", "Required, at most 255 characters."));
        }

        if (sizeBytes is <= 0 or > MaxSizeBytes)
        {
            errors.Add(new DomainError("sizeBytes", $"Must be between 1 and {MaxSizeBytes}."));
        }

        if (sha256.Length != 32)
        {
            errors.Add(new DomainError("sha256", "Must be 32 bytes."));
        }

        if (string.IsNullOrWhiteSpace(rawBlobName) || rawBlobName.Length > 400)
        {
            errors.Add(new DomainError("rawBlobName", "Required, at most 400 characters."));
        }

        if (errors.Count > 0)
        {
            throw new DomainRuleException(errors);
        }

        var utc = now.UtcDateTime;
        return new GpxTrack
        {
            Id = id,
            RunId = runId,
            UserId = userId,
            Kind = kind,
            OriginalFileName = originalFileName,
            SizeBytes = sizeBytes,
            Sha256 = (byte[])sha256.Clone(),
            RawBlobName = rawBlobName,
            Status = GpxTrackStatus.Pending,
            CreatedAt = new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
        };
    }
}
