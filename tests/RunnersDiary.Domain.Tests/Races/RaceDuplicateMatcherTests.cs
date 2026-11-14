using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceDuplicateMatcherTests
{
    private static readonly DateOnly Day = new(2027, 3, 14);
    private static readonly GeoPoint Gliwice = new(50.2945, 18.6714);
    private static readonly DateTime T0 = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RaceDuplicateCandidate C(
        string name,
        DateOnly? date = null,
        GeoPoint? point = null,
        string? locality = "Gliwice",
        DateTime? imported = null,
        RaceSource source = RaceSource.RunningLife,
        Guid? id = null,
        bool hasPoint = true) =>
        new(id ?? Guid.NewGuid(), source, name, date ?? Day, hasPoint ? point ?? Gliwice : null, locality, imported ?? T0);

    [Theory]
    [InlineData("XII Bieg Gliwicki", new[] { "gliwicki" })]
    [InlineData("Bieg Gliwicki 2027", new[] { "gliwicki" })]
    [InlineData("12. Bieg Niepodległości", new[] { "niepodleglosci" })]
    [InlineData("12th Gliwice Run", new[] { "gliwice" })]
    [InlineData("3-ci Półmaraton Rybnicki", new[] { "polmaraton", "rybnicki" })]
    [InlineData("Edycja IX Łódź Maraton", new[] { "lodz", "maraton" })]
    [InlineData("Bieg na 10 km", new[] { "10", "km", "na" })]
    [InlineData("Gliwice Night 10K", new[] { "10k", "gliwice", "night" })]
    public void Name_tokens_drop_noise_editions_and_years(string name, string[] expected) =>
        Assert.Equal(expected, RaceDuplicateMatcher.NameTokens(name));

    [Fact]
    public void Edition_and_year_variants_of_the_same_race_match()
    {
        var a = C("XII Bieg Gliwicki");
        var b = C("Bieg Gliwicki 2027");

        Assert.Equal(1.0, RaceDuplicateMatcher.Similarity(a.Name, b.Name));
        Assert.True(RaceDuplicateMatcher.IsDuplicate(a, b));
    }

    [Fact]
    public void Diacritics_and_case_are_folded()
    {
        Assert.True(RaceDuplicateMatcher.IsDuplicate(C("Łódź Maraton"), C("LODZ MARATON")));
    }

    [Fact]
    public void Similarity_threshold_is_inclusive_at_point_eight()
    {
        // 4 shared of 5 distinct tokens = 0.8 (match); 3 of 5 = 0.6 (no match).
        Assert.Equal(0.8, RaceDuplicateMatcher.Similarity("alfa beta gamma delta epsilon", "alfa beta gamma delta"), 10);
        Assert.True(RaceDuplicateMatcher.IsDuplicate(C("alfa beta gamma delta epsilon"), C("alfa beta gamma delta")));
        Assert.Equal(0.6, RaceDuplicateMatcher.Similarity("alfa beta gamma delta epsilon", "alfa beta gamma"), 10);
        Assert.False(RaceDuplicateMatcher.IsDuplicate(C("alfa beta gamma delta epsilon"), C("alfa beta gamma")));
    }

    [Fact]
    public void Different_names_do_not_match()
    {
        Assert.False(RaceDuplicateMatcher.IsDuplicate(C("Bieg Gliwicki"), C("Bieg Zabrzański")));
    }

    [Fact]
    public void Names_made_only_of_noise_match_only_when_equal()
    {
        Assert.Equal(1.0, RaceDuplicateMatcher.Similarity("Bieg", "bieg"));
        Assert.Equal(0.0, RaceDuplicateMatcher.Similarity("Bieg", "Run"));
        Assert.Equal(0.0, RaceDuplicateMatcher.Similarity("Bieg", "Bieg Gliwicki"));
        Assert.Equal(0.0, RaceDuplicateMatcher.Similarity(null, null));
    }

    [Fact]
    public void Points_four_kilometres_apart_do_not_match_but_two_do()
    {
        var north4 = new GeoPoint(Gliwice.Latitude + 0.036, Gliwice.Longitude);
        var north2 = new GeoPoint(Gliwice.Latitude + 0.018, Gliwice.Longitude);
        Assert.InRange(Haversine.Distance(Gliwice.Latitude, Gliwice.Longitude, north4.Latitude, north4.Longitude), 3_900, 4_100);
        Assert.InRange(Haversine.Distance(Gliwice.Latitude, Gliwice.Longitude, north2.Latitude, north2.Longitude), 1_900, 2_100);

        Assert.False(RaceDuplicateMatcher.IsDuplicate(C("Bieg Gliwicki"), C("Bieg Gliwicki", point: north4)));
        Assert.True(RaceDuplicateMatcher.IsDuplicate(C("Bieg Gliwicki"), C("Bieg Gliwicki", point: north2)));
    }

    [Fact]
    public void Different_dates_do_not_match()
    {
        Assert.False(RaceDuplicateMatcher.IsDuplicate(C("Bieg Gliwicki"), C("Bieg Gliwicki", date: Day.AddDays(1))));
    }

    [Fact]
    public void Without_a_point_the_same_folded_locality_decides()
    {
        var noPointGliwice = C("Bieg Gliwicki", hasPoint: false, locality: "GLIWICE");
        var noPointZabrze = C("Bieg Gliwicki", hasPoint: false, locality: "Zabrze");
        var withPoint = C("Bieg Gliwicki", locality: "Gliwice");

        Assert.False(RaceDuplicateMatcher.IsDuplicate(noPointGliwice, noPointZabrze));
        Assert.True(RaceDuplicateMatcher.IsDuplicate(noPointGliwice, withPoint));
        Assert.False(RaceDuplicateMatcher.IsDuplicate(noPointZabrze, withPoint));
        Assert.False(RaceDuplicateMatcher.IsDuplicate(C("Bieg Gliwicki", hasPoint: false, locality: null), C("Bieg Gliwicki", hasPoint: false, locality: null)));
    }

    [Fact]
    public void Canonical_is_the_older_import()
    {
        var older = C("XII Bieg Gliwicki", imported: T0);
        var newer = C("Bieg Gliwicki 2027", imported: T0.AddDays(7));

        var result = RaceDuplicateMatcher.Resolve([newer, older]);

        Assert.Null(result[older.Id]);
        Assert.Equal(older.Id, result[newer.Id]);
    }

    [Fact]
    public void Import_time_ties_fall_back_to_source_priority_then_id()
    {
        var running = C("Bieg Gliwicki", source: RaceSource.RunningLife, id: new Guid("00000000-0000-0000-0000-000000000009"));
        var demo = C("Bieg Gliwicki", source: RaceSource.Demo, id: new Guid("00000000-0000-0000-0000-000000000001"));
        var bySource = RaceDuplicateMatcher.Resolve([demo, running]);
        Assert.Null(bySource[running.Id]);
        Assert.Equal(running.Id, bySource[demo.Id]);

        var low = C("Bieg Gliwicki", id: new Guid("00000000-0000-0000-0000-000000000001"));
        var high = C("Bieg Gliwicki", id: new Guid("00000000-0000-0000-0000-000000000002"));
        var byId = RaceDuplicateMatcher.Resolve([high, low]);
        Assert.Null(byId[low.Id]);
        Assert.Equal(low.Id, byId[high.Id]);
    }

    [Fact]
    public void A_cluster_of_three_points_everyone_at_the_oldest()
    {
        var a = C("Bieg Gliwicki", imported: T0.AddDays(2));
        var b = C("XII Bieg Gliwicki", imported: T0);
        var c = C("Bieg Gliwicki 2027", imported: T0.AddDays(1));
        var other = C("Zupełnie inny bieg");

        var result = RaceDuplicateMatcher.Resolve([a, b, c, other]);

        Assert.Equal(b.Id, result[a.Id]);
        Assert.Null(result[b.Id]);
        Assert.Equal(b.Id, result[c.Id]);
        Assert.Null(result[other.Id]);
    }

    [Fact]
    public void Non_duplicates_stay_canonical_and_empty_input_is_empty()
    {
        var a = C("Bieg Gliwicki");
        var b = C("Bieg Gliwicki", date: Day.AddDays(7));

        var result = RaceDuplicateMatcher.Resolve([a, b]);

        Assert.Null(result[a.Id]);
        Assert.Null(result[b.Id]);
        Assert.Empty(RaceDuplicateMatcher.Resolve([]));
    }
}
