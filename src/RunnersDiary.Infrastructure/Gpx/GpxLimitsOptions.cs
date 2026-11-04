using RunnersDiary.Domain.Gpx;

namespace RunnersDiary.Infrastructure.Gpx;

/// <summary>GPX upload limits (<c>GpxLimits</c> section, ADR 0020 §7).</summary>
public sealed class GpxLimitsOptions
{
    public const string SectionName = "GpxLimits";

    public long MaxFileBytes { get; set; } = GpxReadLimits.DefaultMaxBytes;

    public long GuestMaxFileBytes { get; set; } = GpxReadLimits.GuestMaxBytes;

    public int UserMaxTracks { get; set; } = 2_000;

    public int GuestMaxTracks { get; set; } = 16;

    public int MaxPoints { get; set; } = 250_000;

    /// <summary>Uploads parsed at the same time per replica; the others wait <see cref="ProcessingWait"/> and then get 503.</summary>
    public int MaxConcurrentProcessing { get; set; } = 2;

    public TimeSpan ProcessingWait { get; set; } = TimeSpan.FromSeconds(5);

    public long MaxFileBytesFor(bool isGuest) => isGuest ? GuestMaxFileBytes : MaxFileBytes;

    public int MaxTracksFor(bool isGuest) => isGuest ? GuestMaxTracks : UserMaxTracks;

    public GpxReadLimits ReadLimitsFor(bool isGuest) => GpxReadLimits.Default with { MaxBytes = MaxFileBytesFor(isGuest), MaxPoints = MaxPoints };
}
