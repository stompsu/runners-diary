namespace RunnersDiary.Domain.Gpx;

/// <summary>Everything stored/served for a track: summary (SQL), simplified line, bbox, profile and splits (blob). ADR 0021.</summary>
public sealed record ProcessedTrack(
    int ProcessorVersion,
    TrackSummary Summary,
    IReadOnlyList<IReadOnlyList<LonLat>> Line,
    LonLat Start,
    LonLat Finish,
    double[] BoundingBox,
    IReadOnlyList<ProfileSample>? Profile,
    IReadOnlyList<TrackSplit> Splits);

public static class TrackProcessor
{
    /// <summary>Bump when metrics or simplification rules change (stored per track).</summary>
    public const int ProcessorVersion = 1;

    public static ProcessedTrack Process(GpxDocument document) => Process(document, CancellationToken.None);

    /// <summary>Cancelling stops the (potentially CPU-heavy) simplification, so an aborted request frees its processing slot.</summary>
    public static ProcessedTrack Process(GpxDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var g = new TrackGeometry(document);
        var summary = TrackMetrics.Compute(g);
        var line = DouglasPeucker.Simplify(g, DouglasPeucker.StartToleranceMeters, DouglasPeucker.MaxVertices, cancellationToken);
        var bbox = new[]
        {
            Math.Round(g.Lon.Min(), 6), Math.Round(g.Lat.Min(), 6), Math.Round(g.Lon.Max(), 6), Math.Round(g.Lat.Max(), 6),
        };
        return new ProcessedTrack(
            ProcessorVersion,
            summary,
            line,
            new LonLat(Math.Round(g.Lon[0], 6), Math.Round(g.Lat[0], 6)),
            new LonLat(Math.Round(g.Lon[^1], 6), Math.Round(g.Lat[^1], 6)),
            bbox,
            ElevationProfile.Build(g),
            Splits.Compute(g));
    }
}
