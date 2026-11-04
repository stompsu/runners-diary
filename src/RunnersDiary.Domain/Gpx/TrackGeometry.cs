namespace RunnersDiary.Domain.Gpx;

/// <summary>Flattened arrays of a <see cref="GpxDocument"/> with cumulative distance (gaps between segments add nothing).</summary>
internal sealed class TrackGeometry
{
    public TrackGeometry(GpxDocument document)
    {
        Document = document;
        var n = document.PointCount;
        Lat = new double[n];
        Lon = new double[n];
        Cum = new double[n];
        Ele = new double[n];
        TimeSeconds = document.IsTimed ? new double[n] : null;
        SegmentStart = new int[document.Segments.Count + 1];

        DateTime t0 = default;
        var haveT0 = false;
        var i = 0;
        for (var s = 0; s < document.Segments.Count; s++)
        {
            SegmentStart[s] = i;
            foreach (var p in document.Segments[s].Points)
            {
                Lat[i] = p.Latitude;
                Lon[i] = p.Longitude;
                Ele[i] = p.Elevation ?? double.NaN;
                if (i > 0)
                {
                    Cum[i] = Cum[i - 1] + (SegmentStart[s] == i ? 0 : Haversine.Distance(Lat[i - 1], Lon[i - 1], p.Latitude, p.Longitude));
                }

                if (TimeSeconds is not null)
                {
                    if (!haveT0)
                    {
                        t0 = p.TimeUtc!.Value;
                        haveT0 = true;
                    }

                    TimeSeconds[i] = (p.TimeUtc!.Value - t0).TotalSeconds;
                }

                i++;
            }
        }

        SegmentStart[document.Segments.Count] = n;
    }

    public GpxDocument Document { get; }

    public double[] Lat { get; }

    public double[] Lon { get; }

    public double[] Cum { get; }

    /// <summary>NaN where the point has no elevation.</summary>
    public double[] Ele { get; }

    /// <summary>Seconds since the first point, or null for untimed tracks.</summary>
    public double[]? TimeSeconds { get; }

    public int[] SegmentStart { get; }

    public int Count => Lat.Length;

    public double TotalDistance => Cum[^1];

    /// <summary>Interpolated lat/lon/time at cumulative distance <paramref name="d"/> (clamped to the track).</summary>
    public (double Lat, double Lon, double? Time) At(double d)
    {
        if (d <= 0)
        {
            return (Lat[0], Lon[0], TimeSeconds?[0]);
        }

        if (d >= TotalDistance)
        {
            return (Lat[^1], Lon[^1], TimeSeconds?[^1]);
        }

        var i = LowerBound(Cum, d);
        if (Cum[i] == d)
        {
            return (Lat[i], Lon[i], TimeSeconds?[i]);
        }

        var f = (d - Cum[i - 1]) / (Cum[i] - Cum[i - 1]);
        return (
            Lat[i - 1] + (Lat[i] - Lat[i - 1]) * f,
            Lon[i - 1] + (Lon[i] - Lon[i - 1]) * f,
            TimeSeconds is null ? null : TimeSeconds[i - 1] + (TimeSeconds[i] - TimeSeconds[i - 1]) * f);
    }

    /// <summary>Elevation interpolated between the neighbouring points that have one; null when no point has elevation.</summary>
    public ElevationSampler? Elevations()
    {
        var cum = new List<double>();
        var ele = new List<double>();
        for (var i = 0; i < Count; i++)
        {
            if (!double.IsNaN(Ele[i]))
            {
                cum.Add(Cum[i]);
                ele.Add(Ele[i]);
            }
        }

        return cum.Count == 0 ? null : new ElevationSampler([.. cum], [.. ele]);
    }

    /// <summary>First index with <c>values[i] &gt;= d</c>.</summary>
    internal static int LowerBound(double[] values, double d)
    {
        int lo = 0, hi = values.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (values[mid] < d)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}

internal sealed class ElevationSampler(double[] cum, double[] ele)
{
    public double Min { get; } = ele.Min();

    public double Max { get; } = ele.Max();

    public double At(double d)
    {
        if (d <= cum[0])
        {
            return ele[0];
        }

        if (d >= cum[^1])
        {
            return ele[^1];
        }

        var i = TrackGeometry.LowerBound(cum, d);
        if (cum[i] == d)
        {
            return ele[i];
        }

        var f = (d - cum[i - 1]) / (cum[i] - cum[i - 1]);
        return ele[i - 1] + (ele[i] - ele[i - 1]) * f;
    }
}
