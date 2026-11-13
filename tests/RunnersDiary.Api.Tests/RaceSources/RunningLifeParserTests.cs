using System.Reflection;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.RaceSources.RunningLife;
using static RunnersDiary.Api.Tests.RaceSources.RaceSourceTestSupport;

namespace RunnersDiary.Api.Tests.RaceSources;

/// <summary>The pure HTML-to-facts parsing on synthetic fixtures (ADR 0035 §6-7).</summary>
public sealed class RunningLifeParserTests
{
    [Fact]
    public void A_listing_page_yields_the_valid_events_with_coordinates_region_codes_and_the_pagination()
    {
        var page = RunningLifeParser.ParseListing(Fixture("listing-p1.html"));

        Assert.Equal(7, page.EventCount);
        Assert.Equal(1, page.NonRunning); // the triathlon card is skipped, not rejected
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(3, page.Rejected); // foreign host, no date, foreign country
        Assert.Equal(3, page.MaxPage);
        Assert.Equal(0, page.MalformedJsonLd);

        var first = page.Items[0];
        Assert.Equal("bieg-testowy-w-gliwicach", first.ExternalId);
        Assert.Equal("Bieg Testowy w Gliwicach", first.Name);
        Assert.Equal(new DateOnly(2027, 3, 14), first.Date);
        Assert.Equal("Gliwice", first.LocationName);
        Assert.Equal("PL-24", first.RegionCode);
        Assert.Equal("PL", first.CountryCode);
        Assert.Equal(new GeoPoint(50.2945, 18.6714), first.Point);

        var series = page.Items[1];
        Assert.Equal("wymyslony-polmaraton-nocny/2027-04-03", series.ExternalId);
        Assert.Equal(new DateOnly(2027, 4, 3), series.Date); // a date-time startDate keeps its date
        Assert.Equal("PL-02", series.RegionCode); // the Polish "Województwo dolnośląskie" form
        Assert.Equal(new GeoPoint(51.1079, 17.0385), series.Point); // quoted coordinates

        Assert.Null(page.Items[2].Point);
        Assert.Equal([RaceKind.Road, RaceKind.Road, RaceKind.Trail], page.Items.Select(i => i.CardKind));
        Assert.DoesNotContain(page.Items, i => i.ExternalId.Contains("triathlon", StringComparison.Ordinal));
    }

    [Fact]
    public void The_card_type_gives_the_kind_and_a_detail_type_label_overrides_it()
    {
        var item = RunningLifeParser.ParseListing(Fixture("listing-p1.html")).Items[2]; // card: Bieg terenowy
        var detail = RunningLifeParser.ParseDetail("<html><body><span class=\"race-type\">Bieg górski</span></body></html>");

        Assert.Equal(RaceKind.Trail, RunningLifeParser.ToFacts(item, null).Kind);
        Assert.Equal(RaceKind.Mountain, RunningLifeParser.ToFacts(item, detail).Kind);
    }

    [Theory]
    [InlineData("Bieg po asfalcie", RaceKind.Road)]
    [InlineData("Bieg miejski", RaceKind.Road)]
    [InlineData("Bieg terenowy", RaceKind.Trail)]
    [InlineData("Bieg z przeszkodami", RaceKind.Ocr)]
    [InlineData("Backyard Ultra", RaceKind.Ultra)]
    public void Site_type_labels_map_to_kinds(string label, RaceKind kind) => Assert.Equal(kind, RaceNormalizer.ParseKind(label));

    [Theory]
    [InlineData("label-triathlon", "Triathlon")]
    [InlineData("label-duathlon", "Duathlon")]
    [InlineData("label-aquathlon", "Aquathlon")]
    [InlineData("label-multisport", "Multisport")]
    [InlineData("label-fitness", "Fitness")]
    [InlineData("label-xyz", "Cross-duathlon")]
    public void Non_running_cards_never_become_races(string cls, string label)
    {
        var html = ListingHtml(1, ("x-race", "X Race", "2027-03-01")).Replace("</body>",
            $"<section class=\"event-card-row\"><div class=\"event-card-distances\"><a href=\"https://running.life/pl/wydarzenie/x-race\" class=\"{cls}\">{label}</a></div></section></body>", StringComparison.Ordinal);

        var page = RunningLifeParser.ParseListing(html);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.NonRunning);
    }

    [Fact]
    public void The_class_is_the_fallback_when_the_label_text_is_unknown()
    {
        var html = ListingHtml(1, ("x-race", "X Race", "2027-03-01")).Replace("</body>",
            "<section class=\"event-card-row\"><div class=\"event-card-distances\"><a href=\"https://running.life/pl/wydarzenie/x-race\" class=\"label-trailrun\">???</a></div></section></body>", StringComparison.Ordinal);

        Assert.Equal(RaceKind.Trail, RunningLifeParser.ParseListing(html).Items.Single().CardKind);
    }

    [Fact]
    public void Html_and_script_text_in_a_name_is_kept_as_plain_text()
    {
        var item = RunningLifeParser.ParseListing(Fixture("listing-p1.html")).Items[2];

        Assert.Equal("Trail <b>Pogrubiony</b> & <script>alert(1)</script>", item.Name);
    }

    [Fact]
    public void Listing_facts_never_contain_the_description()
    {
        var item = RunningLifeParser.ParseListing(Fixture("listing-p1.html")).Items[0];
        var facts = RunningLifeParser.ToFacts(item, detail: null);

        var canonical = RaceFactsHasher.CanonicalJsonString(facts.Normalized());
        Assert.DoesNotContain("wymyslony", canonical, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nigdy nie moze", canonical, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://running.life/pl/wydarzenie/bieg-testowy-w-gliwicach", facts.SourceUrl);
        Assert.Equal(RaceSource.RunningLife, facts.Source);
        Assert.True(RaceFacts.TryNormalize(facts, out _, out _));
    }

    [Fact]
    public void A_detail_page_with_several_distances_yields_them_and_the_two_links_but_not_the_nearby_events()
    {
        var detail = RunningLifeParser.ParseDetail(Fixture("detail-multi-distance.html"));

        Assert.Equal([5_000, 10_000, 21_097.5, 42_195], detail.Distances); // "1 km dla dzieci" and the nearby 100 km card are not facts of this race
        Assert.Equal("http://www.bieg-testowy.example/pl/", detail.WebsiteUrl);
        Assert.Equal("https://zapisy.example/bieg-testowy?edycja=5", detail.SignupUrl);
        Assert.False(detail.IsEmpty);

        var item = RunningLifeParser.ParseListing(Fixture("listing-p1.html")).Items[0];
        var facts = RunningLifeParser.ToFacts(item, detail);
        Assert.Equal(detail.Distances, facts.DistancesMeters);
        Assert.Equal(detail.SignupUrl, facts.SignupUrl);
        Assert.Equal(detail.WebsiteUrl, facts.EventUrl);
        Assert.True(RaceFacts.TryNormalize(facts, out _, out _));
    }

    [Fact]
    public void A_detail_page_without_distances_has_none_and_a_javascript_link_is_never_taken()
    {
        var detail = RunningLifeParser.ParseDetail(Fixture("detail-no-distances.html"));

        Assert.Empty(detail.Distances);
        Assert.Equal("https://orientacja.example/zawody/7", detail.WebsiteUrl);
        Assert.Null(detail.SignupUrl);
        Assert.False(detail.IsEmpty);
        Assert.True(RunningLifeParser.ParseDetail("<html><body><p>nothing</p></body></html>").IsEmpty);
    }

    [Fact]
    public void A_malformed_json_ld_block_is_skipped_and_counted_and_the_good_block_still_parses()
    {
        var page = RunningLifeParser.ParseListing(Fixture("jsonld-malformed.html"));

        Assert.Equal(1, page.MalformedJsonLd);
        var item = Assert.Single(page.Items);
        Assert.Equal("bieg-ocalaly", item.ExternalId);
        Assert.Equal("PL-16", item.RegionCode);
    }

    [Fact]
    public void Json_deeper_than_32_levels_is_rejected_as_malformed()
    {
        var page = RunningLifeParser.ParseListing(Fixture("jsonld-deep.html"));

        Assert.Equal(1, page.MalformedJsonLd);
        Assert.Equal(0, page.EventCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public void A_page_in_a_new_layout_has_no_events()
    {
        var page = RunningLifeParser.ParseListing(Fixture("listing-empty-layout.html"));

        Assert.Equal(0, page.EventCount);
        Assert.Equal(1, page.MaxPage);
    }

    [Theory]
    [InlineData("https://running.life/pl/wydarzenie/abc", true, "abc")]
    [InlineData("https://running.life/pl/wydarzenie/abc/", true, "abc")]
    [InlineData("https://running.life/pl/wydarzenie/abc/2027-01-02?utm=1", true, "abc/2027-01-02")]
    [InlineData("http://running.life/pl/wydarzenie/abc", false, "")]
    [InlineData("https://www.running.life/pl/wydarzenie/abc", false, "")]
    [InlineData("https://running.life.evil.example/pl/wydarzenie/abc", false, "")]
    [InlineData("https://running.life:8443/pl/wydarzenie/abc", false, "")]
    [InlineData("https://user@running.life/pl/wydarzenie/abc", false, "")]
    [InlineData("https://running.life/en/event/abc", false, "")]
    [InlineData("https://running.life/pl/wydarzenie/", false, "")]
    [InlineData("https://running.life/pl/wydarzenie/a/../../admin", false, "")]
    public void Only_https_urls_on_the_source_host_under_the_event_path_are_accepted(string url, bool accepted, string externalId)
    {
        Assert.Equal(accepted, RunningLifeParser.TryReadDetailUrl(url, out _, out var id));
        Assert.Equal(externalId, id);
    }

    [Fact]
    public void No_race_facts_member_can_carry_a_description_or_other_prose()
    {
        var forbidden = new[] { "descr", "summary", "about", "text", "body", "html", "content", "image", "logo", "review", "result", "email", "phone", "note", "comment" };
        var members = typeof(RaceFacts).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();

        Assert.All(members, name => Assert.DoesNotContain(forbidden, f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            ["CountryCode", "Date", "DistancesMeters", "ElevationGainMeters", "EventUrl", "ExternalId", "Kind", "LocationName", "Name", "OrganizerName", "Point",
             "PriceAmount", "PriceCurrency", "RegionCode", "SignupDeadline", "SignupUrl", "Source", "SourceUrl", "StartTime", "Surface"],
            members.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void The_kind_comes_from_the_type_label_when_present_and_otherwise_from_the_name()
    {
        var item = RunningLifeParser.ParseListing(Fixture("listing-p1.html")).Items[0];
        var trail = RunningLifeParser.ParseDetail("<html><body><span class=\"race-type\">Bieg górski</span><span class=\"rounded-full\">21 km</span></body></html>");

        Assert.Equal(RaceKind.Mountain, RunningLifeParser.ToFacts(item, trail).Kind);
        Assert.Equal(RaceKind.Other, RunningLifeParser.ToFacts(item with { CardKind = null }, null).Kind);
        Assert.Equal(RaceKind.Trail, RunningLifeParser.ToFacts(item with { Name = "Trail Beskidy", CardKind = null }, null).Kind);
    }
}
