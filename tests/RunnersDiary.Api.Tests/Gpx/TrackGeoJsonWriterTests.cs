using System.Text;
using System.Text.Json;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Api.Tests.Gpx;

/// <summary>ADR 0021 §5: the processed-track document is an RFC 7946 FeatureCollection with fixed foreign members.</summary>
public sealed class TrackGeoJsonWriterTests
{
    private static ProcessedTrack SmallTrack(bool withElevation) => new(
        TrackProcessor.ProcessorVersion,
        new TrackSummary(2, 1001.5, withElevation ? 1.5 : null, withElevation ? 0 : null, null, null, 300, 290, null, null, 50, 18, 50.009, 18, TrackWarnings.None),
        [[new LonLat(18, 50), new LonLat(18, 50.009)]],
        new LonLat(18, 50),
        new LonLat(18, 50.009),
        [18, 50, 18, 50.009],
        withElevation ? [new ProfileSample(0, 100, 18, 50), new ProfileSample(1001, 101.5, 18, 50.009)] : null,
        [new TrackSplit(1, 1000, 299, withElevation ? 1.5 : null), new TrackSplit(2, 1.5, null, null)]);

    [Fact]
    public void Writes_the_documented_shape_with_fixed_member_order()
    {
        var json = Encoding.UTF8.GetString(TrackGeoJsonWriter.Write(SmallTrack(withElevation: true)));

        string[] parts =
        [
            "{\"type\":\"FeatureCollection\",\"formatVersion\":1,\"bbox\":[18,50,18,50.009],\"features\":[",
            "{\"type\":\"Feature\",\"id\":\"line\",\"properties\":{\"role\":\"line\"},\"geometry\":{\"type\":\"MultiLineString\",\"coordinates\":[[[18,50],[18,50.009]]]}},",
            "{\"type\":\"Feature\",\"id\":\"start\",\"properties\":{\"role\":\"start\"},\"geometry\":{\"type\":\"Point\",\"coordinates\":[18,50]}},",
            "{\"type\":\"Feature\",\"id\":\"finish\",\"properties\":{\"role\":\"finish\"},\"geometry\":{\"type\":\"Point\",\"coordinates\":[18,50.009]}}],",
            "\"profile\":[[0,100,18,50],[1001,101.5,18,50.009]],",
            "\"splits\":[{\"index\":1,\"distanceMeters\":1000,\"elapsedSeconds\":299,\"elevationDeltaMeters\":1.5},",
            "{\"index\":2,\"distanceMeters\":1.5,\"elapsedSeconds\":null,\"elevationDeltaMeters\":null}]}",
        ];
        var expected = string.Concat(parts);
        Assert.Equal(expected, json);
    }

    [Fact]
    public void A_track_without_elevation_has_a_null_profile()
    {
        using var doc = JsonDocument.Parse(TrackGeoJsonWriter.Write(SmallTrack(withElevation: false)));

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("profile").ValueKind);
        Assert.Equal(2, doc.RootElement.GetProperty("splits").GetArrayLength());
    }

    [Fact]
    public void Every_demo_track_serializes_to_a_valid_compact_document_within_the_size_budget()
    {
        var dataset = DemoDatasetLoader.Load(Path.Combine(AppContext.BaseDirectory, "seed", "demo"), DateTimeOffset.UtcNow);

        Assert.Equal(6, dataset.Files.Count);
        foreach (var (name, file) in dataset.Files)
        {
            Assert.InRange(file.GeoJson.Length, 1_000, 300_000);
            using var doc = JsonDocument.Parse(file.GeoJson);
            var root = doc.RootElement;
            Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());
            Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
            Assert.Equal(4, root.GetProperty("bbox").GetArrayLength());
            Assert.Equal(["line", "start", "finish"], root.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetString()));
            Assert.Equal(file.Processed.Splits.Count, root.GetProperty("splits").GetArrayLength());
            Assert.InRange(root.GetProperty("profile").GetArrayLength(), 2, ElevationProfile.MaxSamples);
            Assert.EndsWith(".gpx", name, StringComparison.Ordinal);
        }
    }
}
