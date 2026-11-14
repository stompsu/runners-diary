using System.Globalization;
using System.Text;
using RunnersDiary.Domain.Races;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class PolishRegionsTests
{
    private static string StripDiacritics(string s) =>
        new(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    [Fact]
    public void Has_the_sixteen_iso_codes_PL_02_to_PL_32()
    {
        var expected = Enumerable.Range(1, 16).Select(i => $"PL-{i * 2:00}").ToArray();

        Assert.Equal(expected, PolishRegions.All.Select(r => r.Code).ToArray());
    }

    [Fact]
    public void Known_names_of_a_few_codes()
    {
        Assert.Equal("śląskie", PolishRegions.Find("PL-24")!.PolishName);
        Assert.Equal("Silesian", PolishRegions.Find("PL-24")!.EnglishName);
        Assert.Equal("łódzkie", PolishRegions.Find("PL-10")!.PolishName);
        Assert.Equal("Lesser Poland", PolishRegions.Find("pl-12")!.EnglishName);
        Assert.Null(PolishRegions.Find("PL-99"));
        Assert.False(PolishRegions.IsKnown(null));
    }

    public static TheoryData<string> Codes()
    {
        var data = new TheoryData<string>();
        foreach (var r in PolishRegions.All)
        {
            data.Add(r.Code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_region_resolves_by_code_english_and_polish_name_in_any_case_and_without_diacritics(string code)
    {
        var region = PolishRegions.Find(code)!;
        string[] inputs =
        [
            region.Code,
            region.Code.ToLowerInvariant(),
            region.EnglishName,
            region.EnglishName.ToUpperInvariant(),
            region.EnglishName.ToLowerInvariant(),
            StripDiacritics(region.EnglishName),
            region.PolishName,
            region.PolishName.ToUpperInvariant(),
            StripDiacritics(region.PolishName),
            $"{region.EnglishName} Voivodeship",
            $"  {region.EnglishName.ToUpperInvariant()}  VOIVODESHIP ",
            $"województwo {region.PolishName}",
            $"woj. {StripDiacritics(region.PolishName)}",
        ];

        foreach (var input in inputs)
        {
            Assert.True(PolishRegions.TryGetCode(input, out var resolved), $"'{input}' should resolve");
            Assert.Equal(code, resolved);
        }
    }

    [Theory]
    [InlineData("Silesia", "PL-24")]
    [InlineData("SILESIAN VOIVODESHIP", "PL-24")]
    [InlineData("pl24", "PL-24")]
    [InlineData("PL 24", "PL-24")]
    [InlineData("Lodz", "PL-10")]
    [InlineData("Łódź", "PL-10")]
    [InlineData("Lower Silesia", "PL-02")]
    [InlineData("Warmia-Masuria", "PL-28")]
    [InlineData("Holy Cross", "PL-26")]
    [InlineData("West Pomerania", "PL-32")]
    [InlineData("Małopolska", "PL-12")]
    public void Resolves_aliases(string text, string code)
    {
        Assert.True(PolishRegions.TryGetCode(text, out var resolved));
        Assert.Equal(code, resolved);
    }

    [Theory]
    [InlineData("Bavaria")]
    [InlineData("PL-99")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_text_does_not_resolve(string? text) => Assert.False(PolishRegions.TryGetCode(text, out _));

    [Fact]
    public void Country_zone_is_warsaw_for_poland_and_null_otherwise()
    {
        Assert.Equal("Europe/Warsaw", CountryTimeZones.Find("PL"));
        Assert.Equal("Europe/Warsaw", CountryTimeZones.Find("pl"));
        Assert.Null(CountryTimeZones.Find("DE"));
        Assert.Null(CountryTimeZones.Find(""));
        Assert.Null(CountryTimeZones.Find(null));
    }
}
