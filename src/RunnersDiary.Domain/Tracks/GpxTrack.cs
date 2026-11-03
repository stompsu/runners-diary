using System.Diagnostics.CodeAnalysis;
using RunnersDiary.Domain.Common;

namespace RunnersDiary.Domain.Tracks;

/// <summary>GPX track metadata plus the blob reference (M2). Parsed metrics and GeoJSON arrive in M4.</summary>
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
