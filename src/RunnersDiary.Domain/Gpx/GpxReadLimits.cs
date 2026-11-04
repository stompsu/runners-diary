namespace RunnersDiary.Domain.Gpx;

/// <summary>Bounds applied while reading an untrusted GPX stream (ADR 0020).</summary>
public sealed record GpxReadLimits
{
    public const long DefaultMaxBytes = 20L * 1024 * 1024;
    public const long GuestMaxBytes = 5L * 1024 * 1024;

    /// <summary>Maximum stream length in bytes; also used as <c>XmlReaderSettings.MaxCharactersInDocument</c>.</summary>
    public long MaxBytes { get; init; } = DefaultMaxBytes;

    public int MaxPoints { get; init; } = 250_000;

    public int MaxElements { get; init; } = 1_500_000;

    /// <summary>Maximum element depth (the root element has depth 0).</summary>
    public int MaxDepth { get; init; } = 16;

    public int MaxSegments { get; init; } = 1_000;

    public static GpxReadLimits Default { get; } = new();

    public static GpxReadLimits ForGuest { get; } = new() { MaxBytes = GuestMaxBytes };
}
