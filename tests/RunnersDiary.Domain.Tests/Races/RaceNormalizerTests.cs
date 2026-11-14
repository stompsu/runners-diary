using RunnersDiary.Domain.Races;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceNormalizerTests
{
    [Theory]
    [InlineData("10 km", 10_000)]
    [InlineData("10km", 10_000)]
    [InlineData("10,0 km", 10_000)]
    [InlineData("21,1 km", 21_100)]
    [InlineData("21.0975 km", 21_097.5)]
    [InlineData("42,195 km", 42_195)]
    [InlineData("5K", 5_000)]
    [InlineData("5k", 5_000)]
    [InlineData("10 000 m", 10_000)]
    [InlineData("10000 m", 10_000)]
    [InlineData("1 km", 1_000)]
    [InlineData("półmaraton", 21_097.5)]
    [InlineData("Półmaraton", 21_097.5)]
    [InlineData("polmaraton", 21_097.5)]
    [InlineData("Half Marathon", 21_097.5)]
    [InlineData("maraton", 42_195)]
    [InlineData("Marathon", 42_195)]
    [InlineData("Maraton 42 km", 42_000)]
    [InlineData("100 mil", 160_934.4)]
    [InlineData("26.2 mi", 42_164.8)]
    public void Parses_distance_text_to_metres(string text, double expected)
    {
        Assert.True(RaceNormalizer.TryParseDistanceMeters(text, out var meters));
        Assert.Equal(expected, meters, 1);
    }

    [Fact]
    public void Hundred_miles_is_160934_metres()
    {
        Assert.True(RaceNormalizer.TryParseDistanceMeters("100 mil", out var meters));
        Assert.Equal(160_934, meters, 0);
    }

    [Theory]
    [InlineData("1 km dla dzieci")]
    [InlineData("Bieg dzieci 2 km")]
    [InlineData("Kids run 1 km")]
    [InlineData("500 m")]
    [InlineData("999 m")]
    [InlineData("1000 km")]
    [InlineData("251 km")]
    [InlineData("ultramaraton")]
    [InlineData("10")]
    [InlineData("abc")]
    [InlineData("km")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Drops_children_short_out_of_range_and_junk(string? text)
    {
        Assert.False(RaceNormalizer.TryParseDistanceMeters(text, out var meters));
        Assert.Equal(0, meters);
    }

    [Fact]
    public void Boundaries_are_inclusive()
    {
        Assert.True(RaceNormalizer.TryParseDistanceMeters("1000 m", out _));
        Assert.True(RaceNormalizer.TryParseDistanceMeters("250 km", out var max));
        Assert.Equal(250_000, max);
    }

    [Fact]
    public void Parses_lists_with_separators_deduplicated_ascending()
    {
        var result = RaceNormalizer.ParseDistances("5K / 10K", "21,1 km", "junk", "10 km", null, "1 km dla dzieci");

        Assert.Equal([5_000, 10_000, 21_100], result);
    }

    [Fact]
    public void Caps_distance_lists_at_twelve()
    {
        var texts = Enumerable.Range(1, 15).Select(i => $"{i + 1} km").ToArray();

        var result = RaceNormalizer.ParseDistances(texts);

        Assert.Equal(12, result.Count);
        Assert.Equal(2_000, result[0]);
        Assert.Equal(13_000, result[^1]);
    }

    [Theory]
    [InlineData("miejski", RaceKind.Road)]
    [InlineData("Bieg uliczny", RaceKind.Road)]
    [InlineData("city", RaceKind.Road)]
    [InlineData("Street", RaceKind.Road)]
    [InlineData("road", RaceKind.Road)]
    [InlineData("terenowy", RaceKind.Trail)]
    [InlineData("Trail", RaceKind.Trail)]
    [InlineData("bieg górski", RaceKind.Mountain)]
    [InlineData("Mountain", RaceKind.Mountain)]
    [InlineData("ultra", RaceKind.Ultra)]
    [InlineData("Ultra trail", RaceKind.Ultra)]
    [InlineData("przełaj", RaceKind.CrossCountry)]
    [InlineData("bieg przełajowy", RaceKind.CrossCountry)]
    [InlineData("Cross", RaceKind.CrossCountry)]
    [InlineData("OCR", RaceKind.Ocr)]
    [InlineData("bieg z przeszkodami", RaceKind.Ocr)]
    [InlineData("Obstacle course", RaceKind.Ocr)]
    [InlineData("Bieg po asfalcie", RaceKind.Road)]
    [InlineData("Bieg miejski", RaceKind.Road)]
    [InlineData("urban", RaceKind.Road)]
    [InlineData("asfalt", RaceKind.Road)]
    [InlineData("Backyard Ultra", RaceKind.Ultra)]
    [InlineData("nordic walking", RaceKind.Other)]
    [InlineData("", RaceKind.Other)]
    [InlineData(null, RaceKind.Other)]
    public void Maps_kind_text(string? text, RaceKind expected) => Assert.Equal(expected, RaceNormalizer.ParseKind(text));

    [Fact]
    public void Region_text_resolves_to_iso_code()
    {
        Assert.Equal("PL-24", RaceNormalizer.ParseRegionCode("Silesian Voivodeship"));
        Assert.Null(RaceNormalizer.ParseRegionCode("Bavaria"));
    }
}
