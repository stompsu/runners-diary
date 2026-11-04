using System.Globalization;
using System.Text;
using System.Xml;

namespace RunnersDiary.Domain.Gpx;

/// <summary>
/// Forward-only, hardened GPX reader (ADR 0020). Untrusted input: DTDs are prohibited, no resolver, bounded size/depth/elements/points.
/// Never builds a DOM and never echoes parser text.
/// </summary>
public static class GpxReader
{
    private const string Gpx11 = "http://www.topografix.com/GPX/1/1";
    private const string Gpx10 = "http://www.topografix.com/GPX/1/0";
    private const int SniffBytes = 64;
    private const int MaxValueLength = 64;
    private const double MinElevation = -500;
    private const double MaxElevation = 9_000;
    private static readonly DateTime MinTime = new(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private enum Kind : byte { Other, Root, Trk, Seg, Rte, Pt, Ele, Time }

    /// <summary>Reads and validates <paramref name="input"/>. <paramref name="nowUtc"/> bounds acceptable timestamps (now + 1 day).</summary>
    public static GpxReadResult Read(Stream input, GpxReadLimits limits, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(limits);

        var prefix = new byte[SniffBytes];
        var prefixLength = ReadFully(input, prefix);
        if (!LooksLikeXml(prefix.AsSpan(0, prefixLength)))
        {
            return GpxReadResult.Reject(GpxRejectionCode.UnsupportedFormat);
        }

        using var bounded = new BoundedStream(prefix, prefixLength, input, limits.MaxBytes);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024, // DTDs are prohibited; 0 would mean unlimited should that ever change
            MaxCharactersInDocument = limits.MaxBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        };

        try
        {
            using var reader = XmlReader.Create(bounded, settings);
            return Parse(reader, limits, nowUtc);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException or IOException or DecoderFallbackException or NotSupportedException)
        {
            if (bounded.Exceeded)
            {
                return GpxReadResult.Reject(GpxRejectionCode.TooLarge);
            }

            return GpxReadResult.Reject(bounded.HeadContainsDoctype() ? GpxRejectionCode.DtdProhibited : GpxRejectionCode.MalformedXml);
        }
    }

    private static GpxReadResult Parse(XmlReader reader, GpxReadLimits limits, DateTimeOffset nowUtc)
    {
        var kinds = new Kind[limits.MaxDepth + 1];
        var trkSegments = new List<List<GpxPoint>>();
        var rteSegments = new List<List<GpxPoint>>();
        List<GpxPoint>? current = null;
        string? rootNs = null;
        var elements = 0;
        var points = 0;

        // Point under construction.
        var inPoint = false;
        double lat = 0, lon = 0;
        double? ele = null;
        DateTime? time = null;
        var textTarget = Kind.Other;
        var textDepth = -1;
        var text = new StringBuilder();
        var textOverflow = false;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    var depth = reader.Depth;
                    if (depth > limits.MaxDepth || ++elements > limits.MaxElements)
                    {
                        return GpxReadResult.Reject(GpxRejectionCode.TooComplex);
                    }

                    var name = reader.LocalName;
                    var ns = reader.NamespaceURI;
                    if (depth == 0)
                    {
                        if (name != "gpx" || (ns != Gpx11 && ns != Gpx10))
                        {
                            return GpxReadResult.Reject(GpxRejectionCode.NotGpx);
                        }

                        rootNs = ns;
                        kinds[0] = Kind.Root;
                        break;
                    }

                    var parent = kinds[depth - 1];
                    var kind = ns != rootNs ? Kind.Other : (parent, name) switch
                    {
                        (Kind.Root, "trk") => Kind.Trk,
                        (Kind.Root, "rte") => Kind.Rte,
                        (Kind.Trk, "trkseg") => Kind.Seg,
                        (Kind.Seg, "trkpt") => Kind.Pt,
                        (Kind.Rte, "rtept") => Kind.Pt,
                        (Kind.Pt, "ele") => Kind.Ele,
                        (Kind.Pt, "time") => Kind.Time,
                        _ => Kind.Other,
                    };
                    kinds[depth] = kind;
                    var empty = reader.IsEmptyElement;

                    switch (kind)
                    {
                        case Kind.Seg when !empty:
                            current = [];
                            trkSegments.Add(current);
                            if (trkSegments.Count > limits.MaxSegments)
                            {
                                return GpxReadResult.Reject(GpxRejectionCode.TooComplex);
                            }

                            break;
                        case Kind.Rte when !empty:
                            current = [];
                            rteSegments.Add(current);
                            if (rteSegments.Count > limits.MaxSegments)
                            {
                                return GpxReadResult.Reject(GpxRejectionCode.TooComplex);
                            }

                            break;
                        case Kind.Pt:
                            if (++points > limits.MaxPoints)
                            {
                                return GpxReadResult.Reject(GpxRejectionCode.TooManyPoints);
                            }

                            if (!TryParseCoordinate(reader.GetAttribute("lat"), -90, 90, out lat)
                                || !TryParseCoordinate(reader.GetAttribute("lon"), -180, 180, out lon))
                            {
                                return GpxReadResult.Reject(GpxRejectionCode.InvalidCoordinate);
                            }

                            ele = null;
                            time = null;
                            if (empty)
                            {
                                current?.Add(new GpxPoint(lat, lon, null, null));
                            }
                            else
                            {
                                inPoint = true;
                            }

                            break;
                        case Kind.Ele or Kind.Time when !empty && inPoint:
                            textTarget = kind;
                            textDepth = depth;
                            text.Clear();
                            textOverflow = false;
                            break;
                    }

                    break;
                }

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (textTarget != Kind.Other && reader.Depth == textDepth + 1)
                    {
                        var value = reader.Value;
                        if (text.Length + value.Length > MaxValueLength)
                        {
                            textOverflow = true;
                        }
                        else
                        {
                            text.Append(value);
                        }
                    }

                    break;

                case XmlNodeType.EndElement:
                {
                    var depth = reader.Depth;
                    switch (kinds[depth])
                    {
                        case Kind.Ele when textTarget == Kind.Ele && depth == textDepth:
                            ele = !textOverflow && double.TryParse(text.ToString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var e)
                                  && double.IsFinite(e) && e is >= MinElevation and <= MaxElevation ? e : null;
                            textTarget = Kind.Other;
                            break;
                        case Kind.Time when textTarget == Kind.Time && depth == textDepth:
                            time = !textOverflow && DateTimeOffset.TryParse(text.ToString().Trim(), CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                                ? t.UtcDateTime : null;
                            textTarget = Kind.Other;
                            break;
                        case Kind.Pt when inPoint:
                            current?.Add(new GpxPoint(lat, lon, ele, time));
                            inPoint = false;
                            break;
                    }

                    // Stale kind entries must not leak into later siblings at this depth.
                    kinds[depth] = Kind.Other;
                    break;
                }
            }
        }

        if (rootNs is null)
        {
            return GpxReadResult.Reject(GpxRejectionCode.NotGpx);
        }

        var trkCount = trkSegments.Sum(s => s.Count);
        var fromRoute = trkCount == 0;
        var chosen = (fromRoute ? rteSegments : trkSegments).Where(s => s.Count > 0).ToList();
        var total = chosen.Sum(s => s.Count);
        if (total < 2)
        {
            return GpxReadResult.Reject(GpxRejectionCode.TooFewPoints);
        }

        return GpxReadResult.Ok(BuildDocument(chosen, fromRoute, nowUtc));
    }

    private static GpxDocument BuildDocument(List<List<GpxPoint>> segments, bool fromRoute, DateTimeOffset nowUtc)
    {
        var warnings = TrackWarnings.None;
        if (fromRoute)
        {
            warnings |= TrackWarnings.RouteConverted;
        }

        if (segments.Count > 1)
        {
            warnings |= TrackWarnings.MultipleSegments;
        }

        var all = segments.SelectMany(s => s).ToList();
        if (!all.Any(p => p.Elevation is not null))
        {
            warnings |= TrackWarnings.NoElevation;
        }

        var timed = all.Count(p => p.TimeUtc is not null);
        var isTimed = false;
        if (timed > 0)
        {
            var upper = nowUtc.UtcDateTime.AddDays(1);
            isTimed = timed == all.Count;
            DateTime? previous = null;
            foreach (var p in all)
            {
                var t = p.TimeUtc;
                if (!isTimed)
                {
                    break;
                }

                if (t < MinTime || t > upper || (previous is not null && t < previous))
                {
                    isTimed = false;
                }

                previous = t;
            }

            if (!isTimed)
            {
                warnings |= TrackWarnings.TimestampsIgnored;
            }
        }

        var result = segments
            .Select(s => new GpxSegment(isTimed ? [.. s] : [.. s.Select(p => p with { TimeUtc = null })]))
            .ToList();
        return new GpxDocument(result, isTimed, warnings);
    }

    private static bool TryParseCoordinate(string? raw, double min, double max, out double value)
    {
        value = 0;
        return raw is { Length: > 0 and <= 32 }
               && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value) && value >= min && value <= max;
    }

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read;
    }

    /// <summary>Gzip/zip magic numbers and anything whose first non-blank character is not '&lt;' are not XML (UTF-16 BOMs pass).</summary>
    private static bool LooksLikeXml(ReadOnlySpan<byte> head)
    {
        if (head.Length == 0)
        {
            return false;
        }

        if (head.Length >= 2 && ((head[0] == 0x1F && head[1] == 0x8B) || (head[0] == 0x50 && head[1] == 0x4B)))
        {
            return false;
        }

        if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
        {
            return true;
        }

        var i = head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
        while (i < head.Length && head[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            i++;
        }

        return i < head.Length && head[i] == (byte)'<';
    }

    /// <summary>Replays the sniffed prefix, then reads on, failing once more than <c>maxBytes</c> were consumed.</summary>
    private sealed class BoundedStream(byte[] prefix, int prefixLength, Stream inner, long maxBytes) : Stream
    {
        private readonly byte[] _head = new byte[8192];
        private int _headLength;
        private int _prefixPosition;
        private long _total;

        public bool Exceeded { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n;
            if (_prefixPosition < prefixLength)
            {
                n = Math.Min(count, prefixLength - _prefixPosition);
                Array.Copy(prefix, _prefixPosition, buffer, offset, n);
                _prefixPosition += n;
            }
            else
            {
                n = inner.Read(buffer, offset, count);
            }

            _total += n;
            if (_total > maxBytes)
            {
                Exceeded = true;
                throw new IOException("Size limit exceeded.");
            }

            if (_headLength < _head.Length && n > 0)
            {
                var copy = Math.Min(n, _head.Length - _headLength);
                Array.Copy(buffer, offset, _head, _headLength, copy);
                _headLength += copy;
            }

            return n;
        }

        public bool HeadContainsDoctype()
        {
            var bytes = _head.AsSpan(0, _headLength);
            var utf16 = bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
            var text = utf16
                ? (bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes)
                : Encoding.Latin1.GetString(bytes);
            return text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
