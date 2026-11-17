using Microsoft.Extensions.Options;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>M10 (ADR 0042 §7): the demo translation map loader; offline, no containers.</summary>
public sealed class DemoTextLocalizerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rd-i18n").FullName;

    private DemoTextLocalizer Create() => new(Options.Create(new GuestOptions { DemoDataPath = _directory }));

    private void WriteMap(string language, string json)
    {
        Directory.CreateDirectory(Path.Combine(_directory, "i18n"));
        File.WriteAllText(Path.Combine(_directory, "i18n", language + ".json"), json);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void English_and_empty_text_are_returned_unchanged_without_a_file()
    {
        var localizer = Create();
        var missing = new List<string>();

        Assert.Equal("Rest day.", localizer.Translate("Rest day.", "en", missing));
        Assert.Null(localizer.Translate(null, "pl", missing));
        Assert.Equal(string.Empty, localizer.Translate(string.Empty, "pl", missing));
        Assert.Empty(missing);
        Assert.False(localizer.HasMap("en"));
        Assert.False(localizer.HasMap("pl"));
        Assert.False(localizer.HasMap("../pl"));
        Assert.False(localizer.HasMap("de"));
    }

    [Fact]
    public void A_mapped_text_is_translated_and_an_unmapped_one_falls_back_to_english_and_is_reported()
    {
        WriteMap("pl", """{ "version": 1, "texts": { "Rest day.": "Dzien odpoczynku." } }""");
        var localizer = Create();
        var missing = new List<string>();

        Assert.Equal("Dzien odpoczynku.", localizer.Translate("Rest day.", "pl", missing));
        Assert.Equal("Shake-out.", localizer.Translate("Shake-out.", "pl", missing));

        Assert.Equal(["Shake-out."], missing);
        Assert.True(localizer.HasMap("pl"));
    }

    [Fact]
    public void A_missing_file_means_everything_falls_back_to_english()
    {
        var missing = new List<string>();

        Assert.Equal("Rest day.", Create().Translate("Rest day.", "pl", missing));

        Assert.Equal(["Rest day."], missing);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "version": 2, "texts": {} }""")]
    [InlineData("""{ "version": 1 }""")]
    [InlineData("""{ "version": 1, "texts": { "a": "" } }""")]
    [InlineData("""{ "version": 1, "texts": {}, "extra": 1 }""")]
    public void A_malformed_map_throws(string json)
    {
        WriteMap("pl", json);

        Assert.Throws<InvalidOperationException>(() => Create().Translate("a", "pl"));
    }

    [Fact]
    public void An_unsupported_language_is_rejected_before_any_path_is_built()
    {
        Assert.Throws<ArgumentException>(() => Create().Translate("a", "../../etc/passwd"));
    }
}
