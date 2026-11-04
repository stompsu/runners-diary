using System.Text;
using RunnersDiary.Domain.Gpx;
using static RunnersDiary.Domain.Tests.Gpx.GpxTestSupport;

namespace RunnersDiary.Domain.Tests.Gpx;

public sealed class GpxReaderTests
{
    [Theory]
    [InlineData("xxe-file.gpx", GpxRejectionCode.DtdProhibited)]
    [InlineData("xxe-http.gpx", GpxRejectionCode.DtdProhibited)]
    [InlineData("billion-laughs.gpx", GpxRejectionCode.DtdProhibited)]
    [InlineData("dtd-only.gpx", GpxRejectionCode.DtdProhibited)]
    [InlineData("html-as.gpx", GpxRejectionCode.DtdProhibited)]
    [InlineData("not-gpx-root.xml", GpxRejectionCode.NotGpx)]
    [InlineData("wrong-namespace.gpx", GpxRejectionCode.NotGpx)]
    [InlineData("malformed.gpx", GpxRejectionCode.MalformedXml)]
    [InlineData("empty.gpx", GpxRejectionCode.UnsupportedFormat)]
    [InlineData("deep-nesting.gpx", GpxRejectionCode.TooComplex)]
    [InlineData("invalid-lat.gpx", GpxRejectionCode.InvalidCoordinate)]
    [InlineData("nan-lon.gpx", GpxRejectionCode.InvalidCoordinate)]
    [InlineData("one-point.gpx", GpxRejectionCode.TooFewPoints)]
    [InlineData("no-points.gpx", GpxRejectionCode.TooFewPoints)]
    [InlineData("gzip.gpx", GpxRejectionCode.UnsupportedFormat)]
    [InlineData("zip-as.gpx", GpxRejectionCode.UnsupportedFormat)]
    [InlineData("binary.gpx", GpxRejectionCode.UnsupportedFormat)]
    public void Invalid_fixtures_are_rejected_with_the_expected_code(string fixture, GpxRejectionCode expected)
    {
        var result = Read(Fixture(fixture));

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Rejection!.Code);
    }

    [Theory]
    [InlineData("xxe-file.gpx")]
    [InlineData("xxe-http.gpx")]
    [InlineData("billion-laughs.gpx")]
    public void Rejections_never_echo_file_contents_or_parser_text(string fixture)
    {
        var message = Read(Fixture(fixture)).Rejection!.Message;

        Assert.DoesNotContain("passwd", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("root:", message, StringComparison.Ordinal);
        Assert.DoesNotContain("lol", message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("line", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GpxRejection.For(Read(Fixture(fixture)).Rejection!.Code).Message, message);
    }

    [Fact]
    public void Every_rejection_code_has_a_fixed_message_and_camel_case_name()
    {
        foreach (var code in Enum.GetValues<GpxRejectionCode>())
        {
            var rejection = GpxRejection.For(code);
            Assert.False(string.IsNullOrWhiteSpace(rejection.Message));
            Assert.True(char.IsLower(rejection.CodeName[0]));
        }

        Assert.Equal("notGpx", GpxRejection.For(GpxRejectionCode.NotGpx).CodeName);
    }

    [Fact]
    public void Script_content_in_names_is_ignored_and_never_surfaces()
    {
        var doc = ReadFixture("script-in-name.gpx");

        Assert.Equal(6, doc.PointCount);
    }

    [Fact]
    public void Road_fixture_is_timed_with_elevation_and_no_warnings()
    {
        var doc = ReadFixture("road-5k.gpx");

        Assert.Equal(6, doc.PointCount);
        Assert.True(doc.IsTimed);
        Assert.True(doc.HasElevation);
        Assert.Equal(TrackWarnings.None, doc.Warnings);
        Assert.Equal(new DateTime(2025, 6, 1, 7, 0, 0, DateTimeKind.Utc), doc.Segments[0].Points[0].TimeUtc);
    }

    [Fact]
    public void Route_only_files_use_rtept_and_set_the_route_flag()
    {
        var doc = ReadFixture("route-only.gpx");

        Assert.Equal(4, doc.PointCount);
        Assert.False(doc.IsTimed);
        Assert.Equal(TrackWarnings.RouteConverted, doc.Warnings);
    }

    [Fact]
    public void Track_points_win_over_a_route_in_the_same_file()
    {
        var xml = """
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <rte><rtept lat="1" lon="1"/><rtept lat="1.1" lon="1"/><rtept lat="1.2" lon="1"/></rte>
              <trk><trkseg><trkpt lat="50" lon="18"/><trkpt lat="50.001" lon="18"/></trkseg></trk>
            </gpx>
            """;

        var doc = Ok(Read(xml));

        Assert.Equal(2, doc.PointCount);
        Assert.Equal(TrackWarnings.NoElevation, doc.Warnings);
    }

    [Fact]
    public void Multiple_segments_and_multiple_tracks_are_merged_with_a_warning()
    {
        foreach (var fixture in new[] { "multi-segment.gpx", "two-tracks.gpx" })
        {
            var doc = ReadFixture(fixture);

            Assert.Equal(2, doc.Segments.Count);
            Assert.Equal(6, doc.PointCount);
            Assert.True(doc.Warnings.HasFlag(TrackWarnings.MultipleSegments));
            Assert.True(doc.IsTimed);
        }
    }

    [Fact]
    public void No_elevation_is_flagged_and_keeps_times()
    {
        var doc = ReadFixture("no-elevation.gpx");

        Assert.False(doc.HasElevation);
        Assert.True(doc.IsTimed);
        Assert.Equal(TrackWarnings.NoElevation, doc.Warnings);
    }

    [Fact]
    public void No_timestamps_means_untimed_without_a_timestamp_warning()
    {
        var doc = ReadFixture("no-time.gpx");

        Assert.False(doc.IsTimed);
        Assert.Equal(TrackWarnings.None, doc.Warnings);
    }

    [Theory]
    [InlineData("partial-time.gpx")]
    [InlineData("time-backwards.gpx")]
    public void Partial_or_backwards_timestamps_make_the_track_untimed_with_a_warning(string fixture)
    {
        var doc = ReadFixture(fixture);

        Assert.False(doc.IsTimed);
        Assert.True(doc.Warnings.HasFlag(TrackWarnings.TimestampsIgnored));
        Assert.All(doc.Segments.SelectMany(s => s.Points), p => Assert.Null(p.TimeUtc));
    }

    [Theory]
    [InlineData("1989-12-31T23:59:50Z", "1989-12-31T23:59:59Z")]
    [InlineData("2026-11-05T00:00:00Z", "2026-11-05T00:00:10Z")]
    public void Timestamps_outside_1990_to_tomorrow_are_ignored(string first, string second)
    {
        var xml = BuildGpx([(50, 18, null, first), (50.001, 18, null, second)]);

        var doc = Ok(Read(xml));

        Assert.False(doc.IsTimed);
        Assert.True(doc.Warnings.HasFlag(TrackWarnings.TimestampsIgnored));
    }

    [Fact]
    public void A_timestamp_within_a_day_of_now_is_accepted()
    {
        var xml = BuildGpx([(50, 18, null, "2026-11-04T11:00:00Z"), (50.001, 18, null, "2026-11-04T11:00:10Z")]);

        Assert.True(Ok(Read(xml)).IsTimed);
    }

    [Fact]
    public void Offsets_are_normalised_to_utc()
    {
        var xml = BuildGpx([(50, 18, null, "2025-06-01T09:00:00+02:00"), (50.001, 18, null, "2025-06-01T07:00:10Z")]);

        var doc = Ok(Read(xml));

        Assert.True(doc.IsTimed);
        Assert.Equal(new DateTime(2025, 6, 1, 7, 0, 0, DateTimeKind.Utc), doc.Segments[0].Points[0].TimeUtc);
    }

    [Fact]
    public void Elevations_outside_the_sane_range_are_dropped_not_rejected()
    {
        var xml = BuildGpx([(50, 18, 100, null), (50.001, 18, 12000, null), (50.002, 18, -900, null)]);

        var doc = Ok(Read(xml));

        Assert.Equal([100.0, null, null], doc.Segments[0].Points.Select(p => p.Elevation).ToArray());
    }

    [Theory]
    [InlineData("91", "18")]
    [InlineData("-90.0001", "18")]
    [InlineData("50", "180.1")]
    [InlineData("50", "-181")]
    [InlineData("abc", "18")]
    [InlineData("50", "Infinity")]
    [InlineData("1e999", "18")]
    [InlineData("0x10", "18")]
    [InlineData("", "18")]
    public void Invalid_coordinates_are_rejected(string lat, string lon)
    {
        var xml = $"""<gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg><trkpt lat="50" lon="18"/><trkpt lat="{lat}" lon="{lon}"/></trkseg></trk></gpx>""";

        Assert.Equal(GpxRejectionCode.InvalidCoordinate, Read(xml).Rejection!.Code);
    }

    [Fact]
    public void A_point_without_lat_or_lon_is_rejected()
    {
        var xml = """<gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg><trkpt lat="50"/><trkpt lat="50" lon="18"/></trkseg></trk></gpx>""";

        Assert.Equal(GpxRejectionCode.InvalidCoordinate, Read(xml).Rejection!.Code);
    }

    [Fact]
    public void Boundary_coordinates_are_accepted()
    {
        var xml = BuildGpx([(90, 180, null, null), (-90, -180, null, null)]);

        Assert.Equal(2, Ok(Read(xml)).PointCount);
    }

    [Fact]
    public void Comma_decimals_are_not_parsed_under_any_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pl-PL");
        try
        {
            var xml = BuildGpx([(50.5, 18.5, 123.4, null), (50.6, 18.5, null, null)]);

            Assert.Equal(123.4, Ok(Read(xml)).Segments[0].Points[0].Elevation);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("gpx10.gpx")]
    [InlineData("utf16.gpx")]
    [InlineData("bom.gpx")]
    [InlineData("extensions-heavy.gpx")]
    public void Encodings_namespaces_and_extensions_are_handled(string fixture)
    {
        var doc = ReadFixture(fixture);

        Assert.Equal(3, doc.PointCount);
        Assert.True(doc.IsTimed);
    }

    [Fact]
    public void Leading_whitespace_before_the_root_is_tolerated()
    {
        var xml = "  \n\t" + BuildGpx([(50, 18, null, null), (50.001, 18, null, null)]).Replace("<?xml version=\"1.0\"?>", string.Empty, StringComparison.Ordinal);

        Assert.Equal(2, Ok(Read(xml)).PointCount);
    }

    [Fact]
    public void Plain_text_and_json_are_unsupported()
    {
        Assert.Equal(GpxRejectionCode.UnsupportedFormat, Read("{\"a\":1}").Rejection!.Code);
        Assert.Equal(GpxRejectionCode.UnsupportedFormat, Read("hello").Rejection!.Code);
    }

    [Fact]
    public void Unsupported_encoding_declarations_are_malformed_not_a_crash()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"x-bogus\"?><gpx xmlns=\"http://www.topografix.com/GPX/1/1\"/>";

        Assert.Equal(GpxRejectionCode.MalformedXml, Read(xml).Rejection!.Code);
    }

    [Fact]
    public void A_gpx_root_without_tracks_is_too_few_points()
    {
        var xml = "<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\"><wpt lat=\"1\" lon=\"1\"/><wpt lat=\"2\" lon=\"2\"/></gpx>";

        Assert.Equal(GpxRejectionCode.TooFewPoints, Read(xml).Rejection!.Code);
    }

    [Fact]
    public void Points_outside_a_trkseg_and_foreign_namespace_points_are_ignored()
    {
        var xml = """
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1" xmlns:x="urn:x">
              <trk><trkpt lat="1" lon="1"/><trkseg><x:trkpt lat="2" lon="2"/><trkpt lat="50" lon="18"/><trkpt lat="50.1" lon="18"/></trkseg></trk>
            </gpx>
            """;

        Assert.Equal(2, Ok(Read(xml)).PointCount);
    }

    [Fact]
    public void Overlong_values_are_treated_as_missing()
    {
        var xml = $"""<gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg><trkpt lat="50" lon="18"><ele>{new string('1', 200)}</ele></trkpt><trkpt lat="50.1" lon="18"/></trkseg></trk></gpx>""";

        var doc = Ok(Read(xml));

        Assert.Null(doc.Segments[0].Points[0].Elevation);
    }

    [Theory]
    [InlineData("track.gpx", "track.gpx")]
    [InlineData("C:\\Users\\me\\Run 1.GPX", "Run 1.GPX")]
    [InlineData("../../etc/evil.gpx", "evil.gpx")]
    [InlineData("a\"b\u0001.gpx", "ab.gpx")]
    public void File_names_are_reduced_to_a_safe_display_name(string raw, string expected)
    {
        Assert.True(GpxFileName.TryNormalize(raw, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("track.txt")]
    [InlineData("track.gpx.exe")]
    [InlineData(".gpx")]
    [InlineData("dir/")]
    public void File_names_without_a_gpx_extension_are_refused(string? raw)
    {
        Assert.False(GpxFileName.TryNormalize(raw, out _));
    }

    [Fact]
    public void Long_file_names_are_cut_to_255_keeping_the_extension()
    {
        Assert.True(GpxFileName.TryNormalize(new string('a', 400) + ".gpx", out var name));
        Assert.Equal(255, name.Length);
        Assert.EndsWith(".gpx", name, StringComparison.Ordinal);
    }
}

public sealed class GpxReaderLimitTests
{
    private const string Header = "<?xml version=\"1.0\"?><gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\"><trk><trkseg>";

    private static MemoryStream Points(int count)
    {
        var ms = new MemoryStream();
        using (var w = new StreamWriter(ms, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
        {
            w.Write(Header);
            for (var i = 0; i < count; i++)
            {
                w.Write(FormattableString.Invariant($"<trkpt lat=\"{50 + (i % 1000) * 0.00001:F6}\" lon=\"18.000000\"><ele>100.0</ele></trkpt>"));
            }

            w.Write("</trkseg></trk></gpx>");
        }

        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Exactly_the_maximum_number_of_points_is_accepted()
    {
        var result = GpxReader.Read(Points(250_000), GpxReadLimits.Default, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(250_000, result.Document!.PointCount);
    }

    [Fact]
    public void One_point_over_the_maximum_is_too_many_points()
    {
        var result = GpxReader.Read(Points(250_001), GpxReadLimits.Default, Now);

        Assert.Equal(GpxRejectionCode.TooManyPoints, result.Rejection!.Code);
    }

    [Fact]
    public void A_file_over_the_byte_limit_is_too_large_even_when_streamed()
    {
        var padding = new string('a', 25 * 1024 * 1024);
        var xml = Header + "<trkpt lat=\"50\" lon=\"18\"/><trkpt lat=\"50.1\" lon=\"18\"/></trkseg></trk><metadata><name>" + padding + "</name></metadata></gpx>";

        var result = GpxReader.Read(new NonSeekableStream(Encoding.UTF8.GetBytes(xml)), GpxReadLimits.Default, Now);

        Assert.Equal(GpxRejectionCode.TooLarge, result.Rejection!.Code);
    }

    [Fact]
    public void The_guest_limit_is_stricter_than_the_user_limit()
    {
        var padding = new string('a', 6 * 1024 * 1024);
        var xml = Header + "<trkpt lat=\"50\" lon=\"18\"/><trkpt lat=\"50.1\" lon=\"18\"/></trkseg></trk><metadata><name>" + padding + "</name></metadata></gpx>";
        var bytes = Encoding.UTF8.GetBytes(xml);

        Assert.Equal(GpxRejectionCode.TooLarge, GpxReader.Read(new MemoryStream(bytes), GpxReadLimits.ForGuest, Now).Rejection!.Code);
        Assert.True(GpxReader.Read(new MemoryStream(bytes), GpxReadLimits.Default, Now).IsSuccess);
    }

    [Fact]
    public void Excess_elements_are_rejected()
    {
        var limits = new GpxReadLimits { MaxElements = 20 };

        var result = GpxReader.Read(Points(50), limits, Now);

        Assert.Equal(GpxRejectionCode.TooComplex, result.Rejection!.Code);
    }

    [Fact]
    public void Excess_segments_are_rejected()
    {
        var xml = "<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\"><trk>" + string.Concat(Enumerable.Repeat("<trkseg><trkpt lat=\"1\" lon=\"1\"/><trkpt lat=\"1\" lon=\"2\"/></trkseg>", 1_001)) + "</trk></gpx>";

        Assert.Equal(GpxRejectionCode.TooComplex, Read(xml).Rejection!.Code);
    }

    [Fact]
    public void Depth_of_exactly_the_limit_is_accepted_and_one_more_is_rejected()
    {
        // gpx(0) > trk(1) > trkseg(2) > trkpt(3) > extensions(4) > a(5..): depth 16 is the last allowed.
        string Nested(int levels) => "<extensions>" + string.Concat(Enumerable.Repeat("<a>", levels)) + string.Concat(Enumerable.Repeat("</a>", levels)) + "</extensions>";
        string Doc(int levels) => $"<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\"><trk><trkseg><trkpt lat=\"1\" lon=\"1\">{Nested(levels)}</trkpt><trkpt lat=\"1\" lon=\"2\"/></trkseg></trk></gpx>";

        Assert.True(Read(Doc(12)).IsSuccess);
        Assert.Equal(GpxRejectionCode.TooComplex, Read(Doc(13)).Rejection!.Code);
    }

    [Fact]
    public void Entity_expansion_cannot_happen_because_a_doctype_is_never_parsed()
    {
        // Even a tiny bomb is refused outright rather than expanded.
        var result = Read(Fixture("billion-laughs.gpx"));

        Assert.Equal(GpxRejectionCode.DtdProhibited, result.Rejection!.Code);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, 4096), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
