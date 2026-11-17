using RunnersDiary.Domain.Localization;

namespace RunnersDiary.Domain.Tests.Localization;

public sealed class AcceptLanguageTests
{
    [Theory]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("   ", "en")]
    [InlineData("pl", "pl")]
    [InlineData("PL-pl", "pl")]
    [InlineData("en-GB", "en")]
    [InlineData("pl-PL,pl;q=0.9,en;q=0.8", "pl")]
    [InlineData("en;q=0.4, pl;q=0.8", "pl")]
    [InlineData("pl;q=0.4, en;q=0.8", "en")]
    [InlineData("pl;q=0, en;q=0.1", "en")]
    [InlineData("pl;q=0", "en")]
    [InlineData("de, fr;q=0.9", "en")]
    [InlineData("*", "en")]
    [InlineData("pl;q=abc", "pl")]
    [InlineData(";;;,,,", "en")]
    [InlineData("pl-PL\r\nX: y", "pl")]
    public void The_header_is_reduced_to_a_supported_language(string? header, string expected)
    {
        Assert.Equal(expected, AcceptLanguage.Reduce(header));
    }

    [Fact]
    public void An_oversized_header_is_ignored()
    {
        Assert.Equal("en", AcceptLanguage.Reduce("pl," + new string('x', 300)));
    }

    [Fact]
    public void Only_the_first_entries_are_considered()
    {
        var header = string.Join(',', Enumerable.Repeat("de", 16)) + ",pl";
        Assert.Equal("en", AcceptLanguage.Reduce(header));
    }
}
